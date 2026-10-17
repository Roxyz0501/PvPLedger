using System.Buffers.Binary;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.System.Framework;

namespace PvPLedger;

// Packet offsets / signatures: wrath16/PvpStats, MIT. See NOTICE and PvpStats-LICENSE.txt.
// Only the local ContentId is compared, and only the local result leaves the callback.
public static class ResultDecoder
{
    public static int Length(Mode mode) => mode switch { Mode.CrystallineConflict => 0x48 + 0x50 * 10, Mode.Frontline => 0x58 + 0x60 * 72, _ => 0x30 + 0x50 * 48 };
    public static FinalResult? Parse(ReadOnlySpan<byte> data, Mode mode, ulong localContentId)
    {
        if (localContentId == 0 || data.Length < Length(mode)) return null;
        var seconds = BinaryPrimitives.ReadUInt16LittleEndian(data[0x10..]);
        if (seconds is < 1 or > 3600) return null;
        var count = mode switch { Mode.CrystallineConflict => 10, Mode.Frontline => BinaryPrimitives.ReadUInt16LittleEndian(data[0x24..]), _ => BinaryPrimitives.ReadUInt16LittleEndian(data[0x28..]) };
        var max = mode switch { Mode.CrystallineConflict => 10, Mode.Frontline => 72, _ => 48 };
        if (count < 1 || count > max) return null;
        int start = mode switch { Mode.CrystallineConflict => 0x48, Mode.Frontline => 0x58, _ => 0x30 };
        int stride = mode == Mode.Frontline ? 0x60 : 0x50;
        int ko = mode switch { Mode.CrystallineConflict => 0x1F, Mode.Frontline => 0x2D, _ => 0x27 };
        int assist = mode switch { Mode.CrystallineConflict => 0x21, Mode.Frontline => 0x31, _ => 0x2B };
        for (var i = 0; i < count; i++)
        {
            var row = data.Slice(start + stride * i, stride);
            if (BinaryPrimitives.ReadUInt64LittleEndian(row[8..]) != localContentId || row[ko - 1] == 0) continue;
            MatchOutcome outcome;
            if (mode == Mode.CrystallineConflict) outcome = data[0x3C] switch { 1 => MatchOutcome.Victory, 2 => MatchOutcome.Defeat, _ => MatchOutcome.Unknown };
            else if (mode == Mode.RivalWings) outcome = data[0x22] switch { 0 => MatchOutcome.Victory, 1 => MatchOutcome.Defeat, _ => MatchOutcome.Unknown };
            else
            {
                var team = row[0x2F]; if (team > 2) return null;
                outcome = BinaryPrimitives.ReadUInt16LittleEndian(data[(0x28 + team * 0xE)..]) switch { 0 => MatchOutcome.First, 1 => MatchOutcome.Second, 2 => MatchOutcome.Third, _ => MatchOutcome.Unknown };
            }
            return new(mode, seconds, row[ko], row[ko + 1], row[assist], outcome);
        }
        return null;
    }
}

internal sealed unsafe class NativeResults : IDisposable
{
    public const string SupportedGameBuild = "2026.10.06.0000.0000";
    internal const string CcSignature = "40 55 53 56 57 41 54 41 55 41 56 41 57 48 8D AC 24 ?? ?? ?? ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 85 ?? ?? ?? ?? 0F B6 42";
    internal const string FlSignature = "4C 8B DC 55 56 57 41 56 49 8D AB";
    internal const string RwSignature = "40 55 57 48 81 EC ?? ?? ?? ?? 48 8B F9 48 8B EA";
    private delegate void CcEnd(nint director, nint packet, nint p3, uint p4);
    private delegate void End(nint director, nint packet);
    private Hook<CcEnd>? cc;
    private Hook<End>? fl, rw;
    private readonly Func<MatchSession?> session;
    private readonly Action<Guid, FinalResult> received;
    public bool Available { get; private set; }
    public string Status { get; private set; } = "NativeUnavailable";
    public NativeResults(IGameInteropProvider interop, Func<MatchSession?> session, Action<Guid, FinalResult> received, IPluginLog log)
    {
        this.session = session; this.received = received;
        // Raw layouts are version-specific. Never read an unknown game's offsets.
        try
        {
            if (Framework.Instance() == null || Framework.Instance()->GameVersionString != SupportedGameBuild) return;
            cc = interop.HookFromSignature<CcEnd>(CcSignature, OnCc);
            fl = interop.HookFromSignature<End>(FlSignature, OnFl);
            rw = interop.HookFromSignature<End>(RwSignature, OnRw);
            cc.Enable(); fl.Enable(); rw.Enable(); Available = true; Status = "NativeReady";
        }
        catch { Dispose(); log.Warning("PvP Ledger result hooks unavailable; recording observed events only."); }
    }
    private void Read(nint packet, Mode mode)
    {
        var current = session();
        if (packet == 0 || current == null || current.Mode != mode || current.Result != null) return;
        try
        {
            var result = ResultDecoder.Parse(new ReadOnlySpan<byte>((void*)packet, ResultDecoder.Length(mode)), mode, current.ContentId);
            if (result != null) received(current.Id, result);
        }
        catch { /* Never let a managed exception cross the native callback boundary. */ }
    }
    private void OnCc(nint director, nint packet, nint p3, uint p4) { try { Read(packet, Mode.CrystallineConflict); } finally { cc!.Original(director, packet, p3, p4); } }
    private void OnFl(nint director, nint packet) { try { Read(packet, Mode.Frontline); } finally { fl!.Original(director, packet); } }
    private void OnRw(nint director, nint packet) { try { Read(packet, Mode.RivalWings); } finally { rw!.Original(director, packet); } }
    public int? ReadBattleHigh()
    {
        if (!Available || session()?.Mode != Mode.Frontline) return null;
        var framework = EventFramework.Instance();
        if (framework == null) return null;
        var director = framework->GetInstanceContentDirector();
        if (director == null || director->InstanceContentType != FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.InstanceContentType.Frontlines) return null;
        // The field is documented by FrontlineContentDirector.PlayerBattleHigh in the reference.
        var gauge = *((byte*)director + 0x24D2 + 0xEE5);
        return gauge <= 100 ? gauge : null;
    }
    public void Dispose() { Available = false; cc?.Dispose(); fl?.Dispose(); rw?.Dispose(); cc = null; fl = null; rw = null; }
}
