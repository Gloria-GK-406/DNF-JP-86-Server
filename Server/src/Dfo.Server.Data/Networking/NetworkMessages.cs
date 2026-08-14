namespace Dfo.Server.Data.Networking;

public enum ClientMessageType
{
    GameLogin = 0x0001_0001,
    GameGetAccountCharacterRoster = 0x0001_0208,
    GameSelectCharacter = 0x0001_0004,
    ChannelScriptVersion = 0x05,
    ChannelScript = 0x09,
    ChannelConnect = 0x0B,
    ChannelInfo = 0x11,
    GameExit = 0x0001_0003,
    GameClientSpecStatistic = 0x0001_00AB,
    GameClientHardwareDetail = 0x0001_01DD,
    GameClientSpecProfile = 0x0001_00C2,
}

public enum ServerMessageType
{
    GameInitialConnection = 0x0000_0001,
    GameLogin = 0x0001_0001,
    GameAccountCharacterRoster = 0x0000_0002,
    GameSelectCharacter = 0x0001_0004,
    ChannelScriptVersion = 0x06,
    ChannelScript = 0x0A,
    ChannelConnect = 0x0C,
    ChannelInfo = 0x12,
    GameExit = 0x0001_0003,
}

public interface IClientNetworkMessage
{
    ClientMessageType MessageType { get; }
}

public interface IServerNetworkMessage
{
    ServerMessageType MessageType { get; }
}

public sealed record ChannelMessageMetadata(
    byte Classification,
    byte MessageNumber,
    uint Checksum,
    byte Acknowledgement)
{
    public static ChannelMessageMetadata Empty { get; } = new(0, 0, 0, 0);
}

public sealed record ChannelConnectRequest(ChannelMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.ChannelConnect;
}

public sealed record ChannelConnectResponse(string EncryptionKey) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.ChannelConnect;
}

public sealed record ChannelScriptVersionRequest(ChannelMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.ChannelScriptVersion;
}

public sealed record ChannelScriptVersionResponse(string Version) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.ChannelScriptVersion;
}

public sealed record ChannelScriptRequest(ChannelMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.ChannelScript;
}

public sealed record ChannelScriptResponse(byte[] Content) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.ChannelScript;
}

public sealed record ChannelInfoRequest(ChannelMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.ChannelInfo;
}

public sealed record ChannelInfoResponse(
    IReadOnlyList<Dfo.Server.Data.Channel.ChannelServerInfo> Channels) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.ChannelInfo;
}

public sealed record GameMessageMetadata(
    byte Command,
    ushort Type,
    uint Checksum,
    ushort Sequence)
{
    public static GameMessageMetadata Empty { get; } = new(0, 0, 0, 0);
}

public sealed record GameExitRequest(GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameExit;
}

public sealed record GameLoginRequest(
    GameMessageMetadata Metadata,
    string LoginId,
    string PasswordHash) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameLogin;
}

public sealed record GameLoginResponse(bool Succeeded) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameLogin;
}

public sealed record GameInitialConnectionNotice(
    int ChannelId,
    DateTimeOffset PreparedAt) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameInitialConnection;
}

public sealed record GetAccountCharacterRosterRequest(
    GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameGetAccountCharacterRoster;
}

public sealed record AccountCharacterRosterResponse(
    Dfo.Server.Data.Characters.AccountCharacterRoster Roster) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameAccountCharacterRoster;
}

public sealed record SelectCharacterRequest(
    GameMessageMetadata Metadata,
    ushort SlotIndex) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSelectCharacter;
}

public sealed record SelectCharacterResponse(
    Dfo.Server.Data.Game.SelectCharacterResult Result) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSelectCharacter;
}

/// <summary>An explicitly addressed best-effort projection batch for one connection.</summary>
public sealed record ExplicitProjectionTarget(
    NetworkConnectionId ConnectionId,
    IReadOnlyList<Dfo.Server.Data.Game.ISelectCharacterSupplementalProjection> OrderedProjections);

/// <summary>
/// Typed Select Character projections. Networking frames them; required direct values are delivered first;
/// targets are already selected by the caller and are attempted best-effort afterwards.
/// </summary>
public sealed record SelectCharacterProjectionDelivery(
    IReadOnlyList<Dfo.Server.Data.Game.ISelectCharacterSupplementalProjection> RequiredDirectProjections,
    IReadOnlyList<ExplicitProjectionTarget> BestEffortTargets) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSelectCharacter;
}

public sealed record GameExitResponse(bool Succeeded) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameExit;
}

/// <summary>
/// One fixed slot of the CLIENT_SPEC_STATISTIC (0x00AB) hardware report.
/// Slot semantics by tag (see protocol/game/client-to-server/0x00AB-client-spec-statistic):
/// 0 = CPU, 1 = graphics adapter, 2 = operating system, 3 = physical memory.
/// </summary>
public sealed record ClientSpecStatisticSlot(byte Tag, uint Value1, uint Value2);

/// <summary>
/// CLIENT_SPEC_STATISTIC report (cmd 0x01/type 0x00AB). Fire-and-forget telemetry the client
/// sends once between Game LOGIN and GET_USERINFO; no response exists on the wire.
/// </summary>
public sealed record ClientSpecStatisticReportRequest(
    GameMessageMetadata Metadata,
    byte TriggerPath,
    byte ReportedFlag,
    IReadOnlyList<ClientSpecStatisticSlot> Slots) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameClientSpecStatistic;
}

/// <summary>
/// CLIENT_HARDWARE_DETAIL report (cmd 0x01/type 0x01DD). Fire-and-forget telemetry the client
/// sends immediately after CLIENT_SPEC_STATISTIC (0x00AB) and before GET_USERINFO; no response
/// exists on the wire. Where 0x00AB reports bucketed values, this carries the raw ones plus the
/// OS bitness that 0x00AB omits. Field names mirror the instruction-confirmed wire layout in
/// protocol/game/client-to-server/0x01DD-client-hardware-detail; widths are the wire widths.
///
/// Unlike 0x00AB this report is conditional: the client skips it entirely when its system-info
/// provider or its IsWow64Process pointer is null, so a session may legitimately never send it.
/// </summary>
public sealed record ClientHardwareDetailReportRequest(
    GameMessageMetadata Metadata,
    byte CpuVendorBucket,
    byte CpuLogicalCores,
    uint CpuClockMhz,
    ushort TotalMemoryMegabytes,
    ushort GpuPciVendorId,
    ushort GpuPciDeviceId,
    ushort VideoMemoryMegabytes,
    byte OperatingSystemKind,
    byte OperatingSystemBitness) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameClientHardwareDetail;
}

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
/// Fields with no confirmed meaning are named for their body offset rather than guessed at; see
/// protocol/game/client-to-server/0x00C2-client-spec-profile.
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
    uint Unknown15,
    byte Unknown19,
    ushort Unknown20,
    ushort Unknown22,
    IReadOnlyList<ClientSpecProfileRow> Table,
    IReadOnlyList<ClientSpecProfileEntry> Entries) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameClientSpecProfile;
}
