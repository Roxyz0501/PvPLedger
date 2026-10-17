using System.Net;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace PvPLedger;

public sealed class Webhook : IDisposable
{
    private readonly HttpClient client;
    public Webhook(HttpMessageHandler? handler = null)
    {
        client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 32 * 1024 };
    }
    public static bool TryUrl(string text, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var u) || u.Scheme != "https" || u.Host != "discord.com" || !u.IsDefaultPort || u.UserInfo.Length != 0 || u.Query.Length != 0 || u.Fragment.Length != 0) return false;
        var p = u.AbsolutePath.Split('/');
        if (p.Length != 5 || p[1] != "api" || p[2] != "webhooks" || p[3].Length is < 16 or > 22 || !p[3].All(char.IsAsciiDigit) || p[4].Length is < 40 or > 150 || !p[4].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return false;
        uri = new Uri(u.GetLeftPart(UriPartial.Path) + "?wait=true"); return true;
    }
    public static string Payload(MatchSummary s, string language, bool name)
    {
        string T(string k) => Loc.Get(language, k);
        var description = $"{T(s.Mode.ToString())} · {T(s.Outcome.ToString())}\n{s.Arena}\nK {s.Kills}  /  D {s.Deaths}  /  A {s.Assists?.ToString() ?? "—"}";
        if (name) description += "\n" + s.Player;
        return JsonSerializer.Serialize(new { username = "PvP Ledger", allowed_mentions = new { parse = Array.Empty<string>() }, embeds = new[] {
            new { title = T("MatchResult"), description, color = 0x66DDD0, timestamp = s.Ended.ToUniversalTime().ToString("O"),
                footer = new { text = $"{s.Id:N} · {T(s.Official ? "Official" : "Observed")}" } }
        }});
    }
    public async Task<string> Send(string url, MatchSummary summary, string language, bool name, CancellationToken ct)
    {
        if (!TryUrl(url, out var uri)) return "WebhookInvalid";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new StringContent(Payload(summary, language, name), Encoding.UTF8, "application/json") };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return "Sent";
            return response.StatusCode == HttpStatusCode.TooManyRequests ? "RateLimited" : "SendFailed";
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { return "SendUnknown"; }
        // Deliberately never expose exception text: an exception may contain the webhook token.
    }
    public void Dispose() => client.Dispose();
}

public static class SecretStore
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public nint Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
    private static byte[] Transform(byte[] input, bool protect)
    {
        var blob = new Blob { Size = input.Length, Data = Marshal.AllocHGlobal(input.Length) }; Blob result = default;
        try
        {
            Marshal.Copy(input, 0, blob.Data, input.Length);
            var ok = protect ? CryptProtectData(ref blob, "PvP Ledger webhook", 0, 0, 0, 1, out result) : CryptUnprotectData(ref blob, 0, 0, 0, 0, 1, out result);
            if (!ok) throw new System.Security.Cryptography.CryptographicException("Windows secret protection failed");
            var bytes = new byte[result.Size]; Marshal.Copy(result.Data, bytes, 0, bytes.Length); return bytes;
        }
        finally { Marshal.Copy(new byte[input.Length], 0, blob.Data, input.Length); Marshal.FreeHGlobal(blob.Data); if (result.Data != 0) LocalFree(result.Data); Array.Clear(input); }
    }
    public static string Protect(string text) => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(text), true));
    public static string Unprotect(string text)
    {
        if (text.Length == 0) return "";
        var bytes = Transform(Convert.FromBase64String(text), false);
        try { return Encoding.UTF8.GetString(bytes); } finally { Array.Clear(bytes); }
    }
}
