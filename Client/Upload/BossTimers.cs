using System.IO;
using System.Net.Http;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Upload;

/// <summary>One of a boss's casts as everyone's uploads show it (the site's GET /meter/api/boss-timers): the skill, how many of the fights had it, when it
/// first came (seconds after the boss went into combat), how often it came again (null: once a fight) and how long it takes.</summary>
public sealed record BossTimerCast(int Id, string Name, int Fights, double First, double? Every, double Dur);

/// <summary>A cast the callout names: the skill and the seconds until it comes (0 while it is being cast).</summary>
public sealed record BossCallout(string Name, double In, bool Casting);

/// <summary>What the meter overlay's callout line shows: the next casts (soonest first) and the time left to the boss's rage, when the game data knows it.</summary>
public sealed record BossCalloutFrame(string Boss, IReadOnlyList<BossCallout> Next, TimeSpan? Rage, int Fights);

/// <summary>
/// Boss timers learnt from uploads: every fight the meters send keeps the boss's casts by skill id (src/meter.js, mt_casts), and the site works out per boss
/// when each cast first comes, how often again, how long it takes. While a boss is fought the meter fetches that once (kept six hours), watches the casts
/// itself and counts down to the next ones: from the last time it saw a cast, else from the boss's start. A cast whose time passed unseen (a phase that
/// did not come yet) is left out. The rage comes from the game's own table only (<see cref="Aion2BossRage"/>).
/// </summary>
public static class BossTimers
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly TimeSpan Keep = TimeSpan.FromHours(6), Retry = TimeSpan.FromMinutes(5);
    /// <summary>How far ahead a cast is named, and how long one stays named after its time passed (it may come a little late).</summary>
    public const double Ahead = 60, Late = 3;

    private static readonly Dictionary<int, (DateTime At, IReadOnlyList<BossTimerCast>? Casts, int Fights)> Known = new();
    private static readonly HashSet<int> Loading = new();

    // the fight followed: the boss entity, its start, and when each of its skills was last seen begun
    private static int _entity = -1;
    private static DateTime _since;
    private static readonly Dictionary<int, DateTime> Last = new();

    /// <summary>What the site knows of this boss; null until it answered (the first call asks it).</summary>
    public static (IReadOnlyList<BossTimerCast> Casts, int Fights)? For(int npcId)
    {
        lock (Known)
        {
            if (Known.TryGetValue(npcId, out var hit) && DateTime.UtcNow - hit.At < (hit.Casts is null ? Retry : Keep))
            {
                return hit.Casts is null ? null : (hit.Casts, hit.Fights);
            }

            if (!Loading.Add(npcId))
            {
                return null;
            }
        }

        _ = FetchAsync(npcId);
        return null;
    }

    private static async Task FetchAsync(int npcId)
    {
        IReadOnlyList<BossTimerCast>? casts = null;
        int fights = 0;
        try
        {
            string body = await Http.GetStringAsync($"{Brand.Api}/api/boss-timers?npc={npcId}");
            (casts, fights) = Parse(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or InvalidOperationException or KeyNotFoundException)
        {
            casts = null;
        }
        finally
        {
            lock (Known)
            {
                Known[npcId] = (DateTime.UtcNow, casts, fights);
                Loading.Remove(npcId);
            }
        }
    }

    /// <summary>The site's answer: {ok, npc, fights, casts: [{id, name, fights, first, every, dur}]}.</summary>
    public static (IReadOnlyList<BossTimerCast> Casts, int Fights) Parse(string body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement root = doc.RootElement;
        var list = new List<BossTimerCast>();
        foreach (JsonElement c in root.GetProperty("casts").EnumerateArray())
        {
            double? every = c.TryGetProperty("every", out JsonElement e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;
            list.Add(new BossTimerCast(c.GetProperty("id").GetInt32(), c.GetProperty("name").GetString() ?? "", c.GetProperty("fights").GetInt32(),
                c.GetProperty("first").GetDouble(), every, c.TryGetProperty("dur", out JsonElement d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0));
        }

        return (list, root.TryGetProperty("fights", out JsonElement f) && f.ValueKind == JsonValueKind.Number ? f.GetInt32() : 0);
    }

    /// <summary>The callout for the boss being fought now (local time, like the casts); null when no boss is fought or nothing is known of it.</summary>
    public static BossCalloutFrame? Now(Aion2EntityDirectory dir, DateTime now, string language)
    {
        if (Ui.CombatOverlayData.BossHp(dir, now) is not { } boss || dir.BossNpcIdOf(boss.Id) is not int npc || boss.Since is not DateTime since || since > now)
        {
            return null;
        }

        if (boss.Id != _entity || since != _since)
        {
            _entity = boss.Id;
            _since = since;
            Last.Clear();
        }

        foreach (CastView view in dir.Combat.Casts(now, TimeSpan.Zero))
        {
            if (view.Notice.Actor == boss.Id && view.Notice.Start >= since && (!Last.TryGetValue(view.Notice.SkillId, out DateTime seen) || view.Notice.Start > seen))
            {
                Last[view.Notice.SkillId] = view.Notice.Start;
            }
        }

        string name = Aion2Npcs.NameOf(npc, language) ?? dir.MonsterNameFor(boss.Id) ?? "";
        TimeSpan? rage = Aion2BossRage.Known(npc) ? since.AddSeconds(Aion2BossRage.SecondsFor(npc)) - now : null;
        if (For(npc) is not { } known)
        {
            return rage is null ? null : new BossCalloutFrame(name, Array.Empty<BossCallout>(), rage, 0);
        }

        return new BossCalloutFrame(name, Upcoming(known.Casts, Last, (now - since).TotalSeconds), rage, known.Fights);
    }

    /// <summary>The casts due within <see cref="Ahead"/> seconds, soonest first: a seen one again after its interval, an unseen one at its first time.
    /// <paramref name="seen"/>: when each skill was last begun; <paramref name="t"/>: seconds since the boss's start.</summary>
    internal static IReadOnlyList<BossCallout> Upcoming(IReadOnlyList<BossTimerCast> casts, IReadOnlyDictionary<int, DateTime> seen, double t, DateTime? start = null)
    {
        var due = new List<BossCallout>();
        DateTime origin = start ?? _since;
        foreach (BossTimerCast c in casts)
        {
            string label = Aion2SkillNames.NameOf(c.Id) is { } own && own != c.Id.ToString() ? own : c.Name;
            double? at;
            if (seen.TryGetValue(c.Id, out DateTime last))
            {
                double began = (last - origin).TotalSeconds;
                if (t - began < Math.Max(c.Dur, 0.5))
                {
                    due.Add(new BossCallout(label, 0, true)); // being cast now
                    continue;
                }

                at = c.Every is double every ? began + every : null;
            }
            else
            {
                at = c.First;
            }

            if (at is double when && when - t <= Ahead && when - t >= -Late)
            {
                due.Add(new BossCallout(label, Math.Max(0, when - t), false));
            }
        }

        return due.OrderBy(d => d.In).ToList();
    }
}
