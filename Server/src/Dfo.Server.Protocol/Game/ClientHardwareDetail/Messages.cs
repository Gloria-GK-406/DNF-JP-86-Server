using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ClientHardwareDetail;

/// <summary>
/// CLIENT_HARDWARE_DETAIL report (cmd 0x01/type 0x01DD). Fire-and-forget telemetry the client
/// sends immediately after CLIENT_SPEC_STATISTIC (0x00AB) and before GET_USERINFO; no response
/// exists on the wire. Where 0x00AB reports bucketed values, this carries the raw ones plus the
/// OS bitness that 0x00AB omits. Field names mirror the instruction-confirmed wire layout in
/// protocol/game/client-to-server/0x01DD.md; widths are the wire widths.
///
/// Unlike 0x00AB this report is conditional, and strongly so: the client only builds it when a
/// "CrashDNF2.cra" crash file exists in its working directory, and skips it when its system-info
/// provider or its IsWow64Process pointer is null. A freshly installed client that has never
/// crashed therefore never sends it, so nothing may depend on its arrival.
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
