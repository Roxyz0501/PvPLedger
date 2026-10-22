using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace PvPLedger;

// All public I/O methods are called by background workers; never by Draw.
public sealed class HistoryStore(string root)
{
    public const int PageSize = 25, MaxMatches = 1000, MaxRecordBytes = 1_048_576;
    public const long MaxBytes = 128 * 1024 * 1024;
    private readonly object gate = new();
    private readonly Dictionary<(string Owner, int Page), MatchSummary[]> pages = [];
    private readonly Queue<(string Owner, int Page)> pageOrder = [];
    private string inventoryOwner = "";
    private string[] inventory = [];
    private DateTimeOffset cachedAt;
    public int Revision { get; private set; }
    public int Reads { get; private set; }
    public int Writes { get; private set; }
    public long ReadBytes { get; private set; }
    public long WrittenBytes { get; private set; }
    public static string OwnerKey(ulong contentId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("PvPLedger/v1/" + contentId)))[..24];
    private string OwnerPath(string owner)
    {
        if (owner.Length != 24 || owner.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("Invalid owner key");
        return Path.Combine(root, owner);
    }
    private static string Stem(MatchSummary s) => $"{s.Started.UtcDateTime:yyyyMMddTHHmmssfff}-{s.Id:N}";
    private string MatchStem(string owner, MatchSummary summary)
    {
        var dir = OwnerPath(owner);
        var legacy = Path.Combine(dir, Stem(summary));
        if (File.Exists(legacy + ".summary.json") || File.Exists(legacy + ".match.json") || File.Exists(legacy + ".delivery")) return legacy;
        return Path.Combine(dir, summary.Started.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), Stem(summary));
    }
    private static IEnumerable<string> Summaries(string dir) => Directory.EnumerateFiles(dir, "*.summary.json", SearchOption.AllDirectories)
        .OrderByDescending(Path.GetFileName, StringComparer.Ordinal);
    private void Atomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
        File.Move(temp, path, true); Writes++; WrittenBytes += bytes.Length;
    }
    private T Read<T>(string path, int limit)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > limit) throw new InvalidDataException("History file exceeds limit");
        Reads++; ReadBytes += file.Length;
        return JsonSerializer.Deserialize<T>(file) ?? throw new InvalidDataException("Invalid history");
    }
    // Exceptional exit journal: one bounded match per file, no whole-history snapshot.
    public void PreservePending(string owner, MatchRecord record)
    {
        lock (gate)
        {
            var stem = MatchStem(owner, record.Summary);
            if (File.Exists(stem + ".summary.json") || File.Exists(stem + ".pending.json")) return;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(record);
            if (bytes.Length > MaxRecordBytes) throw new InvalidDataException();
            Atomic(stem + ".pending.json", bytes);
        }
    }
    public void RecoverInterruptedSaves()
    {
        lock (gate)
        {
            if (!Directory.Exists(root)) return;
            foreach (var ownerDir in Directory.EnumerateDirectories(root))
            {
                var owner = Path.GetFileName(ownerDir);
                if (owner.Length != 24 || owner.Any(c => !Uri.IsHexDigit(c))) continue;
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var file in Directory.EnumerateFiles(ownerDir, "*.json", options))
                {
                    bool pending = file.EndsWith(".pending.json", StringComparison.Ordinal);
                    if (!pending && !file.EndsWith(".match.json", StringComparison.Ordinal)) continue;
                    var stem = file[..^(pending ? 13 : 11)];
                    if (!pending && File.Exists(stem + ".summary.json")) continue;
                    MatchRecord record;
                    try
                    {
                        record = Read<MatchRecord>(file, MaxRecordBytes);
                        if (record.Schema != 1 || record.Events.Length > MatchSession.MaxEvents || Path.GetFileName(stem) != Stem(record.Summary)) continue;
                    }
                    catch (Exception ex) when (ex is JsonException or InvalidDataException) { continue; }
                    if (pending) { Save(owner, record); File.Delete(file); }
                    else Atomic(stem + ".summary.json", JsonSerializer.SerializeToUtf8Bytes(record.Summary));
                }
            }
            inventoryOwner = ""; pages.Clear(); pageOrder.Clear(); Revision++;
        }
    }
    public void Save(string owner, MatchRecord record)
    {
        lock (gate)
        {
            var dir = OwnerPath(owner); var stem = MatchStem(owner, record.Summary);
            if (File.Exists(stem + ".summary.json")) return; // idempotent same snapshot retry.
            var bytes = JsonSerializer.SerializeToUtf8Bytes(record);
            if (bytes.Length > MaxRecordBytes) throw new InvalidDataException("Match exceeds history limit");
            Atomic(stem + ".match.json", bytes);
            Atomic(stem + ".summary.json", JsonSerializer.SerializeToUtf8Bytes(record.Summary));
            Prune(dir);
            inventoryOwner = ""; pages.Clear(); pageOrder.Clear(); Revision++;
        }
    }
    private void Prune(string dir)
    {
        var files = Summaries(dir).ToArray();
        long bytes = 0; var cutoff = DateTimeOffset.UtcNow.AddDays(-365).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        for (int i = 0; i < files.Length; i++)
        {
            var stem = files[i][..^13];
            var body = stem + ".match.json";
            bytes += new FileInfo(files[i]).Length + (File.Exists(body) ? new FileInfo(body).Length : 0);
            if (i < MaxMatches && bytes <= MaxBytes && string.CompareOrdinal(Path.GetFileName(files[i])[..8], cutoff) >= 0) continue;
            File.Delete(files[i]); File.Delete(body); File.Delete(stem + ".delivery");
        }
    }
    public (MatchSummary[] Rows, int Count, int Revision) Page(string owner, int page)
    {
        lock (gate)
        {
            var dir = OwnerPath(owner);
            if (inventoryOwner != owner || DateTimeOffset.UtcNow - cachedAt > TimeSpan.FromMinutes(5))
            {
                inventory = Directory.Exists(dir) ? Summaries(dir).Take(MaxMatches).ToArray() : [];
                inventoryOwner = owner; cachedAt = DateTimeOffset.UtcNow; pages.Clear(); pageOrder.Clear();
            }
            page = Math.Max(0, page); var key = (owner, page);
            if (!pages.TryGetValue(key, out var rows))
            {
                var items = new List<MatchSummary>();
                foreach (var path in inventory.Skip(page * PageSize).Take(PageSize))
                {
                    // A corrupt entry cannot prevent access to other matches.
                    try { items.Add(Read<MatchSummary>(path, 4096)); }
                    catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException) { }
                }
                rows = items.ToArray();
                if (pages.Count == 4) pages.Remove(pageOrder.Dequeue());
                pages.Add(key, rows); pageOrder.Enqueue(key);
            }
            return (rows, inventory.Length, Revision);
        }
    }
    public MatchRecord Detail(string owner, MatchSummary summary)
    {
        lock (gate)
        {
            var record = Read<MatchRecord>(MatchStem(owner, summary) + ".match.json", MaxRecordBytes);
            if (record.Schema != 1 || record.Summary.Id != summary.Id || record.Events.Length > MatchSession.MaxEvents) throw new InvalidDataException("Unsupported history");
            return record;
        }
    }
    public string Export(string owner, MatchSummary[] summaries, MatchRecord? detail)
    {
        lock (gate)
        {
            var dir = Path.Combine(OwnerPath(owner), "exports"); Directory.CreateDirectory(dir);
            // No silent overwrite and no automatic deletion of explicit exports.
            var path = Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.csv");
            var csv = detail == null ? CsvExport.SummaryHeader + string.Concat(summaries.Take(PageSize).Select(CsvExport.SummaryRow)) : CsvExport.Events(detail);
            using var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(true)); writer.Write(csv);
            return path;
        }
    }
    // Claim is durably persisted BEFORE POST. Never retry unknown outcomes automatically.
    public bool ClaimDelivery(string owner, MatchSummary summary)
    {
        lock (gate)
        {
            var path = MatchStem(owner, summary) + ".delivery";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try
            {
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                file.Write("Attempted"u8); file.Flush(true); Writes++; WrittenBytes += 9; return true;
            }
            catch (IOException) when (File.Exists(path)) { return false; }
        }
    }
    public void DeliveryResult(string owner, MatchSummary summary, string result)
    { lock (gate) Atomic(MatchStem(owner, summary) + ".delivery", Encoding.UTF8.GetBytes(result)); }
}
