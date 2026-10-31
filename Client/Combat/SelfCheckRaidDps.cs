namespace AionDPS.Combat;

/// <summary>rDPS (RaidDps): the maths against a worked example, the buff spans from buff events, and the pairs that don't stack.</summary>
public static class SelfCheckRaidDps
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] rDPS:");
        const int dps = 1, chanter = 2, cleric = 3;
        var mantra25 = new RaidBuff(RaidDps.ChanterMantra, chanter, 100, 25, false, new List<(long, long)> { (0, 60_000) });

        // Mantra at level 25, on the whole fight: +22.5 % amplification over a 174.9 % bucket, +5 % crit damage, +5 % smite, +10 % perfect,
        // each against the default character: 1.0818 × 1.0204 × 1.0341 × 1.0076 − 1 = 15.02 %.
        double gain = RaidDps.Gain(mantra25, BuffGainContext.Default);
        bool gainOk = Math.Abs(gain - 0.1502) < 0.0015;
        Console.WriteLine($"  -> Mantra L25 for the whole fight is worth {gain:P2} to its recipient (expected ~15.02 %): {gainOk}");

        var result = RaidDps.Compute(new[]
        {
            new RaidParticipant(dps, 10_000, new[] { mantra25 }, new Dictionary<int, long>(), 13),
            new RaidParticipant(chanter, 3_000, Array.Empty<RaidBuff>(), new Dictionary<int, long>(), 18),
        }, Array.Empty<RaidBuff>(), 60);
        double ndps = 10_000 / (1 + gain), rdpsChanter = 3_000 + ndps * gain;
        bool creditOk = Math.Abs(result[dps].Rdps - ndps) < 1 && Math.Abs(result[chanter].Rdps - rdpsChanter) < 1 && Math.Abs(result[chanter].Given - ndps * gain) < 1;
        Console.WriteLine($"  -> the DPS keeps {result[dps].Rdps:0} (nDPS), the Chanter is credited {result[chanter].Given:0}: {creditOk}");

        // Protective Light and Mantra don't stack: at equal levels Mantra wins, the Cleric's light counts only where Mantra was off.
        var light = new RaidBuff(RaidDps.ClericProtectLight, cleric, 100, 25, false, new List<(long, long)> { (0, 60_000) });
        var both = RaidDps.Compute(new[]
        {
            new RaidParticipant(dps, 10_000, new[] { mantra25, light }, new Dictionary<int, long>(), 13),
            new RaidParticipant(chanter, 3_000, Array.Empty<RaidBuff>(), new Dictionary<int, long>(), 18),
            new RaidParticipant(cleric, 2_000, Array.Empty<RaidBuff>(), new Dictionary<int, long>(), 17),
        }, Array.Empty<RaidBuff>(), 60);
        bool pairOk = both[cleric].Given < 1 && Math.Abs(both[chanter].Given - ndps * gain) < 1;
        Console.WriteLine($"  -> overlapping Protective Light and Mantra: only the Mantra counts: {pairOk}");

        // The spans: an application at 10 s for 20 s on the DPS, renewed at 25 s for 20 s more, cast at level 22: on from 10 s to 45 s
        // of a 60 s window (58.3 %), at level 22; an effect that isn't a party buff is left out.
        DateTime t0 = new(2026, 10, 31, 12, 0, 0);
        int skill = RaidDps.ChanterMantra / 10_000 * 100_000 + 51; // a rank code of the Mantra family
        var events = new List<BuffEvent>
        {
            new(t0.AddSeconds(10), BuffKind.On, dps, 7, skill, chanter, 20_000, 22),
            new(t0.AddSeconds(25), BuffKind.Renewed, dps, 7, skill, chanter, 20_000, 22),
            new(t0.AddSeconds(12), BuffKind.On, dps, 8, 2_000_123, dps, 30_000, 0),
        };
        var (onPlayers, _) = RaidDps.BuffsIn(events, new[] { dps }, null, t0, t0.AddSeconds(60));
        RaidBuff? read = onPlayers.TryGetValue(dps, out var list) && list.Count == 1 ? list[0] : null;
        bool spansOk = read is { } b && b.DisplayBase == RaidDps.ChanterMantra && b.Caster == chanter && b.Level == 22 && Math.Abs(b.UptimePercent - 58.33) < 0.1;
        Console.WriteLine($"  -> buff events become one span of the Mantra from the Chanter at level 22, {read?.UptimePercent:0.0} % of the fight: {spansOk}");

        return gainOk && creditOk && pairOk && spansOk;
    }
}
