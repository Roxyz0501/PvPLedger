using System.Numerics;
using Dalamud.Bindings.ImGui;
namespace PvPLedger;

public static class Overlay
{
    private static readonly Vector4 Mint = new(.4f, .87f, .79f, 1);
    private static readonly Vector4 Rose = new(.93f, .57f, .64f, 1);
    private static readonly Vector4 Muted = new(.59f, .66f, .74f, 1);
    public static bool Draw(Configuration c, MatchSession? session, MatchRecord? result, bool preview, DateTimeOffset now)
    {
        if (!c.OverlayEnabled || (!preview && session == null && (result == null || now - result.Summary.Ended > TimeSpan.FromSeconds(15)))) return false;
        var scale = c.Scale;
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoScrollWithMouse;
        if (c.Locked && !preview) flags |= ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoMove;
        var viewport = ImGui.GetMainViewport();
        var width = 328 * scale;
        var x = Math.Clamp(c.X, 0, Math.Max(0, viewport.Size.X - width));
        var y = Math.Clamp(c.Y, 0, Math.Max(0, viewport.Size.Y - 200 * scale));
        ImGui.SetNextWindowPos(viewport.Pos + new Vector2(x, y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new(width, 0)); ImGui.SetNextWindowBgAlpha(c.Opacity);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(.035f, .052f, .076f, 1));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(.2f, .28f, .34f, .65f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16, 12) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 5) * scale);
        var changed = false;
        if (ImGui.Begin("PvP Ledger###PvPLedgerOverlay", flags | ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.SetWindowFontScale(scale);
            var draw = ImGui.GetWindowDrawList(); var pos = ImGui.GetWindowPos();
            draw.AddLine(pos + new Vector2(16, 1) * scale, pos + new Vector2(86, 1) * scale, ImGui.ColorConvertFloat4ToU32(Mint), 2 * scale);
            ImGui.TextColored(Muted, "PvP Ledger"); ImGui.SameLine();
            ImGui.TextColored(Mint, Loc.Get(c.Language, preview ? "Preview" : session != null ? "Live" : result?.Summary.Official == true ? "Official" : "Interrupted"));
            var kills = preview ? 7 : session?.Kills ?? result?.Summary.Kills ?? 0;
            var deaths = preview ? 2 : session?.Deaths ?? result?.Summary.Deaths ?? 0;
            var assists = preview ? "~12–16" : result?.Summary.Assists?.ToString() ?? "—";
            if (session != null && c.EstimateAssists && session.Mode == Mode.Frontline && session.Estimate.Minimum is { } low)
                assists = session.Estimate.Unbounded ? "—" : $"~{low}–{session.Estimate.Maximum}";
            if (ImGui.BeginTable("KDA", 3, ImGuiTableFlags.SizingStretchSame))
            {
                ImGui.TableNextColumn(); ImGui.TextColored(Mint, "K"); ImGui.SameLine(); ImGui.TextUnformatted(kills.ToString());
                ImGui.TableNextColumn(); ImGui.TextColored(Rose, "D"); ImGui.SameLine(); ImGui.TextUnformatted(deaths.ToString());
                ImGui.TableNextColumn(); ImGui.TextColored(Muted, "A"); ImGui.SameLine(); ImGui.TextUnformatted(assists);
                ImGui.EndTable();
            }
            if (assists.StartsWith('~')) ImGui.TextColored(Muted, Loc.Get(c.Language, "EstimateShort"));
            else if (session != null && session.Mode == Mode.Frontline && session.Estimate.Gauge == 100) ImGui.TextColored(Muted, Loc.Get(c.Language, "GaugeCapped"));
            if (c.FeedRows > 0)
            {
                var events = session?.Events;
                int shown = 0;
                if (preview)
                {
                    ImGui.Separator(); ImGui.TextColored(Mint, "K  04:32  ·  Example Player");
                    if (c.FeedRows > 1) ImGui.TextColored(Rose, "D  03:57  ·  Another Player");
                }
                else if (events != null)
                {
                    for (var i = events.Count - 1; i >= 0 && shown < c.FeedRows; i--)
                    {
                        var item = events[i]; if ((now - item.At).TotalSeconds > c.FeedSeconds) break;
                        if (shown++ == 0) ImGui.Separator();
                        var time = item.At - session!.Started;
                        var name = item.Other.Length == 0 ? Loc.Get(c.Language, "Unknown") : item.Other;
                        if (name.Length > 22) name = name[..21] + "…";
                        ImGui.TextColored(item.Kind == EventKind.Kill ? Mint : Rose, $"{(item.Kind == EventKind.Kill ? "K" : "D")}  {time:mm\\:ss}  ·  {name}");
                    }
                }
            }
            if (preview || !c.Locked)
            {
                ImGui.TextColored(Muted, Loc.Get(c.Language, "DragHelp"));
                if (ImGui.IsWindowHovered() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    var delta = ImGui.GetIO().MouseDelta; c.X = x + delta.X; c.Y = y + delta.Y; changed = delta != Vector2.Zero;
                }
            }
            ImGui.SetWindowFontScale(1);
        }
        ImGui.End(); ImGui.PopStyleVar(3); ImGui.PopStyleColor(2); return changed;
    }
}
