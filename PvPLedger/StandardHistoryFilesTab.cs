using System;
using System.Collections.Generic;
using System.Globalization;
using Dalamud.Bindings.ImGui;

namespace Roxyz0501.Dalamud.CommonUi;

/// <summary>Canonical presentation only. Supply cached metadata; never enumerate files in Draw.</summary>
internal static class StandardHistoryFilesTab
{
    internal sealed record Labels(string Tab, string FileCount, string Size, string Directory,
        string OpenDirectory, string Refresh, string Scope, string LogicalBytesNote);
    internal sealed record Location(string Id, string Directory, long? FileCount, long? Bytes);
    internal sealed record Snapshot(long? FileCount, long? Bytes, string Scope,
        string Status, IReadOnlyList<Location> Locations);

    // Callbacks must handle failures and publish a localized Status on the next snapshot.
    // requestRefresh schedules a single background refresh; openDirectory is click-only.
    public static void Draw(Labels labels, Snapshot snapshot,
        Action requestRefresh, Action<string> openDirectory)
    {
        if (!ImGui.BeginTabItem($"{labels.Tab}###HistoryFiles")) return;
        try
        {
            ImGui.TextUnformatted($"{labels.FileCount}: {Number(snapshot.FileCount)}");
            ImGui.TextUnformatted($"{labels.Size}: {Number(snapshot.Bytes)} B");
            ImGui.TextUnformatted($"{labels.Scope}: {snapshot.Scope}");
            ImGui.TextUnformatted(labels.LogicalBytesNote);
            if (!string.IsNullOrEmpty(snapshot.Status)) ImGui.TextUnformatted(snapshot.Status);
            if (ImGui.Button($"{labels.Refresh}###RefreshHistoryFiles")) requestRefresh();
            foreach (var location in snapshot.Locations)
            {
                ImGui.PushID(location.Id);
                try
                {
                    ImGui.Separator();
                    ImGui.TextUnformatted($"{labels.Directory}: {location.Directory}");
                    ImGui.TextUnformatted($"{labels.FileCount}: {Number(location.FileCount)}");
                    ImGui.TextUnformatted($"{labels.Size}: {Number(location.Bytes)} B");
                    if (ImGui.Button($"{labels.OpenDirectory}###OpenHistoryDirectory"))
                        openDirectory(location.Directory);
                }
                finally { ImGui.PopID(); }
            }
        }
        finally { ImGui.EndTabItem(); }
    }

    private static string Number(long? value) => value is >= 0
        ? value.Value.ToString("N0", CultureInfo.CurrentCulture) : "—";
}
