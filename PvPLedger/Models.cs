using System.Globalization;

namespace PvPLedger;

public enum Mode { CrystallineConflict, Frontline, RivalWings }
public enum MatchOutcome { Unknown, Victory, Defeat, First, Second, Third, Interrupted }
public enum EventKind { Kill, Death }
public sealed record CombatEvent(DateTimeOffset At, EventKind Kind, string Other);
public sealed record FinalResult(Mode Mode, int Seconds, int Kills, int Deaths, int Assists, MatchOutcome Outcome);
public sealed record MatchSummary(Guid Id, DateTimeOffset Started, DateTimeOffset Ended, Mode Mode,
    string Arena, string Player, int Kills, int Deaths, int? Assists, MatchOutcome Outcome,
    bool Official, bool Partial, bool LogTruncated, int? AssistMin, int? AssistMax, bool AssistUnbounded);
public sealed record MatchRecord(MatchSummary Summary, CombatEvent[] Events, int Schema = 1);

// Deliberately a range, not a fabricated exact count. Values below reflect Patch 7.55.
public sealed class BattleHighEstimate
{
    private static readonly int[] KillGains = [4, 6, 8, 12, 16, 20];
    private static readonly int[] AssistGains = [2, 3, 4, 6, 8, 10];
    private int? baseline;
    private int highest;
    private bool incomplete;
    public int? Minimum { get; private set; }
    public int? Maximum { get; private set; }
    public bool Unbounded { get; private set; }
    public int? Gauge => baseline.HasValue ? highest : null;

    public void Observe(int gauge, int observedKills, bool partial)
    {
        if (gauge is < 0 or > 100 || observedKills < 0) { Invalidate(); return; }
        if (baseline == null) { baseline = partial ? gauge : 0; highest = gauge; incomplete = partial; }
        if (gauge < highest) { Invalidate(); incomplete = true; baseline = gauge; }
        highest = gauge;
        // At 100 subsequent K/A become invisible. Even a zero lower bound is only an estimate.
        Unbounded = incomplete || gauge == 100;
        var gain = gauge - baseline.Value;
        var kills = Math.Min(observedKills, 25);
        var possibleKills = new bool[101]; possibleKills[0] = true;
        for (var k = 0; k < kills; k++)
        {
            var next = new bool[101];
            for (var p = 0; p <= 100; p++) if (possibleKills[p]) foreach (var n in KillGains) if (p + n <= 100) next[p + n] = true;
            possibleKills = next;
        }
        var min = Enumerable.Repeat(999, 101).ToArray(); var max = Enumerable.Repeat(-1, 101).ToArray(); min[0] = max[0] = 0;
        for (var p = 1; p <= 100; p++) foreach (var n in AssistGains)
            if (p >= n && max[p - n] >= 0) { min[p] = Math.Min(min[p], min[p - n] + 1); max[p] = Math.Max(max[p], max[p - n] + 1); }
        int low = 999, high = -1;
        for (var p = 0; p <= gain; p++) if (possibleKills[p] && max[gain - p] >= 0)
        { low = Math.Min(low, min[gain - p]); high = Math.Max(high, max[gain - p]); }
        if (Unbounded) { Minimum = 0; Maximum = null; }
        else if (high >= 0) { Minimum = low; Maximum = high; }
        else { Minimum = Maximum = null; } // delayed/missing kill notification: wait, do not guess.
    }
    public void Invalidate() { Minimum = Maximum = null; Unbounded = true; }
}

public sealed class MatchSession
{
    public const int MaxEvents = 4096;
    public Guid Id { get; } = Guid.NewGuid();
    public DateTimeOffset Started { get; private set; }
    public DateTimeOffset? Ended { get; private set; }
    public Mode Mode { get; }
    public string Arena { get; }
    public string Player { get; }
    public string Owner { get; }
    public ulong ContentId { get; }
    public uint Territory { get; }
    public bool Partial { get; }
    public bool Truncated { get; private set; }
    public int Kills { get; private set; }
    public int Deaths { get; private set; }
    public FinalResult? Result { get; private set; }
    public BattleHighEstimate Estimate { get; } = new();
    public List<CombatEvent> Events { get; } = [];
    private readonly Queue<(EventKind Kind, string Other, DateTimeOffset At)> recent = new();

    public MatchSession(DateTimeOffset at, Mode mode, string arena, string player, string owner, ulong contentId, uint territory, bool partial)
    { Started = at; Mode = mode; Arena = Limit(arena); Player = Limit(player); Owner = owner; ContentId = contentId; Territory = territory; Partial = partial; }
    public static string Limit(string text) => text.Length <= 128 ? text : text[..128];
    public bool Add(DateTimeOffset at, EventKind kind, string other)
    {
        if (Ended != null || Result != null) return false;
        other = Limit(other);
        foreach (var old in recent)
            if (old.Kind == kind && (kind == EventKind.Death || old.Other == other) && at - old.At < TimeSpan.FromMilliseconds(800)) return false;
        if (recent.Count == 16) recent.Dequeue();
        recent.Enqueue((kind, other, at));
        if (kind == EventKind.Kill) Kills++; else Deaths++;
        if (Events.Count < MaxEvents) Events.Add(new(at, kind, other)); else Truncated = true;
        return true;
    }
    public void End(DateTimeOffset at) => Ended ??= at;
    public bool Apply(FinalResult result, DateTimeOffset at)
    {
        if (Result != null || result.Mode != Mode || result.Seconds is < 1 or > 3600 || result.Kills is < 0 or > 255 || result.Deaths is < 0 or > 255 || result.Assists is < 0 or > 255) return false;
        Result = result; Ended ??= at; Started = Ended.Value.AddSeconds(-result.Seconds); return true;
    }
    public MatchRecord Snapshot(DateTimeOffset at, bool interrupted = false)
    {
        var r = Result;
        return new(new(Id, Started, Ended ?? at, Mode, Arena, Player, r?.Kills ?? Kills, r?.Deaths ?? Deaths,
            r?.Assists, r?.Outcome ?? (interrupted ? MatchOutcome.Interrupted : MatchOutcome.Unknown), r != null, Partial,
            Truncated, Estimate.Minimum, Estimate.Maximum, Estimate.Unbounded), Events.ToArray());
    }
}

public static class CsvExport
{
    public static string Cell(string? value)
    {
        var s = value ?? "";
        if (s.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' || s.StartsWith('\t') || s.StartsWith('\r') || s.StartsWith('\n')) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
    public static string SummaryHeader => "match_id,started_utc,ended_utc,mode,arena,player,kills,deaths,assists,outcome,official,partial,log_truncated,assist_min,assist_max,assist_unbounded\r\n";
    public static string SummaryRow(MatchSummary s) => string.Join(',', new[] {
        s.Id.ToString(), s.Started.ToUniversalTime().ToString("O"), s.Ended.ToUniversalTime().ToString("O"), s.Mode.ToString(), s.Arena, s.Player,
        s.Kills.ToString(CultureInfo.InvariantCulture), s.Deaths.ToString(CultureInfo.InvariantCulture), s.Assists?.ToString(CultureInfo.InvariantCulture) ?? "",
        s.Outcome.ToString(), s.Official.ToString(), s.Partial.ToString(), s.LogTruncated.ToString(), s.AssistMin?.ToString() ?? "", s.AssistMax?.ToString() ?? "", s.AssistUnbounded.ToString()
    }.Select(Cell)) + "\r\n";
    public static string Events(MatchRecord r) => "match_id,time_utc,elapsed_seconds,kind,other\r\n" + string.Concat(r.Events.Select(e =>
        string.Join(',', new[] { r.Summary.Id.ToString(), e.At.ToUniversalTime().ToString("O"), Math.Max(0, (e.At - r.Summary.Started).TotalSeconds).ToString("0.000", CultureInfo.InvariantCulture), e.Kind.ToString(), e.Other }.Select(Cell)) + "\r\n"));
}
