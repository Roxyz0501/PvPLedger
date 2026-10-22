using System.Text.Json;
using PvPLedger;

internal static class PersistenceChecks
{
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        var store = new ConfigurationStore(path);
        var config = store.Load(() => new() { Language = "ja" });
        Program.Check(store.Flush(config) && store.Writes == 1, "Initial config commit");
        int initial = store.Writes;
        var time = DateTimeOffset.UtcNow.AddMinutes(1);
        for (int i = 0; i < 10; i++) store.Tick(config, time.AddMinutes(i));
        Program.Check(store.Writes == initial, "Ten unchanged config periods: zero writes");
        for (int i = 0; i < 100; i++) { config.X = i; store.Changed(); }
        store.Tick(config, time.AddMinutes(11));
        Program.Check(store.Writes == initial + 1, "100 UI changes: one period commit");
        var original = config.X; config.X++; store.Changed(); config.X = original; store.Changed();
        store.Tick(config, time.AddMinutes(12));
        Program.Check(store.Writes == initial + 1, "Reverted config: zero writes");
        for (int i = 0; i < 4; i++) { config.X = 200 + i; store.Changed(); store.Flush(config); }
        Program.Check(Directory.GetFiles(root, "settings.json.backup-*").Length == 3, "Exactly three generations");
        var snapshots = Directory.GetFiles(root, "settings.json.backup-*").OrderDescending().ToArray();
        var previous = JsonSerializer.Deserialize<Configuration>(File.ReadAllBytes(snapshots[0]))!.X;
        var goodPrimary = File.ReadAllBytes(path);
        store.BeforeCommit = () => throw new IOException("injected before atomic commit");
        config.X = 900; store.Changed();
        for (int i = 0; i < 8; i++) store.Tick(config, time.AddMinutes(20 + i));
        Program.Check(store.Stopped && File.ReadAllBytes(path).SequenceEqual(goodPrimary), "Three failures stop; good primary retained");
        Program.Check(Directory.GetFiles(root, "settings.json.backup-*").OrderDescending().SequenceEqual(snapshots), "Failure never rotates backups");
        long failedBytes = store.WrittenBytes;
        store.Flush(config); Program.Check(store.WrittenBytes == failedBytes, "Stopped failure: no extra IO");
        store.Retry(); store.BeforeCommit = null; Program.Check(store.Flush(config), "Explicit retry resumes");
        snapshots = Directory.GetFiles(root, "settings.json.backup-*").OrderDescending().ToArray();
        previous = JsonSerializer.Deserialize<Configuration>(File.ReadAllBytes(snapshots[0]))!.X;
        File.WriteAllText(path, "{broken");
        var recovery = new ConfigurationStore(path); var recovered = recovery.Load(() => throw new Exception("Must recover backup"));
        Program.Check(recovery.Recovered && recovered.X == previous, "Corrupt primary recovers latest good backup");
        Program.Check(Directory.GetFiles(root, "settings.json.corrupt-*").Length == 1, "Corrupt primary preserved separately");
        var healthy = new ConfigurationStore(path); var current = healthy.Load(() => throw new Exception());
        Program.Check(!healthy.Recovered && healthy.Writes == 0 && current.X == previous, "Healthy primary wins, startup zero writes");
        current.X = 300; healthy.Changed();
        healthy.BeforeCommit = () => { current.X = 301; healthy.Changed(); };
        healthy.Flush(current); healthy.BeforeCommit = null; healthy.Flush(current);
        Program.Check(JsonSerializer.Deserialize<Configuration>(File.ReadAllBytes(path))!.X == 301 && healthy.Writes == 2, "Change during save survives for next flush");
        current.X = 302; healthy.Changed();
        Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => healthy.Flush(current)))).GetAwaiter().GetResult();
        Program.Check(healthy.Writes == 3, "Concurrent config flush: single commit");
        var backupPaths = Directory.GetFiles(root, "settings.json.backup-*").OrderDescending().ToArray();
        var fallbackX = JsonSerializer.Deserialize<Configuration>(File.ReadAllBytes(backupPaths[1]))!.X;
        File.WriteAllText(backupPaths[0], "{bad backup");
        File.WriteAllText(path, "{\"Version\":\"invalid\"}");
        var fallback = new ConfigurationStore(path); var fallbackValue = fallback.Load(() => throw new Exception());
        Program.Check(fallback.Recovered && fallbackValue.X == fallbackX, "Invalid version and corrupt newest backup fall back to next valid generation");
        Program.Check(File.ReadAllText(backupPaths[0]) == "{bad backup", "Corrupt backup retained for inspection");
        Console.WriteLine($"Config measured: initial 1; ten unchanged periods 0; 100 UI changes 1; reverted 0; 12 concurrent flushes 1. Successful commits={store.Writes + recovery.Writes + healthy.Writes}, logical temporary-file bytes including fault injection={store.WrittenBytes + recovery.WrittenBytes + healthy.WrittenBytes}.");

        var historyRoot = Path.Combine(root, "history"); var history = new HistoryStore(historyRoot); var owner = HistoryStore.OwnerKey(123);
        var midnight = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-2), TimeSpan.Zero);
        var match = new MatchSession(midnight.AddMinutes(-1), Mode.Frontline, "Synthetic", "Example", owner, 123, 376, false);
        match.Add(midnight.AddSeconds(-10), EventKind.Kill, "Example"); match.Add(midnight.AddSeconds(10), EventKind.Death, "Example");
        var record = match.Snapshot(midnight.AddMinutes(1)); history.Save(owner, record);
        var day = midnight.AddMinutes(-1).ToString("yyyy-MM-dd");
        Program.Check(Directory.GetFiles(Path.Combine(historyRoot, owner, day), "*.match.json").Length == 1, "Midnight match assigned to UTC start day");
        var second = record with { Summary = record.Summary with { Id = Guid.NewGuid(), Started = midnight.AddMinutes(5) } }; history.Save(owner, second);
        var restarted = new HistoryStore(historyRoot); restarted.Save(owner, record);
        Program.Check(restarted.Writes == 0 && restarted.Page(owner, 0).Count == 2, "Restart and delayed duplicate across dates");
        var firstFile = Directory.GetFiles(Path.Combine(historyRoot, owner, day), "*.match.json").Single();
        var summaryFile = firstFile.Replace(".match.json", ".summary.json");
        File.Move(firstFile, Path.Combine(historyRoot, owner, Path.GetFileName(firstFile)));
        File.Move(summaryFile, Path.Combine(historyRoot, owner, Path.GetFileName(summaryFile)));
        var legacy = new HistoryStore(historyRoot);
        Program.Check(legacy.Page(owner, 0).Count == 2 && legacy.Detail(owner, record.Summary).Events.Length == 2, "Mixed legacy/date files remain accessible without migration");
        File.Delete(Path.Combine(historyRoot, owner, Path.GetFileName(summaryFile)));
        var recovering = new HistoryStore(historyRoot); recovering.RecoverInterruptedSaves();
        Program.Check(recovering.Page(owner, 0).Count == 2 && recovering.Writes == 1, "Interrupted body/summary transaction recovers only missing summary");
        recovering.RecoverInterruptedSaves(); Program.Check(recovering.Writes == 1, "Recovery rerun is idempotent");
        var pending = second with { Summary = second.Summary with { Id = Guid.NewGuid() } };
        recovering.PreservePending(owner, pending); recovering.RecoverInterruptedSaves();
        Program.Check(recovering.Page(owner, 0).Count == 3, "Exceptional pending journal recovers");
        Program.Check(!Directory.GetFiles(historyRoot, "*.pending.json", SearchOption.AllDirectories).Any(), "Recovered journal cleared only after successful save");
        recovering.Export(owner, [record.Summary], null);
        var measure = HistoryFiles.Measure(historyRoot);
        var files = Directory.GetFiles(historyRoot, "*", SearchOption.AllDirectories);
        Program.Check(measure.Files == files.Length && measure.Bytes == files.Sum(x => new FileInfo(x).Length), "Cached metadata totals include all dates, legacy and exports exactly once");
        Program.Check(measure.Locations.Length == 2, "History and CSV locations shown separately");
        Console.WriteLine($"History measured: two matches={history.Writes} writes/{history.WrittenBytes} logical bytes; duplicate=0; orphan recovery=1 summary write. Inventory={measure.Files} files/{measure.Bytes} logical bytes, {measure.Locations.Length} locations.");
    }
}
