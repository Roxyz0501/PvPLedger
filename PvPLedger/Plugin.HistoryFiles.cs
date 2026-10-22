using Dalamud.Utility;
using Roxyz0501.Dalamud.CommonUi;

namespace PvPLedger;

public sealed partial class Plugin
{
    private HistoryMeasurement? fileMeasurement;
    private bool filesLoading;
    private string filesStatus = "";
    private int filesGeneration;
    private string HistoryRoot => Path.Combine(pi.GetPluginConfigDirectory(), "matches-v1");
    private void RefreshHistoryFiles()
    {
        if (disposed || filesLoading) return;
        filesLoading = true; filesStatus = "Loading";
        var generation = filesGeneration; var root = HistoryRoot;
        RunBackground(() =>
        {
            try
            {
                var result = HistoryFiles.Measure(root);
                completions.Enqueue(() =>
                {
                    filesLoading = false; fileMeasurement = result; filesStatus = "";
                    if (generation != filesGeneration && open) RefreshHistoryFiles();
                });
            }
            catch { completions.Enqueue(() => { filesLoading = false; fileMeasurement = null; filesStatus = "ReadFailed"; }); }
        });
    }
    private void InvalidateHistoryFiles()
    {
        filesGeneration++; fileMeasurement = null;
        if (open) RefreshHistoryFiles();
    }
    private void DrawHistoryFiles()
    {
        var snapshot = new StandardHistoryFilesTab.Snapshot(fileMeasurement?.Files, fileMeasurement?.Bytes,
            T("FilesScope"), filesStatus.Length == 0 ? "" : T(filesStatus),
            fileMeasurement?.Locations.Select(x => new StandardHistoryFilesTab.Location(x.Directory, x.Directory, x.Files, x.Bytes)).ToArray()
            ?? [new("history", HistoryRoot, null, null)]);
        StandardHistoryFilesTab.Draw(new(T("HistoryFiles"), T("FileCount"), T("FileSize"), T("StorageDirectory"),
            T("OpenDirectory"), T("Refresh"), T("Scope"), T("LogicalBytes")), snapshot, RefreshHistoryFiles, OpenHistoryDirectory);
    }
    private void OpenHistoryDirectory(string path)
    {
        try
        {
            if (fileMeasurement == null || !fileMeasurement.Locations.Any(x => x.Directory == path) || !Directory.Exists(path))
                throw new IOException();
            Util.OpenLink(path);
        }
        catch { filesStatus = "OpenDirectoryFailed"; }
    }
}
