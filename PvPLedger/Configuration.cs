using Dalamud.Configuration;
namespace PvPLedger;
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string? Language { get; set; }
    public bool OverlayEnabled { get; set; } = true;
    public bool Locked { get; set; } = true;
    public float X { get; set; } = 40;
    public float Y { get; set; } = 200;
    public float Scale { get; set; } = 1;
    public float Opacity { get; set; } = .82f;
    public int FeedRows { get; set; } = 3;
    public int FeedSeconds { get; set; } = 12;
    public bool EstimateAssists { get; set; } = true;
    public bool DiscordEnabled { get; set; }
    public bool DiscordIncludeName { get; set; }
    public string ProtectedWebhook { get; set; } = "";
    public void Sanitize()
    {
        if (!float.IsFinite(X)) X = 40; if (!float.IsFinite(Y)) Y = 200;
        Scale = float.IsFinite(Scale) ? Math.Clamp(Scale, .7f, 1.6f) : 1;
        Opacity = float.IsFinite(Opacity) ? Math.Clamp(Opacity, .2f, 1f) : .82f;
        FeedRows = Math.Clamp(FeedRows, 0, 5); FeedSeconds = Math.Clamp(FeedSeconds, 3, 30);
    }
}
