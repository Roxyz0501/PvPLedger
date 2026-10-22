using System.Text.Json;

namespace PvPLedger;

// One writer; backups are immutable previous successful primary files.
public sealed class ConfigurationStore(string path)
{
    private readonly object gate = new();
    private byte[] baseline = [];
    private bool dirty;
    private long revision;
    private int failures;
    private DateTimeOffset next = DateTimeOffset.UtcNow.AddSeconds(60);
    public int Writes { get; private set; }
    public long WrittenBytes { get; private set; }
    public bool Recovered { get; private set; }
    public bool Stopped => failures >= 3;
    public Action? BeforeCommit { get; set; }
    private static Configuration Decode(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("Version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1)
            throw new InvalidDataException("Unsupported configuration");
        var value = JsonSerializer.Deserialize<Configuration>(bytes) ?? throw new InvalidDataException();
        if (value.ProtectedWebhook == null) throw new InvalidDataException();
        value.Sanitize(); return value;
    }
    private static byte[] Read(string file)
    {
        if (new FileInfo(file).Length > 1_048_576) throw new InvalidDataException();
        var bytes = File.ReadAllBytes(file); Decode(bytes); return bytes;
    }
    private IEnumerable<string> Backups() => Directory.Exists(Path.GetDirectoryName(path))
        ? Directory.EnumerateFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".backup-*").OrderDescending(StringComparer.Ordinal)
        : [];
    public Configuration Load(Func<Configuration> legacy)
    {
        lock (gate)
        {
            if (File.Exists(path))
            {
                try { var value = Decode(Read(path)); baseline = JsonSerializer.SerializeToUtf8Bytes(value); return value; }
                catch (Exception ex) when (ex is JsonException or InvalidDataException) { }
            }
            foreach (var backup in Backups())
            {
                try
                {
                    var bytes = Read(backup); var value = Decode(bytes);
                    if (File.Exists(path)) File.Move(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
                    Commit(bytes, false); baseline = JsonSerializer.SerializeToUtf8Bytes(value); Recovered = true; return value;
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { }
            }
            Configuration initial;
            try { initial = legacy(); } catch { initial = new(); }
            initial.Sanitize(); dirty = true;
            return initial;
        }
    }
    public void Changed() { lock (gate) { dirty = true; revision++; } }
    public void Retry() { lock (gate) failures = 0; }
    public bool Tick(Configuration value, DateTimeOffset now)
    {
        lock (gate)
        {
            if (now < next) return true;
            next = now.AddSeconds(60);
            return Flush(value);
        }
    }
    public bool Flush(Configuration value)
    {
        lock (gate)
        {
            if (!dirty) return true;
            if (Stopped) return false;
            var captured = revision;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
            if (baseline.AsSpan().SequenceEqual(bytes)) { dirty = false; return true; }
            try
            {
                Commit(bytes, true); baseline = bytes; dirty = revision != captured; failures = 0;
                return true;
            }
            catch { failures++; return false; }
        }
    }
    private static bool IsValidBackup(string path)
    {
        try { Read(path); return true; }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { return false; }
    }
    private void Commit(byte[] bytes, bool backup)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        { file.Write(bytes); file.Flush(true); WrittenBytes += bytes.Length; }
        Read(temp); BeforeCommit?.Invoke();
        if (File.Exists(path) && backup)
        {
            try { Read(path); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                File.Move(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
            }
        }
        if (File.Exists(path) && backup)
            File.Replace(temp, path, path + ".backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N"));
        else File.Move(temp, path, true);
        Writes++;
        // Cleanup is not part of commit success: a locked old backup must not trigger a repeat save.
        try { foreach (var old in Backups().Where(IsValidBackup).Skip(3)) File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
