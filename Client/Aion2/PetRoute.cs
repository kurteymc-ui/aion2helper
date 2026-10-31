using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AionDPS.Aion2;

/// <summary>A dense spawn spot of a pet: where (metres, the game's X and Y over 100), how many of its monsters spawn there, and the area's name.</summary>
public sealed record PetRouteSpot(double X, double Z, int Count, string Zone);

/// <summary>A teleport hop before a stop: the Kibelisk to teleport to and how far the stop is from it (metres).</summary>
public sealed record PetRouteTeleport(string Name, double X, double Z, int Distance);

/// <summary>How to get to a stop on a windstream island ("wind"), or back from one ("leave").</summary>
public sealed record PetRouteVia(string Kind, string Island, string? Teleport, string How, string Leave);

/// <summary>One stop of the route: where to stand (metres), how far its spots reach, the area, the island it is on (empty: the mainland), the pets
/// farmed there (meter pet ids, by English name) and the spot of each.</summary>
public sealed record PetRouteStop(int Step, double X, double Z, double Radius, string Zone, string Island, IReadOnlyList<int> Pets,
    IReadOnlyDictionary<int, PetRouteSpot> At, PetRouteTeleport? Teleport = null, PetRouteVia? Via = null);

/// <summary>A route: the map, where it starts (the camp, or where the player is), and the stops in the order to farm them.</summary>
public sealed record PetRoutePlan(string MapKey, double OriginX, double OriginZ, string OriginLabel, bool FromPlayer, IReadOnlyList<PetRouteStop> Stops);

/// <summary>
/// The pet farming route of aion2helper.com's Pet Soul Map Atlas (guides/pets-route-map.html, buildRoute), the same steps in C#: each pet is on
/// the route once, at one of its dense spawn spots (at least 60 % of the spawns of its densest one); stops go where the most pets have a dense spot
/// within 260 m; the mainland's stops are ordered for the shortest way with teleports counted like 200 m of walking (every first stop tried,
/// then improved by turning parts round), the windstream islands come after the mainland. The data (assets/aion2/pets/route.json) is made from the
/// site's own by Tools/pet-route/make-route.mjs, so the meter plans the route the site shows, from the camp or from where the player stands.
/// </summary>
public static class PetRoute
{
    /// <summary>The genera the route can take (the special pets come from shops and events, never from monsters).</summary>
    public static readonly string[] Genera = { "Cogni", "Natura", "Fera", "Varian" };

    private sealed record Teleport(string Type, string Name, double X, double Z);

    private sealed record Island(string Teleport, string? Kibelisk, string How, string Leave);

    private sealed record MapData(string Site, Dictionary<string, (string Name, double X, double Z)> Starts, List<Teleport> Tps,
        Dictionary<string, Island> Islands, List<(int Id, string Name, List<PetRouteSpot> Spots)> Clusters);

    private sealed record Data(double StopRadius, double GoodSpot, double TeleportCost, Dictionary<int, string> Genus, Dictionary<string, MapData> Maps);

    private static Data? _data;
    private static readonly object Gate = new();
    private static readonly Regex SkyIsland = new("Sky Island", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The route data from a file (the shipped one when null); for the checks.</summary>
    private static Data Load(string? path = null)
    {
        lock (Gate)
        {
            if (_data is not null && path is null)
            {
                return _data;
            }

            string file = path ?? Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "pets", "route.json");
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
            JsonElement root = doc.RootElement;
            var genus = new Dictionary<int, string>();
            foreach (JsonProperty p in root.GetProperty("genus").EnumerateObject())
            {
                if (int.TryParse(p.Name, out int id))
                {
                    genus[id] = p.Value.GetString() ?? "";
                }
            }

            var maps = new Dictionary<string, MapData>();
            foreach (JsonProperty mp in root.GetProperty("maps").EnumerateObject())
            {
                JsonElement m = mp.Value;
                var starts = new Dictionary<string, (string, double, double)>();
                foreach (JsonProperty s in m.GetProperty("starts").EnumerateObject())
                {
                    starts[s.Name] = (s.Value[0].GetString() ?? "", s.Value[1].GetDouble(), s.Value[2].GetDouble());
                }

                var tps = m.GetProperty("tps").EnumerateArray().Select(t => new Teleport(t[0].GetString() ?? "", t[1].GetString() ?? "", t[2].GetDouble(), t[3].GetDouble())).ToList();
                var islands = new Dictionary<string, Island>();
                foreach (JsonProperty i in m.GetProperty("islands").EnumerateObject())
                {
                    JsonElement v = i.Value;
                    islands[i.Name] = new Island(v.GetProperty("tp").GetString() ?? "", v.GetProperty("kibelisk").ValueKind == JsonValueKind.String ? v.GetProperty("kibelisk").GetString() : null,
                        v.GetProperty("how").GetString() ?? "", v.GetProperty("leave").GetString() ?? "");
                }

                var clusters = m.GetProperty("clusters").EnumerateArray().Select(c => (c[0].GetInt32(), c[1].GetString() ?? "",
                    c[2].EnumerateArray().Select(q => new PetRouteSpot(q[0].GetDouble(), q[1].GetDouble(), q[2].GetInt32(), q[3].GetString() ?? "")).ToList())).ToList();
                maps[mp.Name] = new MapData(m.GetProperty("site").GetString() ?? mp.Name, starts, tps, islands, clusters);
            }

            var data = new Data(root.GetProperty("stopRadius").GetDouble(), root.GetProperty("goodSpot").GetDouble(), root.GetProperty("teleportCost").GetDouble(), genus, maps);
            if (path is null)
            {
                _data = data;
            }

            return data;
        }
    }

    /// <summary>The genus of a pet as the site names it (Cogni, Natura, Fera, Varian, Special), null for a pet it does not list.</summary>
    public static string? GenusOf(int petId) => Load().Genus.TryGetValue(petId, out string? g) ? g : null;

    /// <summary>Whether a map (Aion2MapInfo key: verteron, altgard, abyss) has a route.</summary>
    public static bool Covers(string mapKey) => Load().Maps.ContainsKey(mapKey);

    /// <summary>The area a place is in (metres), as the route names them: the area of the nearest dense spawn spot within 400 m, else empty
    /// (the islands are told apart by it).</summary>
    public static string ZoneNear(string mapKey, double x, double z)
    {
        if (!Load().Maps.TryGetValue(mapKey, out MapData? m))
        {
            return "";
        }

        string zone = "";
        double best = 400;
        foreach (var (_, _, spots) in m.Clusters)
        {
            foreach (PetRouteSpot q in spots)
            {
                double d = Dist((x, z), (q.X, q.Z));
                if (d < best)
                {
                    best = d;
                    zone = q.Zone;
                }
            }
        }

        return zone;
    }

    /// <summary>The site's English name of a pet on the route (the order of the pets of a stop follows it).</summary>
    public static string? NameOf(int petId) => Load().Maps.Values.SelectMany(m => m.Clusters).FirstOrDefault(c => c.Id == petId).Name;

    /// <summary>
    /// Plans the route of a map. <paramref name="faction"/>: "Elyos" or "Asmodian" (which camp it starts at). <paramref name="needed"/>: the pets still
    /// to farm. <paramref name="from"/>: where the player stands (metres and the area's name); null starts at the camp. <paramref name="pin"/>: the stop
    /// being farmed, which stays the first while it still has pets to farm.
    /// </summary>
    public static PetRoutePlan? Plan(string mapKey, string? faction, IReadOnlySet<string> genera, Func<int, bool> needed,
        (double X, double Z, string Zone)? from = null, PetRouteStop? pin = null) => PlanWith(Load(), mapKey, faction, genera, needed, from, pin);

    /// <summary>The same with the route data of a file (the checks).</summary>
    internal static PetRoutePlan? PlanFrom(string path, string mapKey, string? faction, IReadOnlySet<string> genera, Func<int, bool> needed, (double X, double Z, string Zone)? from = null) =>
        PlanWith(Load(path), mapKey, faction, genera, needed, from, null);

    private static double Dist((double X, double Z) a, (double X, double Z) b) => Hypot(a.X - b.X, a.Z - b.Z);

    /// <summary>
    /// The distance as JavaScript's Math.hypot works it out (V8: scaled by the larger value, the squares added with Kahan's compensation): the plain
    /// square root differs from it in the last digit now and then, and where two orders of the stops cost the same to the last digit (teleports cost
    /// the same 200 m), that decided between them. With this the meter plans exactly the site's route.
    /// </summary>
    private static double Hypot(double x, double y)
    {
        x = Math.Abs(x);
        y = Math.Abs(y);
        double max = Math.Max(x, y);
        if (double.IsInfinity(max))
        {
            return double.PositiveInfinity;
        }

        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return double.NaN;
        }

        if (max == 0)
        {
            return 0;
        }

        double sum = 0, compensation = 0;
        foreach (double v in new[] { x / max, y / max })
        {
            double summand = v * v - compensation;
            double preliminary = sum + summand;
            compensation = (preliminary - sum) - summand;
            sum = preliminary;
        }

        return Math.Sqrt(sum) * max;
    }

    private static double Dist((double X, double Z)? a, (double X, double Z) b) => a is { } p ? Dist(p, b) : double.NaN;

    private static PetRoutePlan? PlanWith(Data data, string mapKey, string? faction, IReadOnlySet<string> genera, Func<int, bool> needed,
        (double X, double Z, string Zone)? from, PetRouteStop? pin)
    {
        if (!data.Maps.TryGetValue(mapKey, out MapData? m))
        {
            return null;
        }

        string side = string.Equals(faction, "Elyos", StringComparison.OrdinalIgnoreCase) ? "elyos" : "asmodian";
        double stopR = data.StopRadius, goodShare = data.GoodSpot, tpCost = data.TeleportCost;
        string IslandOf(string zone) => zone.Length > 0 && m.Islands.ContainsKey(zone) ? zone : "";
        var names = m.Clusters.Where(c => data.Genus.TryGetValue(c.Id, out string? g) && genera.Contains(g) && needed(c.Id)).ToList();
        var nameOf = names.ToDictionary(c => c.Id, c => c.Name);
        var pinned = pin is null ? new List<int>() : pin.Pets.Where(id => nameOf.ContainsKey(id)).ToList();
        var camp = m.Starts.TryGetValue(side, out var own) ? own : m.Starts.Values.First();

        // density first: a pet may only be farmed at one of its dense spots; among those, stops go where the most pets have a dense spot close together
        var best = new Dictionary<int, int>();
        var good = new Dictionary<int, List<PetRouteSpot>>();
        foreach (var (id, _, spots) in names)
        {
            int b = spots.Max(c => c.Count);
            best[id] = b;
            good[id] = spots.Where(c => c.Count >= Math.Max(Math.Min(3, b), b * goodShare)).ToList();
        }

        var cands = new List<PetRouteSpot>();
        foreach (var (id, _, _) in names)
        {
            cands.AddRange(good[id]);
        }

        var left = names.Select(c => c.Id).Where(id => !pinned.Contains(id)).ToList();
        var stops = new List<(double X, double Z, double R, string Island, List<int> Pets, Dictionary<int, PetRouteSpot> At, string Zone)>();
        while (left.Count > 0)
        {
            List<(int Id, PetRouteSpot Q)>? bestCov = null;
            double bestW = -1;
            foreach (PetRouteSpot c in cands)
            {
                var cov = new List<(int, PetRouteSpot)>();
                double w = 0;
                foreach (int n in left)
                {
                    (double D, PetRouteSpot Q)? near = null;
                    foreach (PetRouteSpot q in good[n])
                    {
                        double d = Dist((c.X, c.Z), (q.X, q.Z));
                        if (d <= stopR && IslandOf(q.Zone) == IslandOf(c.Zone) && (near is not { } nr || q.Count > nr.Q.Count || (q.Count == nr.Q.Count && d < nr.D)))
                        {
                            near = (d, q);
                        }
                    }

                    if (near is { } found)
                    {
                        cov.Add((n, found.Q));
                        w += 1 + 0.5 * found.Q.Count / best[n];
                    }
                }

                if (w > bestW)
                {
                    bestW = w;
                    bestCov = cov;
                }
            }

            if (bestCov is null || bestCov.Count == 0)
            {
                break;
            }

            double cx = bestCov.Average(v => v.Q.X), cz = bestCov.Average(v => v.Q.Z);
            var pets = bestCov.Select(v => v.Id).OrderBy(id => nameOf[id], StringComparer.Ordinal).ToList();
            var at = bestCov.ToDictionary(v => v.Id, v => v.Q);
            // the area the stop is in: the one most of its spots are in (the first such in the order of the pets)
            var counts = new List<(string Zone, int Count)>();
            foreach (int id in pets)
            {
                string zone = at[id].Zone;
                int i = counts.FindIndex(z => z.Zone == zone);
                if (i < 0)
                {
                    counts.Add((zone, 1));
                }
                else
                {
                    counts[i] = (zone, counts[i].Count + 1);
                }
            }

            int top = counts.Max(z => z.Count);
            string area = counts.First(z => z.Count == top).Zone;
            stops.Add((cx, cz, Math.Max(bestCov.Max(v => Dist((cx, cz), (v.Q.X, v.Q.Z))), 0) + 50, IslandOf(bestCov[0].Q.Zone), pets, at, area.Length > 0 ? area : "Open area"));
            foreach (var (id, _) in bestCov)
            {
                left.Remove(id);
            }
        }

        PetRouteStop? pinStop = pinned.Count > 0
            ? new PetRouteStop(1, pin!.X, pin.Z, pin.Radius, pin.Zone, IslandOf(pin.Zone), pinned.OrderBy(id => nameOf[id], StringComparer.Ordinal).ToList(), pinned.ToDictionary(id => id, id => pin.At[id]))
            : null;
        (double X, double Z)? cur = pinStop is not null ? (pinStop.X, pinStop.Z) : from is { } f ? (f.X, f.Z) : (camp.X, camp.Z);
        string startIsl = pinStop is not null ? pinStop.Island : from is { } fz ? IslandOf(fz.Zone) : "";
        (double X, double Z)? TpAt(string name) => m.Tps.FirstOrDefault(t => t.Type == "t" && t.Name == name) is { } t ? (t.X, t.Z) : null;
        int Rank(string a) => a == startIsl ? 0 : a.Length == 0 ? 1 : m.Islands[a].Kibelisk is not null ? 2 : 3;
        var areas = stops.Select(s => s.Island).Distinct().OrderBy(Rank).ToList();
        var islandKibs = m.Islands.Values.Select(i => i.Kibelisk).Where(k => k is not null).ToHashSet();
        var kibs = m.Site == "reshanta" ? new List<Teleport>() : m.Tps.Where(t => t.Type == "t" && !islandKibs.Contains(t.Name) && !SkyIsland.IsMatch(t.Name)).ToList();
        (Teleport? T, double D) NearTp((double X, double Z) p)
        {
            Teleport? b = null;
            double bd = double.PositiveInfinity;
            foreach (Teleport t in kibs)
            {
                double d = Dist(p, (t.X, t.Z));
                if (d < bd)
                {
                    bd = d;
                    b = t;
                }
            }

            return (b, bd);
        }

        bool freeStart = pinStop is null && from is null && kibs.Count > 0;
        if (freeStart)
        {
            cur = null;
        }

        (double X, double Z) Sp(int i) => (stops[i].X, stops[i].Z);
        var order = new List<int>();
        foreach (string area in areas)
        {
            var rest = Enumerable.Range(0, stops.Count).Where(i => stops[i].Island == area).ToList();
            if (area.Length == 0 && kibs.Count > 0)
            {
                // mainland with teleports: try every first stop, walk or teleport to the next, then improve the order
                var tpD = rest.ToDictionary(i => i, i => NearTp(Sp(i)).D);
                double Leg((double X, double Z)? a, int i) => Math.Min(a is { } p ? Dist(p, Sp(i)) : double.PositiveInfinity, tpCost + tpD[i]);
                double Total(List<int> o)
                {
                    double c = 0;
                    (double X, double Z)? p = cur;
                    foreach (int i in o)
                    {
                        c += Leg(p, i);
                        p = Sp(i);
                    }

                    return c;
                }

                List<int>? bestOrder = null;
                double bestC = double.PositiveInfinity;
                foreach (int first in rest)
                {
                    var o = new List<int> { first };
                    var remaining = rest.Where(i => i != first).ToList();
                    while (remaining.Count > 0)
                    {
                        var p = Sp(o[^1]);
                        int bi = remaining[0];
                        foreach (int i in remaining)
                        {
                            if (Leg(p, i) < Leg(p, bi))
                            {
                                bi = i;
                            }
                        }

                        o.Add(bi);
                        remaining.Remove(bi);
                    }

                    double c = Total(o);
                    if (c < bestC)
                    {
                        bestC = c;
                        bestOrder = o;
                    }
                }

                bestOrder ??= new List<int>();
                bool improved = true;
                for (int pass = 0; improved && pass < 40; pass++)
                {
                    improved = false;
                    for (int a = 0; a < bestOrder.Count - 1; a++)
                    {
                        for (int b = a + 1; b < bestOrder.Count; b++)
                        {
                            var o = bestOrder.Take(a).Concat(bestOrder.Skip(a).Take(b - a + 1).Reverse()).Concat(bestOrder.Skip(b + 1)).ToList();
                            double c = Total(o);
                            if (c < bestC - 0.5)
                            {
                                bestOrder = o;
                                bestC = c;
                                improved = true;
                            }
                        }
                    }
                }

                order.AddRange(bestOrder);
                if (bestOrder.Count > 0)
                {
                    cur = Sp(bestOrder[^1]);
                }

                continue;
            }

            (double X, double Z)? entry = area.Length > 0 && area != startIsl ? TpAt(m.Islands[area].Teleport) ?? cur : cur;
            var sub = new List<int>();
            (double X, double Z)? c0 = entry;
            while (rest.Count > 0)
            {
                int bi = rest[0];
                foreach (int i in rest)
                {
                    if (Dist(c0, Sp(i)) < Dist(c0, Sp(bi)))
                    {
                        bi = i;
                    }
                }

                sub.Add(bi);
                rest.Remove(bi);
                c0 = Sp(bi);
            }

            (double X, double Z)? P(int k) => k < 0 ? entry : Sp(sub[k]);
            double D2((double X, double Z)? a, (double X, double Z)? b) => a is { } p && b is { } q ? Dist(p, q) : double.NaN;
            bool better = true;
            for (int pass = 0; better && pass < 80; pass++)
            {
                better = false;
                for (int a = -1; a < sub.Count - 2; a++)
                {
                    for (int b = a + 2; b < sub.Count; b++)
                    {
                        var pa = P(a);
                        var pb = P(a + 1);
                        var pc = P(b);
                        (double X, double Z)? pd = b + 1 < sub.Count ? P(b + 1) : null;
                        double now = D2(pa, pb) + (pd is not null ? D2(pc, pd) : 0), turned = D2(pa, pc) + (pd is not null ? D2(pb, pd) : 0);
                        if (turned < now - 0.5)
                        {
                            sub.Reverse(a + 1, b - a);
                            better = true;
                        }
                    }
                }
            }

            order.AddRange(sub);
            if (sub.Count > 0)
            {
                cur = Sp(sub[^1]);
            }
        }

        var groups = new List<PetRouteStop>();
        if (pinStop is not null)
        {
            groups.Add(pinStop);
        }

        foreach (int i in order)
        {
            var s = stops[i];
            groups.Add(new PetRouteStop(groups.Count + 1, s.X, s.Z, s.R, s.Zone, s.Island, s.Pets, s.At));
        }

        double ox, oz;
        string label;
        bool fromPlayer = pinStop is not null || from is not null;
        if (pinStop is not null)
        {
            (ox, oz, label) = (pinStop.X, pinStop.Z, "YOU · " + pinStop.Zone);
        }
        else if (from is { } start)
        {
            (ox, oz, label) = (start.X, start.Z, "YOU · " + start.Zone);
        }
        else
        {
            (ox, oz, label) = (camp.X, camp.Z, "START · " + camp.Name);
        }

        // the windstream islands: how to get there, and back
        string was = startIsl;
        for (int k = 0; k < groups.Count; k++)
        {
            PetRouteStop g = groups[k];
            if (g.Island != was)
            {
                if (g.Island.Length > 0)
                {
                    Island isl = m.Islands[g.Island];
                    groups[k] = g with { Via = new PetRouteVia("wind", g.Island, isl.Teleport, isl.How, isl.Leave) };
                }
                else
                {
                    Island? from2 = m.Islands.TryGetValue(was, out Island? i) ? i : null;
                    string how = from2 is not null && from2.Kibelisk is null
                        ? $"You are on {was}, which has no Kibelisk: use a teleport scroll, then continue here."
                        : "Teleport back to the mainland with a Kibelisk, then continue here.";
                    groups[k] = g with { Via = new PetRouteVia("leave", was, null, how, "") };
                }
            }

            was = g.Island;
        }

        // teleport hops on the mainland: when the nearest Kibelisk and a short walk beat walking from the last stop
        if (kibs.Count > 0)
        {
            (double X, double Z)? prev = freeStart ? null : (ox, oz);
            for (int k = 0; k < groups.Count; k++)
            {
                PetRouteStop g = groups[k];
                if (g.Island.Length == 0 && (g.Via is null || g.Via.Kind == "leave"))
                {
                    var (t, d) = NearTp((g.X, g.Z));
                    double walk = prev is { } p ? Dist(p, (g.X, g.Z)) : double.PositiveInfinity;
                    if (t is not null && tpCost + d < walk)
                    {
                        groups[k] = g with { Teleport = new PetRouteTeleport(t.Name, t.X, t.Z, (int)Math.Floor(d + 0.5)) };
                    }
                }

                prev = (g.X, g.Z);
            }

            if (freeStart && groups.Count > 0 && groups[0].Teleport is { } firstHop)
            {
                (ox, oz, label) = (firstHop.X, firstHop.Z, "START · Teleport: " + firstHop.Name);
            }
        }

        return new PetRoutePlan(mapKey, ox, oz, label, fromPlayer, groups);
    }
}
