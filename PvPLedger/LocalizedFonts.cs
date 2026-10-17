using Dalamud;
using Dalamud.Interface.ManagedFontAtlas;

namespace PvPLedger;

internal sealed class LocalizedFonts : IDisposable
{
    private readonly IFontHandle[] fonts;

    public LocalizedFonts(IFontAtlas atlas, IEnumerable<string> itemNames)
    {
        var glyphs = (Loc.FontText + string.Concat(itemNames)
            + new string(Enumerable.Range(32, 95).Select(x => (char)x).ToArray())).ToGlyphRange();
        fonts = Enumerable.Range(0, 4).Select(face => atlas.NewDelegateFontHandle(step => step.OnPreBuild(tk =>
            tk.AddDalamudAssetFont(DalamudAsset.NotoSansCjkRegular,
                new SafeFontConfig { SizePx = 17, FontNo = face, GlyphRanges = glyphs })))).ToArray();
    }

    private static int Face(string? code) => code switch { "zh-Hant" => 1, "zh-Hans" => 2, "ko" => 3, _ => 0 };
    public IDisposable Push(string? code) => fonts[Face(code)].Push();
    public void Dispose() { foreach (var font in fonts) font.Dispose(); }
}


