using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ClientSpecProfile;

/// <summary>One (u32, f32) pair of a CLIENT_SPEC_PROFILE (0x00C2) table row. Both members are
/// named for their wire type rather than a meaning: the client sources them from an array the
/// serializer walks blindly, and no semantics are confirmed yet.</summary>
public sealed record ClientSpecProfilePair(uint Integer, float Real);

/// <summary>One row of the fixed 6-row CLIENT_SPEC_PROFILE (0x00C2) table: four u16 words
/// (each the truncated integer value of a client-side float) then six (u32, f32) pairs.</summary>
public sealed record ClientSpecProfileRow(
    IReadOnlyList<ushort> Words,
    IReadOnlyList<ClientSpecProfilePair> Pairs);

/// <summary>One entry of the variable trailing list of CLIENT_SPEC_PROFILE (0x00C2). The client
/// clamps <paramref name="Kind"/> to 6 and <paramref name="Value"/> to 2000 before writing.</summary>
public sealed record ClientSpecProfileEntry(byte Kind, ushort Value);

/// <summary>
/// CLIENT_SPEC_PROFILE report (cmd 0x01/type 0x00C2). Fire-and-forget telemetry; the third and
/// largest of the hardware reports the client sends, after 0x00AB (bucketed) and 0x01DD (raw).
/// Its first 15 bytes come from the same report object those two use, with the two memory figures
/// passed through a normalizing filter, so they are comparable but not byte-identical.
///
/// Most header names come from the client's own plaintext LagLog.txt, which dumps the same
/// statistics at exit and lines up with the wire field for field. <paramref name="Unknown19"/>
/// keeps its offset name: it occupies the position LagLog calls TING, but the two disagree in
/// value and it is supplied by the serializer's caller rather than read from the report object.
/// See protocol/game/client-to-server/0x00C2.md.
/// </summary>
public sealed record ClientSpecProfileReportRequest(
    GameMessageMetadata Metadata,
    uint CpuClockMhz,
    ushort CpuLogicalCores,
    ushort GpuPciVendorId,
    ushort GpuPciDeviceId,
    ushort NormalizedVideoMemoryMegabytes,
    ushort NormalizedTotalMemoryMegabytes,
    byte OperatingSystemKind,
    uint DirectXVersion,
    byte Unknown19,
    short StartLoadingSeconds,
    short ReturnVillageSeconds,
    IReadOnlyList<ClientSpecProfileRow> Table,
    IReadOnlyList<ClientSpecProfileEntry> Entries) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameClientSpecProfile;
}
