using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.UnansweredReports;

/// <summary>
/// The protocols the client sends inside a session and the reference server answers with nothing.
/// </summary>
/// <remarks>
/// Grouped as a family for the same reason the map-entry reports are, and answered the same way:
/// not at all. What separates them from an unregistered type is that the silence is now a decision
/// with evidence behind it rather than a gap. Every one below was counted in a working A21
/// session's own capture -- received n times, answered zero -- so a reader asking "why is nothing
/// sent here" gets an answer instead of a shrug, and the server log stops reporting them as frames
/// nobody knows.
/// <para>
/// That distinction matters more than it looks. An unhandled frame and a deliberately unanswered
/// one are indistinguishable on the wire and opposite in meaning: one is a hole and the other is
/// the protocol. While they shared a log line, every real hole was buried in the same noise --
/// which is how <c>GET_ITEM</c> sat unimplemented behind a warning that looked like six others.
/// </para>
/// <para>
/// The shared handler is typed on this marker rather than on
/// <see cref="IClientNetworkMessage"/> so the dispatcher's guard still rejects a message routed
/// here by mistake; typed on the base interface it would accept anything.
/// </para>
/// </remarks>
public interface IUnansweredReport : IClientNetworkMessage
{
    /// <summary>What the frame carried, kept whole so the log can print it.</summary>
    byte[] Body { get; }
}

/// <summary>
/// CMD 0x0026 <c>USE_SKILL</c>: the client reporting a swing it has already drawn.
/// </summary>
/// <remarks>
/// Combat is the client's to decide. It draws the skill, applies the damage, and tells the server
/// afterwards -- and the only part the server acts on is the death that follows, under
/// <c>CMD 0x0027</c>. A working A21 session received three of these and answered none.
/// <para>
/// Thirteen bytes in the live capture, e.g. <c>00 05 00 70 17 70 7E 00 00 4B A6 07 75</c>. The
/// fields are not read: nothing here decides anything about a swing.
/// </para>
/// </remarks>
public sealed record UseSkillReport(GameMessageMetadata Metadata, byte[] Body) : IUnansweredReport
{
    public ClientMessageType MessageType => ClientMessageType.GameUseSkill;
}

/// <summary>
/// CMD 0x035C <c>SYNC_ITEM_SPACE</c>: sent as the client finishes drawing a room.
/// </summary>
/// <remarks>
/// One byte, <c>FF</c> in every live frame, and sent right after each <c>START_MAP</c>. A working
/// A21 session received six and answered none. The name says the client is reconciling its own
/// inventory view; whatever it reconciles against, it is not something the server sends back.
/// </remarks>
public sealed record SyncItemSpaceReport(GameMessageMetadata Metadata, byte[] Body)
    : IUnansweredReport
{
    public ClientMessageType MessageType => ClientMessageType.GameSyncItemSpace;
}

/// <summary>CMD 0x00D9 <c>OVERFLOW_INFO</c>: three bytes, <c>01 C1 02</c> in the live capture.</summary>
/// <remarks>A working A21 session received one and answered none.</remarks>
public sealed record OverflowInfoReport(GameMessageMetadata Metadata, byte[] Body)
    : IUnansweredReport
{
    public ClientMessageType MessageType => ClientMessageType.GameOverflowInfo;
}

/// <summary>CMD 0x04F1 <c>LOG_DOMAIN_CONNECT</c>: the client's own connection telemetry.</summary>
/// <remarks>
/// Thirty bytes, sent once just after login. A working A21 session received one and answered none.
/// Telemetry for the operator's log pipeline, which this server does not have.
/// </remarks>
public sealed record LogDomainConnectReport(GameMessageMetadata Metadata, byte[] Body)
    : IUnansweredReport
{
    public ClientMessageType MessageType => ClientMessageType.GameLogDomainConnect;
}

/// <summary>CMD 0x04D7 <c>LOG_CONNECT_PROCESS</c>: the same pipeline, one step at a time.</summary>
/// <remarks>
/// Twenty-two bytes, sent twice just after login with a step counter that goes 1 then 2. A working
/// A21 session received two and answered none.
/// </remarks>
public sealed record LogConnectProcessReport(GameMessageMetadata Metadata, byte[] Body)
    : IUnansweredReport
{
    public ClientMessageType MessageType => ClientMessageType.GameLogConnectProcess;
}

/// <summary>CMD 0x007B <c>CHARACTER_STATISTIC</c>: figures the client keeps about itself.</summary>
/// <remarks>
/// Twenty-four bytes in the live capture, sent as the result screen opens. A working A21 session
/// received one and answered none.
/// </remarks>
public sealed record CharacterStatisticReport(GameMessageMetadata Metadata, byte[] Body)
    : IUnansweredReport
{
    public ClientMessageType MessageType => ClientMessageType.GameCharacterStatistic;
}

/// <summary>CMD 0x0078 <c>REQUEST_BLACKLIST</c>: asked for on the way back into town.</summary>
/// <remarks>
/// An empty body. A working A21 session received one and answered none -- which is the answer only
/// because this server has no blacklist to send; a server that had one would owe a list here.
/// </remarks>
public sealed record RequestBlacklistReport(GameMessageMetadata Metadata, byte[] Body)
    : IUnansweredReport
{
    public ClientMessageType MessageType => ClientMessageType.GameRequestBlacklist;
}

/// <summary>CMD 0x02BE <c>FARM_EVENT_ACTION</c>: one byte, sent once on arriving in town.</summary>
/// <remarks>A working A21 session received one and answered none.</remarks>
public sealed record FarmEventActionReport(GameMessageMetadata Metadata, byte[] Body)
    : IUnansweredReport
{
    public ClientMessageType MessageType => ClientMessageType.GameFarmEventAction;
}

/// <summary>CMD 0x011B <c>PI</c>: eight bytes, and <b>no reference frame behind it</b>.</summary>
/// <remarks>
/// Sent once as a room finished loading. The captured A21 session never sent it, so "the reference
/// answers nothing" cannot be said here -- only that nothing in this server knows what it is.
/// Unlike the durability request, this report still has no modeled behavior.
/// </remarks>
public sealed record PiReport(GameMessageMetadata Metadata, byte[] Body) : IUnansweredReport
{
    public ClientMessageType MessageType => ClientMessageType.GamePi;
}

/// <summary>CMD 0x039D <c>REQ_IGA_VER</c>: an in-game-advertising version check.</summary>
/// <remarks>
/// Sixteen bytes, sent once while standing in town. <b>No reference frame behind it</b> -- neither
/// captured A21 session sent it. The same caveat <see cref="PiReport"/> and
/// durability once carried: nothing here knows what it is, and the silence is
/// this server having no advertising service rather than the reference declining to answer.
/// </remarks>
public sealed record RequestIgaVersionReport(GameMessageMetadata Metadata, byte[] Body)
    : IUnansweredReport
{
    public ClientMessageType MessageType => ClientMessageType.GameRequestIgaVersion;
}

/// <summary>
/// CMD 0x02EF <c>FPS_PERFORMANCE_STATISTIC</c>: 424 bytes of frame-rate figures.
/// </summary>
/// <remarks>
/// One of the five reports the client empties out after a dungeon on its way to leaving. Two of
/// those five were already answered-by-silence here under other names -- <c>CMD 0x00FA</c> as the
/// session metrics and <c>CMD 0x02B3</c> among the map-entry reports -- and the reference server
/// registers a handler for none of the five: they reach its dispatcher and fall off the end.
/// <para>
/// This one, <see cref="FrameLagPerDungeonReport"/> and <see cref="LagStatisticsReport"/> were
/// found by how they went missing. This server never saw two of them at all: it threw on
/// <c>CMD 0x00C2</c> earlier in the same burst and took the connection with it, so everything the
/// client said afterwards -- these, and the request to leave the game -- went nowhere. See
/// <c>protocol/patterns/data/traces/a21-exit-and-return-20260827.md</c>.
/// </para>
/// </remarks>
public sealed record FpsPerformanceStatisticReport(GameMessageMetadata Metadata, byte[] Body)
    : IUnansweredReport
{
    public ClientMessageType MessageType => ClientMessageType.GameFpsPerformanceStatistic;
}

/// <summary>CMD 0x045C <c>FRAMELAG_PER_DUNGEON</c>: 21 bytes, one per dungeon left.</summary>
/// <inheritdoc cref="FpsPerformanceStatisticReport" path="/remarks"/>
public sealed record FrameLagPerDungeonReport(GameMessageMetadata Metadata, byte[] Body)
    : IUnansweredReport
{
    public ClientMessageType MessageType => ClientMessageType.GameFrameLagPerDungeon;
}

/// <summary>CMD 0x0117 <c>LAG_STATISTICS</c>: 61 bytes, position samples paired with timings.</summary>
/// <inheritdoc cref="FpsPerformanceStatisticReport" path="/remarks"/>
public sealed record LagStatisticsReport(GameMessageMetadata Metadata, byte[] Body)
    : IUnansweredReport
{
    public ClientMessageType MessageType => ClientMessageType.GameLagStatistics;
}

internal sealed class FpsPerformanceStatisticDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new FpsPerformanceStatisticReport(metadata, body.ToArray());
}

internal sealed class FrameLagPerDungeonDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new FrameLagPerDungeonReport(metadata, body.ToArray());
}

internal sealed class LagStatisticsDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new LagStatisticsReport(metadata, body.ToArray());
}

internal sealed class RequestIgaVersionDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new RequestIgaVersionReport(metadata, body.ToArray());
}

internal sealed class CharacterStatisticDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new CharacterStatisticReport(metadata, body.ToArray());
}

internal sealed class RequestBlacklistDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new RequestBlacklistReport(metadata, body.ToArray());
}

internal sealed class FarmEventActionDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new FarmEventActionReport(metadata, body.ToArray());
}

internal sealed class PiDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new PiReport(metadata, body.ToArray());
}

internal sealed class UseSkillDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new UseSkillReport(metadata, body.ToArray());
}

internal sealed class SyncItemSpaceDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new SyncItemSpaceReport(metadata, body.ToArray());
}

internal sealed class OverflowInfoDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new OverflowInfoReport(metadata, body.ToArray());
}

internal sealed class LogDomainConnectDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new LogDomainConnectReport(metadata, body.ToArray());
}

internal sealed class LogConnectProcessDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new LogConnectProcessReport(metadata, body.ToArray());
}

/// <summary>
/// Consumes the family and sends nothing.
/// </summary>
/// <remarks>
/// One handler for all of them, because there is exactly one thing to do with each. It logs at
/// debug: a frame this server has decided not to answer is not news, and at information it would
/// bury the frames that are.
/// </remarks>
public sealed class UnansweredReportHandler(ILogger<UnansweredReportHandler> logger)
    : ClientMessageHandler<IUnansweredReport>
{
    protected override ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        IUnansweredReport message,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Unanswered report {ConnectionId}: {Message} body({BodyLength}B)",
            context.Connection.ConnectionId,
            message.MessageType,
            message.Body.Length);
        return ValueTask.CompletedTask;
    }
}
