using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>
/// The guess of the player's position between the game's own reports (Aion2EntityDirectory.BestPosition): pulled into reach of every monster that
/// appears, pushed out of reach of every one left behind, not thrown across the map by one announced from afar, and moved to a group of them only
/// when nothing near the old guess lives on (the player was moved without a zone frame). Also the direction of the last dash.
/// </summary>
public static class SelfCheckPositionTrack
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Position between the game's reports (followed by the monsters coming and going):");
        var directory = new Aion2EntityDirectory();
        DateTime t0 = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc), now = t0;
        directory.Clock = () => now;
        bool ok = true;

        void Check(string what, bool good, string detail)
        {
            ok &= good;
            Console.WriteLine($"  -> {what}: {detail}: {(good ? "ok" : "FAIL")}");
        }

        string At() => directory.BestPosition is { } p ? $"({p.X / 100:0.0} m, {p.Y / 100:0.0} m)" : "none";
        bool Near(double x, double y, double metres) => directory.BestPosition is { } p && Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y)) <= metres * 100;

        // a report at the origin; five seconds later a monster appears 100 m east: he rode there, the guess is pulled to 22 m from it
        directory.NoteLocalPosition(0, 0);
        Check("an exact report", Near(0, 0, 0.01), At());
        now = t0.AddSeconds(5);
        directory.NoteMobPosition(1, 1170, 10000, 0);
        Check("a monster 100 m away appears", Near(7800, 0, 0.5), At());

        // one announced 3 km away: farther than he can have gone, the guess stays
        now = t0.AddSeconds(5.5);
        directory.NoteMobPosition(2, 1170, 300000, 0);
        Check("one announced 3 km away", Near(7800, 0, 0.5), At());

        // the first monster is taken away as out of range: he is at least 25 m from it
        now = t0.AddSeconds(6);
        directory.NoteRemoved(1, Aion2EntityDirectory.RemovedOutOfRange, now);
        Check("the monster left behind", Near(7500, 0, 0.5), At());

        // three monsters appear together far away, nothing near the guess has lived on for 5 s: he was moved there
        directory.NoteRemoved(2, Aion2EntityDirectory.RemovedOutOfRange, now);
        for (int i = 0; i < 3; i++)
        {
            now = t0.AddSeconds(20 + i * 0.5);
            directory.NoteMobPosition(10 + i, 1170, 500000 + i * 1000, 500000);
        }

        Check("moved without a zone frame", Near(500000 + 1000, 500000, 25), At());

        // standing among monsters that move: a group announced far away does not take the guess along
        now = t0.AddSeconds(30);
        directory.NoteLocalPosition(0, 0);
        for (int i = 0; i < 3; i++)
        {
            now = t0.AddSeconds(30.5 + i * 0.5);
            directory.NoteAround(20 + i, 500 * i, 300);
            directory.NoteMobPosition(30 + i, 1170, 400000 + i * 1000, 0);
        }

        Check("a far group while monsters move around him", Near(0, 0, 20), At());

        // the position frame's angle is the direction of the dash
        directory.NoteLocalFacing(-151.5);
        Check("the direction of the dash", directory.LocalFacing?.Degrees == -151.5, directory.LocalFacing?.Degrees.ToString("0.0") ?? "none");
        return ok;
    }
}
