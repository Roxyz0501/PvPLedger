using System.Text.Json;
using Dalamud.Game;

namespace PvPLedger;

public static class Loc
{
    internal static readonly string[] Codes = ["ja", "en", "de", "fr", "ko", "zh-Hans", "zh-Hant"];
    internal static readonly Dictionary<string, string[]> Messages = Load();
    private static Dictionary<string, string[]> Load()
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream("PvPLedger.Locales.json")!;
        var data = JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)!;
        if (data.Any(x => x.Value.Length != 7 || x.Value.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidDataException("Incomplete localization.");
        return data;
    }
    public static string FontText => string.Concat(Messages.Values.SelectMany(x => x)) + "日本語EnglishDeutschFrançais한국어简体中文繁體中文—–…~0123456789:/";
    public static string Get(string? code, string key) => Messages.TryGetValue(key, out var values) ? values[Math.Max(0, Array.IndexOf(Codes, code) is var i && i >= 0 ? i : 1)] : key;
    public static string? Normalize(string? code)
    {
        var c = code?.Replace('_', '-').ToLowerInvariant();
        if (c is null) return null;
        if (c == "zh-hant" || c.StartsWith("zh-hant-") || c is "zh-tw" or "zh-hk" or "zh-mo") return "zh-Hant";
        if (c == "zh-hans" || c.StartsWith("zh-hans-") || c is "zh-cn" or "zh-sg") return "zh-Hans";
        return Codes.Take(5).FirstOrDefault(x => c == x || c.StartsWith(x + "-"));
    }
    public static string Resolve(string? saved, ClientLanguage? game, string? ui) => Normalize(saved) ?? game switch
    {
        ClientLanguage.Japanese => "ja", ClientLanguage.English => "en", ClientLanguage.German => "de", ClientLanguage.French => "fr",
        _ => Normalize(ui) ?? "en",
    };
}

