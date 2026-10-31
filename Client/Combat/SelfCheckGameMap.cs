using System.Diagnostics;
using System.IO;
using AionDPS.Aion2.Protocol;
using AionDPS.Ui;
using OpenCvSharp;

namespace AionDPS.Combat;

/// <summary>
/// The game's map found on a picture of the screen (Ui/GameMapMatcher.cs): pictures made from the map tiles at a known zoom and place, a bit
/// darker and blurred, with a side panel, icons and names drawn over them like the game's map has, must be found back to within a few pixels,
/// also while dragged a little (the quick search near the last place), and a picture without the map must not be taken for it.
/// </summary>
public static class SelfCheckGameMap
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Game map (M) found on a picture of the screen:");
        if (!GameMapMatcher.Available())
        {
            Console.WriteLine("  -> OpenCV did not load: FAIL");
            return false;
        }

        Aion2MapInfo? map = Aion2Maps.All.FirstOrDefault(m => m.Key == "altgard");
        if (map is null)
        {
            Console.WriteLine("  -> no Altgard map: FAIL");
            return false;
        }

        using var matcher = new GameMapMatcher();
        bool ok = true;
        var clock = Stopwatch.StartNew();
        using (Mat warm = Shot(map, 0.3, 4500, 3000, 1920, 1080, false, out _, out _))
        {
            matcher.Find(warm, map); // the map's features are cut on the first look
        }

        Console.WriteLine($"  -> features of the map cut in {clock.ElapsedMilliseconds} ms");
        foreach (var (scale, cx, cy) in new[] { (0.12, 3500.0, 3800.0), (0.3, 4500.0, 3000.0), (0.8, 2500.0, 3200.0), (1.6, 5300.0, 5400.0) })
        {
            using Mat shot = Shot(map, scale, cx, cy, 1920, 1080, false, out double tx, out double ty);
            clock.Restart();
            GameMapView? found = matcher.Find(shot, map);
            long took = clock.ElapsedMilliseconds;
            double error = found is null ? double.PositiveInfinity : Error(found, scale, tx, ty);
            bool good = error < 3;
            ok &= good;
            Console.WriteLine($"  -> zoom {scale}: {(found is null ? "not found" : $"found at {found.Scale:0.0000}, {found.Inliers} features agree")}, off by {error:0.00} px at most, {took} ms: {(good ? "ok" : "FAIL")}");

            // dragged by 37 px and zoomed 4 %: found again near the last place
            using Mat moved = Shot(map, scale * 1.04, cx - 37 / scale, cy + 21 / scale, 1920, 1080, false, out double tx2, out double ty2);
            clock.Restart();
            GameMapView? again = found is null ? null : matcher.Find(moved, map, found);
            double error2 = again is null ? double.PositiveInfinity : Error(again, scale * 1.04, tx2, ty2);
            bool tracked = error2 < 3;
            ok &= tracked;
            Console.WriteLine($"     dragged and zoomed: off by {error2:0.00} px, {clock.ElapsedMilliseconds} ms: {(tracked ? "ok" : "FAIL")}");
        }

        // no map on the screen (the game's world, all over the picture): nothing found
        using (var noise = new Mat(1080, 1920, MatType.CV_8UC1))
        {
            Cv2.Randu(noise, Scalar.All(0), Scalar.All(255));
            Cv2.GaussianBlur(noise, noise, new Size(9, 9), 3);
            bool none = matcher.Find(noise, map) is null;
            ok &= none;
            Console.WriteLine($"  -> a picture without the map: {(none ? "nothing found, ok" : "FAIL, found a map")}");
        }

        Console.WriteLine(ok ? "  -> game map: ok" : "  -> game map: FAIL");
        return ok;
    }

    /// <summary>The largest distance, over the corners and the middle of the picture, between where a map point is and where it was found.</summary>
    private static double Error(GameMapView found, double scale, double tx, double ty)
    {
        double worst = 0;
        foreach (var (sx, sy) in new[] { (400.0, 100.0), (1900.0, 100.0), (400.0, 1060.0), (1900.0, 1060.0), (1100.0, 540.0) })
        {
            double nx = (sx - tx) / scale, ny = (sy - ty) / scale;
            var (fx, fy) = found.ToScreen(nx, ny);
            worst = Math.Max(worst, Math.Max(Math.Abs(fx - sx), Math.Abs(fy - sy)));
        }

        return worst;
    }

    /// <summary>
    /// A picture of the screen with the game's map open, made from the map tiles: native point (cx, cy) at 55 % across and halfway down, at
    /// <paramref name="scale"/> screen pixels per native pixel; the sea in a dark blue, the land a little darker and blurred, a panel on the left,
    /// a bar at the top, icons and place names over the map. Grey, or in colour for the pictures.
    /// </summary>
    public static Mat Shot(Aion2MapInfo map, double scale, double cx, double cy, int width, int height, bool colour, out double tx, out double ty)
    {
        tx = width * 0.55 - scale * cx;
        ty = height * 0.5 - scale * cy;
        MatType type = colour ? MatType.CV_8UC3 : MatType.CV_8UC1;
        Scalar sea = colour ? new Scalar(52, 44, 38) : Scalar.All(44);
        var shot = new Mat(height, width, type, sea);
        string folder = Aion2Maps.TileFolder(map);
        for (int col = 0; col < map.Grid; col++)
        {
            for (int row = 0; row < map.Grid; row++)
            {
                double left = tx + scale * col * map.Tile, top = ty + scale * row * map.Tile, size = scale * map.Tile;
                if (left > width || top > height || left + size < 0 || top + size < 0)
                {
                    continue;
                }

                string jpg = Path.Combine(folder, $"{col}_{row}.jpg"), png = Path.Combine(folder, $"{col}_{row}_a.png");
                if (!File.Exists(jpg) || !File.Exists(png))
                {
                    continue;
                }

                using Mat tile = Cv2.ImRead(jpg, colour ? ImreadModes.Color : ImreadModes.Grayscale);
                using Mat alphaRead = Cv2.ImRead(png, ImreadModes.Grayscale);
                using var alpha = new Mat();
                Cv2.Resize(alphaRead, alpha, tile.Size(), 0, 0, InterpolationFlags.Linear);
                using var land = new Mat();
                Cv2.Threshold(alpha, land, 128, 255, ThresholdTypes.BinaryInv);
                tile.SetTo(sea, land);
                double k = scale * map.Tile / tile.Width;
                using var placement = new Mat(2, 3, MatType.CV_64FC1);
                placement.Set(0, 0, k);
                placement.Set(0, 1, 0.0);
                placement.Set(0, 2, left);
                placement.Set(1, 0, 0.0);
                placement.Set(1, 1, k);
                placement.Set(1, 2, top);
                Cv2.WarpAffine(tile, shot, placement, shot.Size(), k < 1 ? InterpolationFlags.Area : InterpolationFlags.Linear, BorderTypes.Transparent);
            }
        }

        shot.ConvertTo(shot, -1, 0.82, 12);
        Cv2.GaussianBlur(shot, shot, new Size(3, 3), 0.6);
        var rnd = new Random(7);
        Scalar panel = colour ? new Scalar(30, 25, 25) : Scalar.All(27);
        Cv2.Rectangle(shot, new Rect(0, 0, 360, height), panel, -1);
        Cv2.Rectangle(shot, new Rect(0, 0, width, 70), panel, -1);
        for (int i = 0; i < 40; i++)
        {
            Scalar icon = colour ? new Scalar(rnd.Next(100, 255), rnd.Next(100, 255), rnd.Next(100, 255)) : Scalar.All(rnd.Next(100, 255));
            Cv2.Circle(shot, new Point(rnd.Next(380, width - 40), rnd.Next(90, height - 40)), rnd.Next(6, 14), icon, -1);
        }

        for (int i = 0; i < 25; i++)
        {
            Cv2.PutText(shot, "Village of Lights", new Point(rnd.Next(380, width - 200), rnd.Next(90, height - 20)), HersheyFonts.HersheySimplex, 0.6, Scalar.All(230), 2);
        }

        return shot;
    }
}
