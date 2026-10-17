using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
using Roxyz0501.Dalamud.CommonUi;

namespace PvPLedger;

public sealed partial class Plugin
{
    private void Draw()
    {
        if (!open && current == null && lastMatch == null) return;
        using var font = fonts.Push(config.Language);
        if (owner.Length > 0 && Overlay.Draw(config, current, lastMatch, preview && open, DateTimeOffset.UtcNow)) ChangeConfig();
        if (!open) return;
        ImGui.SetNextWindowSize(new Vector2(850, 580), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(640, 380), new Vector2(float.MaxValue));
        if (ImGui.Begin("PvP Ledger###PvPLedger", ref open))
        {
            ImGui.TextColored(new Vector4(.4f, .87f, .79f, 1), T("JournalTitle"));
            if (status.Length > 0) ImGui.TextWrapped(T(status));
            if (ImGui.BeginTabBar("MainTabs"))
            {
                if (ImGui.BeginTabItem(T("History") + "###History")) { DrawHistory(); ImGui.EndTabItem(); }
                StandardPluginTabs.DrawStandardTrailingTabs(T("Settings"), "Settings", DrawSettings,
                    T("Language"), DrawLanguage, T("Support"), () => StandardPluginTabs.DrawSupportContent(T("SupportHeading"), T("SupportBody"), T("Recipient"), T("SupportButton")));
                ImGui.EndTabBar();
            }
        }
        ImGui.End();
    }
    private void DrawHistory()
    {
        if (owner.Length == 0) { ImGui.TextWrapped(T("LoginNeeded")); return; }
        ImGui.BeginDisabled(loading || detailLoading);
        if (ImGui.Button(T("Previous") + "###Previous") && page > 0) { page--; LoadHistory(); }
        ImGui.SameLine(); ImGui.TextUnformatted($"{page + 1} / {Math.Max(1, (count + 24) / 25)}"); ImGui.SameLine();
        if (ImGui.Button(T("Next") + "###Next") && (page + 1) * 25 < count) { page++; LoadHistory(); }
        ImGui.SameLine();
        if (ImGui.Button(T("Refresh") + "###Refresh")) LoadHistory();
        ImGui.EndDisabled();
        ImGui.SameLine(); ImGui.BeginDisabled(exporting || history.Length == 0);
        if (ImGui.Button(T("ExportPage") + "###ExportPage")) Export(false);
        ImGui.EndDisabled();
        if (loading) ImGui.TextWrapped(T("Loading"));
        if (history.Length == 0 && !loading) ImGui.TextWrapped(T("Empty"));
        if (ImGui.BeginChild("HistoryRows", new Vector2(0, detail == null ? -50 : 190)))
        {
            if (ImGui.BeginTable("Matches", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn(T("Date")); ImGui.TableSetupColumn(T("Mode")); ImGui.TableSetupColumn("K / D / A"); ImGui.TableSetupColumn(T("Result")); ImGui.TableHeadersRow();
                foreach (var item in history)
                {
                    ImGui.TableNextRow(); ImGui.TableNextColumn();
                    ImGui.BeginDisabled(detailLoading);
                    if (ImGui.Selectable($"{item.Started.ToLocalTime():MM/dd HH:mm}###{item.Id}", detail?.Summary.Id == item.Id)) LoadDetail(item);
                    ImGui.EndDisabled(); ImGui.TableNextColumn(); ImGui.TextUnformatted(T(item.Mode.ToString()));
                    ImGui.TableNextColumn(); ImGui.TextUnformatted($"{item.Kills} / {item.Deaths} / {item.Assists?.ToString() ?? "—"}");
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(T(item.Outcome.ToString()) + (item.Official ? "" : " *"));
                }
                ImGui.EndTable();
            }
        }
        ImGui.EndChild();
        if (detailLoading) ImGui.TextWrapped(T("Loading"));
        if (detail != null) DrawDetail();
        ImGui.TextWrapped(T("Retention"));
        if (exportPath.Length > 0 && ImGui.Button(T("OpenExports") + "###OpenExports")) Util.OpenLink(Path.GetDirectoryName(exportPath)!);
    }
    private void DrawDetail()
    {
        var record = detail!; var s = record.Summary;
        ImGui.Separator(); ImGui.TextUnformatted($"{s.Started.ToLocalTime():yyyy/MM/dd HH:mm} · {s.Arena}");
        ImGui.TextWrapped(T(s.Official ? "Official" : "Observed") + (s.Partial ? " · " + T("Partial") : ""));
        if (s.LogTruncated) ImGui.TextWrapped(T("Truncated"));
        ImGui.BeginDisabled(exporting);
        if (ImGui.Button(T("ExportEvents") + "###ExportEvents")) Export(true);
        ImGui.EndDisabled(); ImGui.SameLine();
        if (ImGui.Button(T("CloseDetail") + "###CloseDetail")) { detail = null; return; }
        ImGui.SameLine(); if (ImGui.SmallButton("<###EventPrevious") && eventPage > 0) eventPage--;
        ImGui.SameLine(); ImGui.TextUnformatted($"{eventPage + 1} / {Math.Max(1, (record.Events.Length + 49) / 50)}");
        ImGui.SameLine(); if (ImGui.SmallButton(">###EventNext") && (eventPage + 1) * 50 < record.Events.Length) eventPage++;
        if (ImGui.BeginChild("MatchEvents", new Vector2(0, -65)))
        {
            foreach (var ev in record.Events.Skip(eventPage * 50).Take(50))
                ImGui.TextUnformatted($"{ev.At.ToLocalTime():HH:mm:ss}  {T(ev.Kind.ToString())}  ·  {(ev.Other.Length > 0 ? ev.Other : T("Unknown"))}");
            if (record.Events.Length == 0) ImGui.TextWrapped(T("NoEvents"));
        }
        ImGui.EndChild();
    }
    private void DrawSettings()
    {
        if (ImGui.BeginChild("SettingsContent", Vector2.Zero))
        {
            var enabled = config.OverlayEnabled; if (ImGui.Checkbox(T("OverlayEnabled") + "###Enabled", ref enabled)) { config.OverlayEnabled = enabled; ChangeConfig(); }
            ImGui.Checkbox(T("Preview") + "###Preview", ref preview);
            var locked = config.Locked; if (ImGui.Checkbox(T("Locked") + "###Locked", ref locked)) { config.Locked = locked; ChangeConfig(); }
            var scale = config.Scale; if (ImGui.SliderFloat(T("Scale") + "###Scale", ref scale, .7f, 1.6f, "%.2f")) { config.Scale = scale; ChangeConfig(); }
            var opacity = config.Opacity; if (ImGui.SliderFloat(T("Opacity") + "###Opacity", ref opacity, .2f, 1f, "%.2f")) { config.Opacity = opacity; ChangeConfig(); }
            var rows = config.FeedRows; if (ImGui.SliderInt(T("FeedRows") + "###FeedRows", ref rows, 0, 5)) { config.FeedRows = rows; ChangeConfig(); }
            var seconds = config.FeedSeconds; if (ImGui.SliderInt(T("FeedSeconds") + "###FeedSeconds", ref seconds, 3, 30)) { config.FeedSeconds = seconds; ChangeConfig(); }
            if (ImGui.Button(T("ResetPosition") + "###ResetPosition")) { config.X = 40; config.Y = 200; ChangeConfig(); }
            var estimate = config.EstimateAssists; if (ImGui.Checkbox(T("EstimateAssists") + "###EstimateAssists", ref estimate)) { config.EstimateAssists = estimate; lastGauge = -1; ChangeConfig(); }
            ImGui.TextWrapped(T("EstimateHelp"));
            ImGui.Separator();
            var discord = config.DiscordEnabled;
            if (ImGui.Checkbox(T("DiscordEnabled") + "###DiscordEnabled", ref discord)) { config.DiscordEnabled = discord; allowDiscord = discord; ChangeConfig(); }
            ImGui.TextWrapped(T("DiscordHelp"));
            var name = config.DiscordIncludeName; if (ImGui.Checkbox(T("IncludeName") + "###IncludeName", ref name)) { config.DiscordIncludeName = name; ChangeConfig(); }
            ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("###Webhook", T("WebhookUrl"), ref webhookDraft, 256, ImGuiInputTextFlags.Password);
            if (ImGui.Button(T("SaveWebhook") + "###SaveWebhook"))
            {
                if (!Webhook.TryUrl(webhookDraft, out _)) status = "WebhookInvalid";
                else
                {
                    try { config.ProtectedWebhook = SecretStore.Protect(webhookDraft.Trim()); webhookDraft = ""; SaveConfig(); status = "SecretSaved"; }
                    catch { status = "SecretFailed"; }
                }
            }
            ImGui.SameLine(); if (ImGui.Button(T("ClearWebhook") + "###ClearWebhook")) { config.ProtectedWebhook = ""; webhookDraft = ""; config.DiscordEnabled = false; allowDiscord = false; SaveConfig(); }
            ImGui.TextWrapped(T(config.ProtectedWebhook.Length > 0 ? "WebhookConfigured" : "WebhookNotConfigured"));
            ImGui.Separator(); ImGui.TextWrapped(T(native.Status));
            if (failed.Count > 0 && ImGui.Button(T("RetrySave") + "###RetrySave"))
            {
                for (int i = failed.Count - 1; i >= 0; i--) if (jobs.Writer.TryWrite(failed[i])) failed.RemoveAt(i);
            }
        }
        ImGui.EndChild();
    }
    private void DrawLanguage()
    {
        var selected = StandardPluginTabs.DrawLanguageSelector(T("Language"), T("LanguageHelp"), config.Language!);
        if (selected != null && selected != config.Language) { config.Language = selected; command.HelpMessage = T("CommandHelp"); SaveConfig(); }
    }
}
