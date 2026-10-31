using System.IO;
using AionDPS.Aion2.Protocol;
using OpenCvSharp;
using OpenCvSharp.Features2D;
using OpenCvSharp.Flann;

namespace AionDPS.Ui;

/// <summary>Where the game's own map (M) shows a map on the screen: screen = Scale × native map pixel + (OffsetX, OffsetY), in pixels of the game
/// window's client area. Inliers: how many picture features agreed (the higher, the surer).</summary>
public sealed record GameMapView(string MapKey, double Scale, double OffsetX, double OffsetY, int Inliers)
{
    public (double X, double Y) ToScreen(double nativeX, double nativeY) => (Scale * nativeX + OffsetX, Scale * nativeY + OffsetY);

    public (double X, double Y) ToNative(double screenX, double screenY) => ((screenX - OffsetX) / Scale, (screenY - OffsetY) / Scale);
}

/// <summary>
/// Finds the game's own map on a picture of the game window: the map pictures the meter ships with (the same artwork the game shows) are cut
/// into features once per map (SIFT, found again at any zoom; tried against ORB and AKAZE on pictures made from the tiles, SIFT found the map from
/// far out to far in where the others lost it); a picture of the screen is matched against them (a FLANN index, fast) and the zoom and the
/// position the map is shown at are worked out from the features that agree (RANSAC, scale and shift only). The map is then found again every few hundred milliseconds, near where it
/// was (a drag or a zoom moves it). Nothing is saved or sent: the picture is used in memory and dropped.
/// </summary>
public sealed class GameMapMatcher : IDisposable
{
    /// <summary>The features of one map: their native map position, their descriptors (SIFT, 128 numbers each) and the index they are looked up in.</summary>
    private sealed class MapFeatures : IDisposable
    {
        public required string Key { get; init; }
        public required Point2f[] Native { get; init; }
        public required Mat Descriptors { get; init; }
        public required FlannBasedMatcher Index { get; init; }

        public void Dispose()
        {
            Index.Dispose();
            Descriptors.Dispose();
        }
    }

    /// <summary>The width the screen picture is brought down to before it is looked at (fast enough, and enough detail).</summary>
    public const int WorkWidth = 1280;

    /// <summary>How many features agreeing make a match (fewer: the map is not open, or it is another map).</summary>
    public const int MinInliers = 24;

    private readonly Dictionary<string, MapFeatures> _maps = new();
    private readonly object _gate = new();

    /// <summary>OpenCV could not be loaded on this PC (its native part is missing): the feature is off.</summary>
    public static bool Unavailable { get; private set; }

    /// <summary>The features of a map, cut once (about a second for the largest map) and kept for the session.</summary>
    private MapFeatures? FeaturesOf(Aion2MapInfo map)
    {
        lock (_gate)
        {
            if (_maps.TryGetValue(map.Key, out MapFeatures? known))
            {
                return known;
            }
        }

        MapFeatures? built = Build(map);
        if (built is null)
        {
            return null;
        }

        lock (_gate)
        {
            if (_maps.TryGetValue(map.Key, out MapFeatures? raced))
            {
                built.Dispose();
                return raced;
            }

            _maps[map.Key] = built;
            return built;
        }
    }

    /// <summary>Whether the features of a map are ready (cut already).</summary>
    public bool Ready(string key)
    {
        lock (_gate)
        {
            return _maps.ContainsKey(key);
        }
    }

    /// <summary>
    /// Cuts a map's features at two sizes (about 4096 and 1400 pixels across), so the game's map is found zoomed far in and far out: the tiles are
    /// put together in grey, the sea (transparent in the mask) is left out, and each feature keeps its position in native map pixels.
    /// </summary>
    private static MapFeatures? Build(Aion2MapInfo map)
    {
        string folder = Aion2Maps.TileFolder(map);
        if (!Directory.Exists(folder))
        {
            return null;
        }

        double nativeSize = (double)map.Tile * map.Grid;
        var points = new List<Point2f>();
        var parts = new List<Mat>();
        try
        {
            foreach ((int across, int features) in new[] { (4096, 16000), (1400, 6000) })
            {
                int cell = Math.Max(16, (int)Math.Round(across / (double)map.Grid));
                double perNative = cell / (double)map.Tile;
                using var grey = new Mat(cell * map.Grid, cell * map.Grid, MatType.CV_8UC1, Scalar.All(0));
                using var mask = new Mat(cell * map.Grid, cell * map.Grid, MatType.CV_8UC1, Scalar.All(0));
                bool any = false;
                for (int col = 0; col < map.Grid; col++)
                {
                    for (int row = 0; row < map.Grid; row++)
                    {
                        string jpg = Path.Combine(folder, $"{col}_{row}.jpg"), png = Path.Combine(folder, $"{col}_{row}_a.png");
                        if (!File.Exists(jpg) || !File.Exists(png))
                        {
                            continue;
                        }

                        using Mat tile = Cv2.ImRead(jpg, ImreadModes.Grayscale);
                        using Mat alpha = AlphaOf(png);
                        if (tile.Empty() || alpha.Empty())
                        {
                            continue;
                        }

                        var at = new Rect(col * cell, row * cell, cell, cell);
                        using (var target = new Mat(grey, at))
                        {
                            Cv2.Resize(tile, target, new Size(cell, cell), 0, 0, InterpolationFlags.Area);
                        }

                        using (var target = new Mat(mask, at))
                        {
                            Cv2.Resize(alpha, target, new Size(cell, cell), 0, 0, InterpolationFlags.Area);
                        }

                        any = true;
                    }
                }

                if (!any)
                {
                    return null;
                }

                // the land only, a little inside its edge (the sea and the coast line look different in the game)
                Cv2.Threshold(mask, mask, 128, 255, ThresholdTypes.Binary);
                using (var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5)))
                {
                    Cv2.Erode(mask, mask, kernel);
                }

                using var sift = SIFT.Create(features);
                var descriptors = new Mat();
                sift.DetectAndCompute(grey, mask, out KeyPoint[] keys, descriptors);
                if (keys.Length == 0 || descriptors.Empty())
                {
                    descriptors.Dispose();
                    continue;
                }

                points.AddRange(keys.Select(k => new Point2f((float)(k.Pt.X / perNative), (float)(k.Pt.Y / perNative))));
                parts.Add(descriptors);
            }

            if (parts.Count == 0)
            {
                return null;
            }

            var all = new Mat();
            Cv2.VConcat(parts.ToArray(), all);
            var index = new FlannBasedMatcher(new KDTreeIndexParams(4), new SearchParams(48));
            index.Add(new[] { all });
            index.Train();
            return new MapFeatures { Key = map.Key, Native = points.ToArray(), Descriptors = all, Index = index };
        }
        finally
        {
            foreach (Mat part in parts)
            {
                part.Dispose();
            }
        }
    }

    /// <summary>The mask of a tile (white: land), from a grey, colour or transparent picture.</summary>
    private static Mat AlphaOf(string path)
    {
        Mat read = Cv2.ImRead(path, ImreadModes.Unchanged);
        if (read.Empty() || read.Channels() == 1)
        {
            return read;
        }

        var single = new Mat();
        if (read.Channels() == 4)
        {
            Cv2.ExtractChannel(read, single, 3);
        }
        else
        {
            Cv2.CvtColor(read, single, ColorConversionCodes.BGR2GRAY);
        }

        read.Dispose();
        return single;
    }

    /// <summary>
    /// Looks for the map on a grey picture of the game window (full size, <paramref name="screen"/>). With <paramref name="near"/> (where it was a
    /// moment ago) only the matches in that part of the map (and a margin) count, which is surer while it is dragged or zoomed.
    /// Null: not this map, or not open.
    /// </summary>
    public GameMapView? Find(Mat screen, Aion2MapInfo map, GameMapView? near = null)
    {
        if (FeaturesOf(map) is not { } features || screen.Empty())
        {
            return null;
        }

        double f = screen.Width > WorkWidth ? WorkWidth / (double)screen.Width : 1.0;
        using var work = new Mat();
        if (f < 1.0)
        {
            Cv2.Resize(screen, work, new Size((int)Math.Round(screen.Width * f), (int)Math.Round(screen.Height * f)), 0, 0, InterpolationFlags.Area);
        }
        else
        {
            screen.CopyTo(work);
        }

        using var sift = SIFT.Create(4000);
        using var descriptors = new Mat();
        sift.DetectAndCompute(work, null, out KeyPoint[] keys, descriptors);
        if (keys.Length < MinInliers || descriptors.Empty())
        {
            return null;
        }

        // near where the map was a moment ago: only the matches in that part of the map (and a margin) count, which keeps a drag or a zoom surer
        (double X0, double Y0, double X1, double Y1)? region = null;
        if (near is not null && near.MapKey == map.Key)
        {
            var (x0, y0) = near.ToNative(0, 0);
            var (x1, y1) = near.ToNative(screen.Width, screen.Height);
            double mx = (x1 - x0) * 0.6, my = (y1 - y0) * 0.6;
            region = (x0 - mx, y0 - my, x1 + mx, y1 + my);
        }

        DMatch[][] pairs;
        lock (features)
        {
            pairs = features.Index.KnnMatch(descriptors, 2);
        }

        var from = new List<Point2f>();
        var to = new List<Point2f>();
        foreach (DMatch[] pair in pairs)
        {
            if (pair.Length < 2 || pair[0].Distance >= 0.8f * pair[1].Distance)
            {
                continue;
            }

            Point2f native = features.Native[pair[0].TrainIdx];
            if (region is { } r && (native.X < r.X0 || native.X > r.X1 || native.Y < r.Y0 || native.Y > r.Y1))
            {
                continue;
            }

            from.Add(native);
            to.Add(new Point2f((float)(keys[pair[0].QueryIdx].Pt.X / f), (float)(keys[pair[0].QueryIdx].Pt.Y / f)));
        }

        if (from.Count < MinInliers)
        {
            return null;
        }

        using var fromMat = Mat.FromArray(from.ToArray());
        using var toMat = Mat.FromArray(to.ToArray());
        using var inliers = new Mat();
        using Mat? fit = Cv2.EstimateAffinePartial2D(fromMat, toMat, inliers, RobustEstimationAlgorithms.RANSAC, 4.0 / f, 4000, 0.995, 10);
        if (fit is null || fit.Empty())
        {
            return null;
        }

        double a = fit.At<double>(0, 0), b = fit.At<double>(1, 0);
        double scale = Math.Sqrt(a * a + b * b), turn = Math.Abs(Math.Atan2(b, a)) * 180 / Math.PI;
        int agree = Cv2.CountNonZero(inliers);
        // the game's map is never turned; a turned fit is chance
        if (agree < MinInliers || agree < from.Count * 0.2 || turn > 1.5 || scale < 0.01 || scale > 12)
        {
            return null;
        }

        return new GameMapView(map.Key, scale, fit.At<double>(0, 2), fit.At<double>(1, 2), agree);
    }

    /// <summary>A grey picture of a part of the screen (the game window's client area), in memory only.</summary>
    public static Mat? Capture(System.Drawing.Rectangle area)
    {
        if (area.Width < 200 || area.Height < 200)
        {
            return null;
        }

        using var frame = new System.Drawing.Bitmap(area.Width, area.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(frame))
        {
            g.CopyFromScreen(area.Location, System.Drawing.Point.Empty, area.Size);
        }

        return GreyOf(frame);
    }

    /// <summary>A picture as a grey OpenCV matrix.</summary>
    public static Mat GreyOf(System.Drawing.Bitmap bitmap)
    {
        var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        System.Drawing.Imaging.BitmapData data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            using Mat bgra = Mat.FromPixelData(bitmap.Height, bitmap.Width, MatType.CV_8UC4, data.Scan0, data.Stride);
            var grey = new Mat();
            Cv2.CvtColor(bgra, grey, ColorConversionCodes.BGRA2GRAY);
            return grey;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>Whether OpenCV's native part loads on this PC (asked once; a missing part turns the feature off instead of crashing).</summary>
    public static bool Available()
    {
        if (Unavailable)
        {
            return false;
        }

        try
        {
            using var probe = new Mat(4, 4, MatType.CV_8UC1, Scalar.All(0));
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or BadImageFormatException or EntryPointNotFoundException)
        {
            Unavailable = true;
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (MapFeatures features in _maps.Values)
            {
                features.Dispose();
            }

            _maps.Clear();
        }
    }
}
