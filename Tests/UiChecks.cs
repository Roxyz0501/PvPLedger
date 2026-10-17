using System.Reflection;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PvPLedger;

internal static unsafe class UiChecks
{
    static T Stub<T>() where T : class => DispatchProxy.Create<T, UiStub>();
    public static void Run(string output)
    {
        Directory.CreateDirectory(output); UiStub.Directory = Path.Combine(output, "config");
        ImGui.CreateContext(); var io = ImGui.GetIO(); io.DisplaySize = new(1440, 900); io.DeltaTime = 1f / 60;
        io.ConfigInputTrickleEventQueue = false;
        using (var plugin = new Plugin(Stub<IDalamudPluginInterface>(), Stub<ICommandManager>(), Stub<IClientState>(), Stub<IPlayerState>(),
            Stub<IDutyState>(), Stub<IDataManager>(), Stub<IFramework>(), Stub<IChatGui>(), Stub<IGameInteropProvider>(), Stub<IPluginLog>()))
        {
            var type = plugin.GetType();
            void Set(string field, object? value) => type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(plugin,value);
            object? Call(string method) => type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(plugin,null);
            var owner = HistoryStore.OwnerKey(123);
            Set("owner", owner); Set("open", true); Set("preview", true);
            UiStub.Config.X = 1020; UiStub.Config.Y = 200;
            var now = DateTimeOffset.UtcNow;
            var sample = new MatchSummary(Guid.NewGuid(), now.AddMinutes(-5), now, Mode.Frontline, "Test Arena", "Example Player", 7, 2, 16, MatchOutcome.First, true, false, false, 4, 12, false);
            Set("history", new[] {sample, sample with { Id = Guid.NewGuid(), Mode = Mode.CrystallineConflict, Outcome = MatchOutcome.Victory }, sample with { Id = Guid.NewGuid(), Mode = Mode.RivalWings, Outcome = MatchOutcome.Defeat }});
            Set("count", 3);
            io.Fonts.Build();
            var loc = typeof(Loc); var fontText = Loc.FontText;
            foreach (var f in UiStub.Fonts) foreach (var ch in fontText.Distinct().Where(c => !char.IsControl(c))) Program.Check(f.FindGlyphNoFallback(ch) != null, "Glyph " + ch);
            var textures = new Dictionary<ulong, (nint,int,int)>();
            for (int i = 0; i < io.Fonts.Textures.Size; i++)
            {
                byte* pixels; int w = 0, h = 0, bpp = 0; io.Fonts.GetTexDataAsRGBA32(i, &pixels, &w, &h, &bpp);
                io.Fonts.SetTexID(i, new ImTextureID((ulong)i + 1)); textures[(ulong)i + 1] = ((nint)pixels, w, h);
            }
            void Frame() { ImGui.NewFrame(); Call("Draw"); ImGui.Render(); }
            void Click(float x, float y) { io.AddMousePosEvent(x,y); Frame(); io.AddMouseButtonEvent(0,true); Frame(); io.AddMouseButtonEvent(0,false); Frame(); Frame(); }
            foreach (var language in new[] { "ja", "en", "de", "fr", "ko", "zh-Hans", "zh-Hant" })
            {
                UiStub.Config.Language = language; Frame(); Frame(); Frame();
                Program.Check(ImGui.GetDrawData().TotalVtxCount > 0, "Production UI draws " + language);
                Renderer.Render(ImGui.GetDrawData(), textures, Path.Combine(output, language + ".bmp"));
            }
            UiStub.Config.Language = "ja"; Frame(); Frame();
            var saves = UiStub.Saves;
            for (int i = 0; i < 200; i++) Frame();
            Program.Check(UiStub.Saves == saves, "Unchanged rendering: zero config writes");
            var historyStore = (HistoryStore)type.GetField("store", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(plugin)!;
            Program.Check(historyStore.Reads == 0 && historyStore.Writes == 0, "UI frames: zero history IO");
            // Tab header lies at y=124 in the production window after the title and subtitle.
            Click(187, 123); Frame(); Renderer.Render(ImGui.GetDrawData(), textures, Path.Combine(output, "settings-ja.bmp"));
            // Render each tab directly in a real ImGui window to verify all localized content and scope balance.
            foreach (var language in new[] { "ja", "en", "de", "fr", "ko", "zh-Hans", "zh-Hant" })
            {
                UiStub.Config.Language = language;
                foreach (var method in new[] { "DrawSettings", "DrawLanguage" })
                {
                    ImGui.NewFrame(); ImGui.SetNextWindowPos(new(60,60)); ImGui.SetNextWindowSize(new(920,800));
                    ImGui.Begin(method + "###Direct" + method);
                    var fonts = type.GetField("fonts", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(plugin)!;
                    using ((IDisposable)fonts.GetType().GetMethod("Push")!.Invoke(fonts, [language])!) Call(method);
                    ImGui.End(); ImGui.Render();
                    Renderer.Render(ImGui.GetDrawData(), textures, Path.Combine(output, method + "-" + language + ".bmp"));
                }
            }
            Program.Check(UiStub.Saves == saves, "Tabs: zero config writes");
            // Replay end -> duplicate end -> official result -> duplicate result using the real lifecycle.
            var match = new MatchSession(now.AddMinutes(-5), Mode.Frontline, "Test Arena", "Example Player", owner,123,376,false);
            Set("current", match); Set("open", false);
            var onEnd = type.GetMethod("OnEnd", BindingFlags.NonPublic | BindingFlags.Instance)!;
            onEnd.Invoke(plugin, [null]); onEnd.Invoke(plugin, [null]);
            Program.Check(match.Ended != null, "Real duty end transition");
            var onResult = type.GetMethod("OnResult", BindingFlags.NonPublic | BindingFlags.Instance)!;
            onResult.Invoke(plugin, [match.Id, new FinalResult(Mode.Frontline,300,7,2,16,MatchOutcome.First)]);
            onResult.Invoke(plugin, [match.Id, new FinalResult(Mode.Frontline,300,7,2,16,MatchOutcome.First)]);
            Program.Check(type.GetField("current",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(plugin) == null, "Finalized session cleared");
            Set("preview", false);
        }
        Program.Check(UiStub.Removes == 1, "Command unregisters on unload");
        ImGui.DestroyContext(); foreach (var pin in UiStub.Pins) pin.Free();
    }
}

public unsafe class UiStub : DispatchProxy
{
    public static Configuration Config = new() { Language = "ja" };
    public static string Directory = "";
    public static int Saves, Removes;
    public static List<ImFontPtr> Fonts = [];
    public static List<GCHandle> Pins = [];
    public ImFontPtr Font;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case "GetPluginConfig": return Config;
            case "GetPluginConfigDirectory": return Directory;
            case "SavePluginConfig": Saves++; return null;
            case "AddHandler": return true;
            case "RemoveHandler": Removes++; return true;
            case "get_UiBuilder": case "get_FontAtlas": return Create(method.ReturnType, typeof(UiStub));
            case "get_BuildStep": return FontAtlasBuildStep.PreBuild;
            case "NewDelegateFontHandle":
                var tk = Create<IFontAtlasBuildToolkitPreBuild, UiStub>(); ((FontAtlasBuildStepDelegate)args![0]!)(tk);
                var handle = Create<IFontHandle, UiStub>(); ((UiStub)(object)handle).Font = Fonts.Last(); return handle;
            case "AddDalamudAssetFont":
                var safe = (SafeFontConfig)args![1]!;
                var pin = GCHandle.Alloc(safe.GlyphRanges, GCHandleType.Pinned); Pins.Add(pin);
                var native = ImGui.ImFontConfig(); native.FontDataOwnedByAtlas = true; native.OversampleH = 2; native.OversampleV = 1;
                native.RasterizerMultiply = 1; native.GlyphMaxAdvanceX = float.MaxValue; native.FontNo = safe.FontNo;
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher/dalamudAssets/dev/UIRes/NotoSansCJK-Regular.ttc");
                var font = ImGui.GetIO().Fonts.AddFontFromFileTTF(path, safe.SizePx, native, (ushort*)pin.AddrOfPinnedObject()); native.Destroy(); Fonts.Add(font); return font;
            case "Push": ImGui.PushFont(Font); return new Pop();
        }
        return method.ReturnType == typeof(void) ? null : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
    }
    private sealed class Pop : IDisposable { public void Dispose() => ImGui.PopFont(); }
}
