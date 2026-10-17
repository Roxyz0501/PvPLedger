using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using PvPLedger;
using Dalamud.Game;

internal static class Program
{
    internal static int Checks;
    internal static void Check(bool value, string label) { Checks++; if (!value) throw new Exception(label); }
    static async Task Main(string[] args)
    {
        var root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        var now = DateTimeOffset.UtcNow; var owner = HistoryStore.OwnerKey(123);
        var session = new MatchSession(now, Mode.Frontline, "Test arena", "Example Player", owner, 123, 376, false);
        Check(session.Add(now.AddSeconds(1), EventKind.Kill, "Enemy Player"), "First kill");
        Check(!session.Add(now.AddSeconds(1.1), EventKind.Kill, "Enemy Player"), "Duplicate event ignored");
        Check(session.Add(now.AddSeconds(2), EventKind.Kill, "Second Player"), "Distinct nearby kill retained");
        Check(session.Add(now.AddSeconds(3), EventKind.Death, "Enemy Player"), "Death");
        session.End(now.AddMinutes(5)); Check(!session.Add(now.AddMinutes(6), EventKind.Kill, "Late"), "No post-end events");
        Check(session.Apply(new(Mode.Frontline, 300, 4, 2, 17, MatchOutcome.First), now.AddMinutes(5)), "Official correction");
        Check(!session.Apply(new(Mode.Frontline, 300, 4, 2, 17, MatchOutcome.First), now), "Duplicate result ignored");
        var record = session.Snapshot(now);
        Check(record.Summary.Kills == 4 && record.Summary.Deaths == 2 && record.Summary.Assists == 17 && record.Events.Length == 3, "Official score separate from observed events");
        Check(new MatchSession(now, Mode.Frontline, "", "", owner, 123, 1, false).Kills == 0, "New match resets");
        var bounded = new MatchSession(now, Mode.Frontline, "", "", owner, 123, 1, false);
        for (int i = 0; i < 4200; i++) bounded.Add(now.AddSeconds(i), EventKind.Kill, "Other");
        Check(bounded.Events.Count == 4096 && bounded.Truncated && bounded.Kills == 4200, "Bounded log retains counters");

        var estimate = new BattleHighEstimate();
        estimate.Observe(4, 0, false); Check(estimate.Minimum == 1 && estimate.Maximum == 2, "Ambiguous +4 gives range");
        estimate.Observe(4, 1, false); Check(estimate.Minimum == 0 && estimate.Maximum == 0, "Delayed kill reconciles +4");
        estimate.Observe(7, 1, false); Check(estimate.Minimum == 1 && estimate.Maximum == 1, "Known kill leaves one assist");
        estimate.Observe(100, 4, false); Check(estimate.Unbounded && estimate.Maximum == null, "Gauge cap hides future assists");
        var partial = new BattleHighEstimate(); partial.Observe(20, 0, true); Check(partial.Unbounded, "Late join not exact");
        var random = new Random(3); var watch = Stopwatch.StartNew();
        int[] kg = [4,6,8,12,16,20], ag = [2,3,4,6,8,10];
        for (int trial = 0; trial < 2000; trial++)
        {
            int k = random.Next(5), a = random.Next(8);
            int gauge = Enumerable.Range(0,k).Sum(_ => kg[random.Next(6)]) + Enumerable.Range(0,a).Sum(_ => ag[random.Next(6)]);
            if (gauge >= 100) continue;
            var e = new BattleHighEstimate(); e.Observe(gauge, k, false);
            Check(e.Minimum <= a && e.Maximum >= a, "Assist inference contains actual count");
        }
        Console.WriteLine($"Inference replay: {watch.Elapsed.TotalMilliseconds:F1} ms / 2,000 generated cases.");
        foreach (Mode mode in Enum.GetValues<Mode>())
        {
            var bytes = new byte[ResultDecoder.Length(mode)];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x10), 300);
            int start = mode switch { Mode.CrystallineConflict => 0x48, Mode.Frontline => 0x58, _ => 0x30 };
            int ko = mode switch { Mode.CrystallineConflict => 0x1F, Mode.Frontline => 0x2D, _ => 0x27 };
            int assist = mode switch { Mode.CrystallineConflict => 0x21, Mode.Frontline => 0x31, _ => 0x2B };
            if (mode != Mode.CrystallineConflict) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(mode == Mode.Frontline ? 0x24 : 0x28), 1);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(start + 8), 123);
            bytes[start + ko - 1] = 19; bytes[start + ko] = 8; bytes[start + ko + 1] = 3; bytes[start + assist] = 12;
            if (mode == Mode.CrystallineConflict) bytes[0x3C] = 1;
            var decoded = ResultDecoder.Parse(bytes, mode, 123);
            Check(decoded is { Kills: 8, Deaths: 3, Assists: 12 }, "Result layout " + mode);
            Check(ResultDecoder.Parse(bytes, mode, 555) == null, "Reject other character " + mode);
            Check(ResultDecoder.Parse(bytes.AsSpan(0, 20), mode, 123) == null, "Short packet rejected " + mode);
            if (mode != Mode.CrystallineConflict)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(mode == Mode.Frontline ? 0x24 : 0x28), 65535);
                Check(ResultDecoder.Parse(bytes, mode, 123) == null, "Invalid player count rejected");
            }
        }
        foreach (var danger in new[] { "=1+1", "+cmd", "-10", "@name", "  =X", "\t=Y", "\rX", "\nX" }) Check(CsvExport.Cell(danger).StartsWith("\"'"), "CSV formula protection");
        Check(CsvExport.Cell("a,\"b\n") == "\"a,\"\"b\n\"", "CSV escaping");
        var store = new HistoryStore(Path.Combine(root, "history"));
        var first = store.Page(owner, 0); Check(first.Count == 0 && store.Writes == 0, "Initial read has zero writes");
        for (int i = 0; i < 60; i++) store.Save(owner, record with { Summary = record.Summary with { Id = Guid.NewGuid(), Started = now.AddSeconds(i) } });
        var page = store.Page(owner, 0); Check(page.Rows.Length == 25 && page.Count == 60, "Paged history");
        int reads = store.Reads, writes = store.Writes; long bytesRead = store.ReadBytes;
        for (int i = 0; i < 500; i++) store.Page(owner, 0);
        Check(store.Reads == reads && store.Writes == writes && store.ReadBytes == bytesRead, "500 unchanged opens: zero reads/writes/bytes");
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => store.Page(owner, 1))));
        Check(store.Reads == reads + 25, "Concurrent page requests coalesced by cache");
        Check(store.Writes == writes, "Page navigation zero writes");
        var loaded = store.Detail(owner, page.Rows[0]); Check(loaded.Events.Length == 3, "Detail single file");
        store.Save(owner, loaded); Check(store.Writes == writes, "Idempotent save");
        var csv = store.Export(owner, page.Rows, null); Check(File.ReadAllBytes(csv).Take(3).SequenceEqual(new byte[] {239,187,191}), "CSV UTF-8 BOM");
        Check(File.ReadAllLines(csv).Length == 26, "CSV page bounded");
        var eventsPath = store.Export(owner, [], loaded); Check(File.ReadAllLines(eventsPath).Length == 4, "Event CSV");
        Check(store.Page(HistoryStore.OwnerKey(456), 0).Count == 0, "Character isolation");
        Check(store.ClaimDelivery(owner, loaded.Summary) && !store.ClaimDelivery(owner, loaded.Summary), "Durable delivery dedupe");
        Check(!new HistoryStore(Path.Combine(root, "history")).ClaimDelivery(owner, loaded.Summary), "Delivery dedupe survives restart");
        Console.WriteLine($"Storage measured: first page {reads} summary reads, cached opens +0 reads/+0 writes/+0 bytes; concurrent page +25 reads; detail +1 read. Summary bytes {bytesRead}; save writes {writes}.");
        var old = record with { Summary = record.Summary with { Id = Guid.NewGuid(), Started = now.AddDays(-366) } }; store.Save(owner, old);
        Check(store.Page(owner, 0).Count == 60, "Expiry pruning on save");

        var codes = new[] {"ja","en","de","fr","ko","zh-Hans","zh-Hant"};
        var locales = JsonSerializer.Deserialize<Dictionary<string,string[]>>(File.ReadAllText(Path.Combine(args[1], "PvPLedger/Locales.json")))!;
        foreach (var (key, values) in locales)
        { Check(values.Length == 7 && values.All(v => !string.IsNullOrWhiteSpace(v)), "Localization completeness " + key); foreach (var code in codes) Check(Loc.Get(code,key) == values[Array.IndexOf(codes, code)], "Localized " + key); }
        foreach (var file in Directory.GetFiles(Path.Combine(args[1], "PvPLedger"), "*.cs"))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "(?:T|status = |return )\\(?\"([A-Z][A-Za-z]+)\""))
                Check(locales.ContainsKey(m.Groups[1].Value), "Missing localization key " + m.Groups[1].Value);
        foreach (var (input, expected) in new[] {("ja_JP","ja"),("EN-gb","en"),("de-DE","de"),("fr-FR","fr"),("ko-KR","ko"),("zh-TW","zh-Hant"),("zh-Hans-CN","zh-Hans"),("zh-Hant-HK","zh-Hant"),("zh-SG","zh-Hans")}) Check(Loc.Normalize(input) == expected, "Language normalize");
        Check(Loc.Normalize("zh") == null && Loc.Normalize("auto") == null && Loc.Normalize("xx") == null, "No guessed language");
        Check(Loc.Resolve("ko", ClientLanguage.Japanese, "en") == "ko", "Saved choice wins");
        Check(Loc.Resolve(null, ClientLanguage.Japanese, "en") == "ja", "Game before UI");
        Check(Loc.Resolve(null, null, "zh-Hant") == "zh-Hant", "UI fallback");
        Check(Loc.Resolve(null, null, null) == "en", "English fallback");
        var protectedText = SecretStore.Protect("synthetic-secret-for-offline-test");
        Check(!protectedText.Contains("synthetic") && SecretStore.Unprotect(protectedText) == "synthetic-secret-for-offline-test", "DPAPI roundtrip");
        var url = "https://discord.com/api/webhooks/123456789012345678/" + new string('a', 60);
        Check(Webhook.TryUrl(url, out var validated) && validated!.Query == "?wait=true", "Strict webhook URL");
        foreach (var invalid in new[] {url.Replace("https", "http"), url.Replace("discord.com", "discord.com.evil.example"), "http://localhost/", url + "?x=1", url + "#x", url.Replace("discord.com", "user@discord.com")}) Check(!Webhook.TryUrl(invalid, out _), "Invalid URL rejected");
        var handler = new FakeHttp(); using var webhook = new Webhook(handler);
        Check(await webhook.Send(url, record.Summary, "ja", false, default) == "Sent", "Successful webhook");
        Check(!handler.Body.Contains("Example Player") && !handler.Body.Contains("Enemy Player"), "Discord minimal data");
        Check(JsonDocument.Parse(handler.Body).RootElement.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength() == 0, "No mentions");
        handler.Code = HttpStatusCode.TooManyRequests; Check(await webhook.Send(url,record.Summary,"en",true,default) == "RateLimited" && handler.Calls == 2, "Rate limit no retry");
        handler.Throw = true; Check(await webhook.Send(url,record.Summary,"en",false,default) == "SendUnknown" && handler.Calls == 3, "Unknown delivery no retry");
        UiChecks.Run(Path.Combine(root, "ui"));
        Console.WriteLine($"PASS: {Checks} core, storage, privacy, webhook, localization and UI checks.");
    }
}
internal sealed class FakeHttp : HttpMessageHandler
{
    public int Calls; public string Body = ""; public bool Throw; public HttpStatusCode Code = HttpStatusCode.OK;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    { Calls++; Body = await request.Content!.ReadAsStringAsync(token); if (Throw) throw new HttpRequestException("synthetic timeout"); return new(Code); }
}
