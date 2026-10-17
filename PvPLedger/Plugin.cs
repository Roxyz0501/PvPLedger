using System.Collections.Concurrent;
using System.Threading.Channels;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Game.DutyState;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace PvPLedger;

public sealed partial class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commands;
    private readonly IClientState client;
    private readonly IPlayerState player;
    private readonly IDutyState duty;
    private readonly IDataManager data;
    private readonly IFramework framework;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private LocalizedFonts fonts;
    private readonly HistoryStore store;
    private readonly Webhook webhook = new();
    private readonly NativeResults native;
    private readonly CommandInfo command;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Channel<SaveJob> jobs = Channel.CreateBounded<SaveJob>(new BoundedChannelOptions(16) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly ConcurrentQueue<System.Action> completions = new();
    private readonly ConcurrentQueue<(Guid Id, FinalResult Result)> resultQueue = new();
    private readonly Task worker;
    private MatchSession? current;
    private MatchRecord? lastMatch;
    private readonly List<SaveJob> failed = [];
    private string owner = "", status = "", exportPath = "", webhookDraft = "";
    private bool open, preview, disposed;
    private volatile bool allowDiscord;
    private DateTimeOffset nextTick, endDue, saveConfigDue;
    private uint finishedTerritory;
    private int lastGauge = -1, lastKills = -1;
    private int historyRevision = -1, page, count, historyGeneration;
    private MatchSummary[] history = [];
    private MatchRecord? detail;
    private bool loading, detailLoading, exporting;
    private int eventPage;
    private sealed record SaveJob(string Owner, MatchRecord Record, bool Send, string Secret, string Language, bool Name);

    public Plugin(IDalamudPluginInterface pi, ICommandManager commands, IClientState client, IPlayerState player,
        IDutyState duty, IDataManager data, IFramework framework, IChatGui chat, IGameInteropProvider interop, IPluginLog log)
    {
        this.pi = pi; this.commands = commands; this.client = client; this.player = player; this.duty = duty;
        this.data = data; this.framework = framework; this.chat = chat; this.log = log;
        config = pi.GetPluginConfig() as Configuration ?? new(); config.Sanitize();
        allowDiscord = config.DiscordEnabled;
        var language = Loc.Resolve(config.Language, client.ClientLanguage, pi.UiLanguage);
        if (config.Language != language) { config.Language = language; pi.SavePluginConfig(config); }
        fonts = new(pi.UiBuilder.FontAtlas, []);
        store = new(Path.Combine(pi.GetPluginConfigDirectory(), "matches-v1"));
        native = new(interop, () => current, (id, result) => { if (resultQueue.Count < 16) resultQueue.Enqueue((id, result)); }, log);
        command = new((_, _) => Open()) { HelpMessage = T("CommandHelp") };
        if (!commands.AddHandler("/pvpledger", command)) { native.Dispose(); fonts.Dispose(); webhook.Dispose(); throw new InvalidOperationException("Command already registered"); }
        worker = Task.Run(Worker);
        pi.UiBuilder.Draw += Draw; pi.UiBuilder.OpenMainUi += Open; pi.UiBuilder.OpenConfigUi += Open;
        framework.Update += Update; chat.LogMessage += OnLog;
        duty.DutyStarted += OnStart; duty.DutyRecommenced += OnRecommence; duty.DutyCompleted += OnEnd;
        client.TerritoryChanged += OnTerritory; client.Login += OnLogin; client.Logout += OnLogout;
        OnLogin();
    }
    private string T(string key) => Loc.Get(config.Language, key);
    private void Open() { open = true; if (historyRevision != store.Revision) LoadHistory(); }
    private void OnLogin()
    {
        if (!player.IsLoaded || player.ContentId == 0) return;
        var nextOwner = HistoryStore.OwnerKey(player.ContentId);
        if (owner != nextOwner)
        {
            owner = nextOwner; historyGeneration++; loading = detailLoading = false;
            history = []; detail = null; lastMatch = null; count = page = 0; historyRevision = -1;
        }
        if (duty.IsDutyStarted && current == null && finishedTerritory != client.TerritoryType) Start(true);
        if (open) LoadHistory();
    }
    private void OnLogout(int _, int __)
    {
        DrainResults();
        Finish(true); owner = ""; history = []; detail = null; lastMatch = null; historyGeneration++;
        loading = detailLoading = false; historyRevision = -1; count = page = 0; finishedTerritory = 0;
    }
    private void OnTerritory(uint territory)
    {
        DrainResults();
        if (current != null && current.Territory != territory) Finish(current.Ended == null);
        if (finishedTerritory != territory) finishedTerritory = 0;
    }
    private void OnStart(IDutyStateEventArgs _) => Start(false);
    private void OnRecommence(IDutyStateEventArgs _) { DrainResults(); Finish(true); finishedTerritory = 0; Start(false); }
    private void OnEnd(IDutyStateEventArgs _)
    {
        if (current == null || current.Ended != null) return;
        current.End(DateTimeOffset.UtcNow); endDue = DateTimeOffset.UtcNow.AddSeconds(8);
    }
    private void Start(bool partial)
    {
        if (current != null || !client.IsPvPExcludingDen || !player.IsLoaded || player.ContentId == 0) return;
        try
        {
            var cfc = duty.ContentFinderCondition.Value;
            var type = data.GetExcelSheet<InstanceContent>().GetRow(cfc.Content.RowId).InstanceContentType.RowId;
            Mode? mode = type switch { 5 => Mode.CrystallineConflict, 6 => Mode.Frontline, 12 => Mode.RivalWings, _ => null };
            if (mode == null) return;
            owner = HistoryStore.OwnerKey(player.ContentId);
            current = new(DateTimeOffset.UtcNow, mode.Value, cfc.Name.ExtractText(), player.CharacterName, owner, player.ContentId, client.TerritoryType, partial);
            lastMatch = null; lastGauge = lastKills = -1; finishedTerritory = 0;
            RefreshFonts([current.Arena]);
        }
        catch { status = "StartFailed"; }
    }
    private void OnResult(Guid id, FinalResult result)
    {
        if (current == null || current.Id != id) return;
        if (current.Apply(result, DateTimeOffset.UtcNow)) Finish(false);
    }
    private void DrainResults()
    {
        while (resultQueue.TryDequeue(out var item)) OnResult(item.Id, item.Result);
    }
    private void Finish(bool interrupted)
    {
        if (current == null) return;
        var record = current.Snapshot(DateTimeOffset.UtcNow, interrupted);
        var job = new SaveJob(current.Owner, record, config.DiscordEnabled && record.Summary.Official,
            config.ProtectedWebhook, config.Language!, config.DiscordIncludeName);
        finishedTerritory = current.Territory; current = null; lastMatch = record;
        if (!jobs.Writer.TryWrite(job)) { failed.Add(job); status = "SaveFailed"; }
    }
    private async Task Worker()
    {
        await foreach (var job in jobs.Reader.ReadAllAsync())
        {
            try
            {
                store.Save(job.Owner, job.Record);
                completions.Enqueue(() => { status = "Saved"; if (open && owner == job.Owner) LoadHistory(); });
                if (!job.Send || !allowDiscord || cancellation.IsCancellationRequested) continue;
                string url;
                try { url = SecretStore.Unprotect(job.Secret); }
                catch { completions.Enqueue(() => status = "SecretFailed"); continue; }
                if (!store.ClaimDelivery(job.Owner, job.Record.Summary)) continue;
                var result = await webhook.Send(url, job.Record.Summary, job.Language, job.Name, cancellation.Token).ConfigureAwait(false);
                store.DeliveryResult(job.Owner, job.Record.Summary, result);
                completions.Enqueue(() => status = result);
            }
            catch
            {
                completions.Enqueue(() => { failed.Add(job with { Send = false }); status = "SaveFailed"; });
                log.Warning("PvP Ledger background save failed; snapshot retained for manual save retry.");
            }
        }
    }
    private void Update(IFramework _)
    {
        DrainResults();
        while (completions.TryDequeue(out var complete)) if (!disposed) complete();
        var now = DateTimeOffset.UtcNow;
        if (saveConfigDue != default && now >= saveConfigDue) SaveConfig();
        if (now < nextTick) return; nextTick = now.AddMilliseconds(250);
        if (current is { Ended: not null } && now >= endDue) { Finish(false); return; }
        if (current == null)
        {
            if (lastMatch != null && now - lastMatch.Summary.Ended > TimeSpan.FromSeconds(15)) lastMatch = null;
            return;
        }
        if (current.Mode == Mode.Frontline && config.EstimateAssists && current.Ended == null)
        {
            var gauge = native.ReadBattleHigh();
            if (gauge != null && (gauge != lastGauge || current.Kills != lastKills))
            { current.Estimate.Observe(gauge.Value, current.Kills, current.Partial); lastGauge = gauge.Value; lastKills = current.Kills; }
        }
    }
    private bool IsLocal(ILogMessageEntity? entity) => entity is { IsPlayer: true } && entity.Name.ExtractText() == player.CharacterName && entity.HomeWorldId == player.HomeWorld.RowId;
    private void OnLog(ILogMessage message)
    {
        if (message.LogMessageId is not (557 or 558 or 559) || !client.IsPvPExcludingDen) return;
        if (current == null && duty.IsDutyStarted && finishedTerritory != client.TerritoryType) Start(true);
        if (current == null || current.Ended != null) return;
        var source = message.SourceEntity; var target = message.TargetEntity;
        // 557: source defeats target. 558/559: source is defeated (by target).
        var killer = message.LogMessageId == 557 ? source : target;
        var victim = message.LogMessageId == 557 ? target : source;
        if (victim is { IsPlayer: true } && IsLocal(killer)) current.Add(DateTimeOffset.UtcNow, EventKind.Kill, victim.Name.ExtractText());
        else if (IsLocal(victim)) current.Add(DateTimeOffset.UtcNow, EventKind.Death, killer?.Name.ExtractText() ?? "");
    }
    private void ChangeConfig() => saveConfigDue = DateTimeOffset.UtcNow.AddMilliseconds(500);
    private void SaveConfig()
    {
        try { pi.SavePluginConfig(config); saveConfigDue = default; }
        catch { status = "ConfigFailed"; saveConfigDue = default; }
    }
    private void LoadHistory()
    {
        if (owner.Length == 0 || loading) return;
        loading = true; var generation = historyGeneration; var requestedOwner = owner; var requestedPage = page;
        _ = Task.Run(() =>
        {
            try
            {
                var result = store.Page(requestedOwner, requestedPage);
                completions.Enqueue(() =>
                {
                    if (generation != historyGeneration || requestedOwner != owner) return;
                    loading = false; history = result.Rows; count = result.Count; historyRevision = result.Revision;
                    RefreshFonts(history.Select(s => s.Arena));
                    if (historyRevision != store.Revision) LoadHistory();
                });
            }
            catch { completions.Enqueue(() => { if (generation == historyGeneration) { loading = false; status = "ReadFailed"; } }); }
        });
    }
    private void LoadDetail(MatchSummary summary)
    {
        if (detailLoading || owner.Length == 0) return;
        detailLoading = true; var generation = historyGeneration; var requestedOwner = owner;
        _ = Task.Run(() =>
        {
            try { var result = store.Detail(requestedOwner, summary); completions.Enqueue(() => { if (generation == historyGeneration) { detail = result; eventPage = 0; detailLoading = false; } }); }
            catch { completions.Enqueue(() => { if (generation == historyGeneration) { status = "ReadFailed"; detailLoading = false; } }); }
        });
    }
    private void Export(bool events)
    {
        if (exporting || owner.Length == 0 || (events && detail == null)) return;
        exporting = true; var requestedOwner = owner; var rows = history; var selected = events ? detail : null;
        _ = Task.Run(() =>
        {
            try { var path = store.Export(requestedOwner, rows, selected); completions.Enqueue(() => { exporting = false; exportPath = path; status = "Exported"; }); }
            catch { completions.Enqueue(() => { exporting = false; status = "ExportFailed"; }); }
        });
    }
    private void RefreshFonts(IEnumerable<string> extra)
    {
        var replacement = new LocalizedFonts(pi.UiBuilder.FontAtlas, extra);
        fonts.Dispose(); fonts = replacement;
    }
    public void Dispose()
    {
        pi.UiBuilder.Draw -= Draw; pi.UiBuilder.OpenMainUi -= Open; pi.UiBuilder.OpenConfigUi -= Open;
        framework.Update -= Update; chat.LogMessage -= OnLog; duty.DutyStarted -= OnStart;
        duty.DutyRecommenced -= OnRecommence; duty.DutyCompleted -= OnEnd;
        client.TerritoryChanged -= OnTerritory; client.Login -= OnLogin; client.Logout -= OnLogout;
        native.Dispose();
        DrainResults();
        while (completions.TryDequeue(out var complete)) complete();
        Finish(true);
        if (saveConfigDue != default) SaveConfig();
        disposed = true; commands.RemoveHandler("/pvpledger"); cancellation.Cancel(); jobs.Writer.TryComplete();
        // Disk-only queue drains on unload. Cancelling HTTP prevents the ten-second network timeout.
        worker.GetAwaiter().GetResult();
        webhook.Dispose(); cancellation.Dispose(); fonts.Dispose();
    }
}
