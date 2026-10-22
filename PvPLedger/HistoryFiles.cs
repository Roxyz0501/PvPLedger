namespace PvPLedger;

public sealed record HistoryLocation(string Directory, long Files, long Bytes);
public sealed record HistoryMeasurement(HistoryLocation[] Locations)
{
    public long Files => Locations.Sum(x => x.Files);
    public long Bytes => Locations.Sum(x => x.Bytes);
}
public static class HistoryFiles
{
    // Metadata only, all characters and all dates. Exports are shown separately without double counting.
    public static HistoryMeasurement Measure(string root)
    {
        var locations = new List<HistoryLocation>();
        long count = 0, bytes = 0;
        if (Directory.Exists(root))
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint };
            var exports = new Dictionary<string, (long Count, long Bytes)>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", options))
            {
                if (file.Directory?.Name == "exports")
                {
                    var key = file.Directory.FullName;
                    exports.TryGetValue(key, out var value); exports[key] = (value.Count + 1, value.Bytes + file.Length);
                }
                else { count++; bytes += file.Length; }
            }
            locations.Add(new(root, count, bytes));
            locations.AddRange(exports.OrderBy(x => x.Key).Select(x => new HistoryLocation(x.Key, x.Value.Count, x.Value.Bytes)));
        }
        else locations.Add(new(root, 0, 0));
        return new(locations.ToArray());
    }
}
