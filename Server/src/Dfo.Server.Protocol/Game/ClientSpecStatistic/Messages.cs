using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ClientSpecStatistic;

/// <summary>
/// One fixed slot of the CLIENT_SPEC_STATISTIC (0x00AB) hardware report.
/// Slot semantics by tag (see protocol/game/client-to-server/0x00AB.md):
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
