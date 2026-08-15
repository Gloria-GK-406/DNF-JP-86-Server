using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.SessionMetrics;

/// <summary>
/// One of the seven fixed metric sections of SESSION_METRICS (0x00FA). The value is a mean the
/// client already computed as total/samples, truncated toward zero, and written as 0 when it had
/// no samples -- so 0 means "nothing measured", not "measured zero".
/// Which metric each tag denotes is unconfirmed; see protocol/game/client-to-server/0x00FA.md.
/// </summary>
public sealed record SessionMetricMean(ushort Tag, uint Mean);

/// <summary>
/// One record of a SESSION_METRICS (0x00FA) counted section. Nine wire bytes drawn from a
/// ten-byte source record, so the source's padding byte is absent here. No field has a confirmed
/// meaning, so all five are named for their offset and width.
/// </summary>
public sealed record SessionMetricRecord(
    ushort Word0,
    byte Byte2,
    ushort Word3,
    ushort Word5,
    ushort Word7);

/// <summary>
/// SESSION_METRICS report (cmd 0x01/type 0x00FA). Fire-and-forget; the second frame of the
/// client's normal-exit flush. Seven tagged means followed by two counted sections that share a
/// record shape. Nothing depends on its arrival: a force-killed client never sends it.
/// </summary>
public sealed record SessionMetricsReportRequest(
    GameMessageMetadata Metadata,
    IReadOnlyList<SessionMetricMean> Means,
    IReadOnlyList<SessionMetricRecord> Section7,
    IReadOnlyList<SessionMetricRecord> Section8) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSessionMetrics;
}
