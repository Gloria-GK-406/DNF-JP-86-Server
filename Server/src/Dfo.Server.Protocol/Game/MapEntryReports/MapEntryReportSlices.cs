using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.MapEntryReports;

/// <summary>
/// The fire-and-forget reports the client sends once the entry batch has been accepted and it
/// begins loading into a town.
/// </summary>
/// <remarks>
/// Grouped as a family rather than split into five folders. They share a trigger (the map-entry
/// handshake), none carries domain semantics, none is answered -- the client registers no CMD
/// receive handler for any of them -- and most have a body whose fields are still unknown. The
/// protocols in that same handshake that carry real state -- ENTER_SELECT_DUNGEON, SELECT_DUNGEON
/// and SET_USER_POSITION, whose coordinates the server has to keep -- live on their own.
///
/// Every layout below is pinned by a capture taken 2026-08-17; see
/// protocol/game/client-to-server/ for the per-protocol documents and samples.
///
/// The shared handler is typed on this marker rather than on
/// <see cref="IClientNetworkMessage"/> so the dispatcher's <c>CanHandle</c> guard still rejects a
/// message routed here by mistake; typed on the base interface it would accept anything.
/// </remarks>
public interface IMapEntryReport : IClientNetworkMessage;

internal static class MapEntryReportFamily
{
    /// <summary>Rejects a body that cannot be this protocol, naming the protocol in the message.</summary>
    public static void RequireExactly(string name, ReadOnlySpan<byte> body, int expected)
    {
        if (body.Length != expected)
        {
            throw new InvalidDataException(
                $"{name} requires exactly {expected} body bytes; received '{body.Length}'.");
        }
    }

    public static void RequireAtLeast(string name, ReadOnlySpan<byte> body, int minimum)
    {
        if (body.Length < minimum)
        {
            throw new InvalidDataException(
                $"{name} requires at least {minimum} body bytes; received '{body.Length}'.");
        }
    }
}

/// <summary>
/// CMD 0x0002 <c>SET_UDP_IP_PORT</c>. Body is 31 bytes in the only capture and its structure is
/// unknown -- the census could not find a builder for it at all.
/// </summary>
/// <remarks>
/// Its census row reads "response-handler-only (builder not found in .text; likely obfuscated
/// region)", yet the client demonstrably sends it. That is a direct confirmation of the
/// obfuscated-region blind spot the index documents, not a contradiction: the builder exists, it
/// just is not in the scannable text section.
/// </remarks>
public sealed record SetUdpIpPortReport(
    GameMessageMetadata Metadata,
    byte[] Body) : IMapEntryReport
{
    public ClientMessageType MessageType => ClientMessageType.GameSetUdpIpPort;
}

/// <summary>CMD 0x01F8 <c>INFORM_NOTICE_2ND</c>: one <c>u32</c>, 16 in the capture.</summary>
public sealed record InformNoticeSecondReport(
    GameMessageMetadata Metadata,
    uint Value) : IMapEntryReport
{
    public ClientMessageType MessageType => ClientMessageType.GameInformNoticeSecond;
}

/// <summary>CMD 0x003F <c>CERA</c>: an empty body. The type itself is the whole message.</summary>
public sealed record CeraQueryReport(GameMessageMetadata Metadata) : IMapEntryReport
{
    public ClientMessageType MessageType => ClientMessageType.GameCeraQuery;
}

/// <summary>
/// CMD 0x0252 <c>SECURITY_STATUS</c>: eight bytes in the capture (<c>0F0000007F000001</c>).
/// </summary>
/// <remarks>
/// The static census reports "at least 12" for this type. The capture says eight, so the census
/// figure is wrong here -- a walked builder path that counted fields the real send does not write.
/// The sample wins: a decoder built to the census would reject every real frame.
/// </remarks>
public sealed record SecurityStatusReport(
    GameMessageMetadata Metadata,
    byte[] Body) : IMapEntryReport
{
    public ClientMessageType MessageType => ClientMessageType.GameSecurityStatus;
}

/// <summary>CMD 0x02B3 <c>STATICS_RUNTIME_TING</c>: a single byte, 0 in the capture.</summary>
public sealed record RuntimeTingStatisticReport(
    GameMessageMetadata Metadata,
    byte Value) : IMapEntryReport
{
    public ClientMessageType MessageType => ClientMessageType.GameRuntimeTingStatistic;
}

internal sealed class SetUdpIpPortDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new SetUdpIpPortReport(metadata, body.ToArray());
}

internal sealed class InformNoticeSecondDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        MapEntryReportFamily.RequireExactly("INFORM_NOTICE_2ND", body, sizeof(uint));
        return new InformNoticeSecondReport(
            metadata, BinaryPrimitives.ReadUInt32LittleEndian(body));
    }
}

internal sealed class CeraQueryDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        MapEntryReportFamily.RequireExactly("CERA", body, 0);
        return new CeraQueryReport(metadata);
    }
}

internal sealed class SecurityStatusDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        MapEntryReportFamily.RequireAtLeast("SECURITY_STATUS", body, 8);
        return new SecurityStatusReport(metadata, body.ToArray());
    }
}

internal sealed class RuntimeTingStatisticDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        MapEntryReportFamily.RequireExactly("STATICS_RUNTIME_TING", body, 1);
        return new RuntimeTingStatisticReport(metadata, body[0]);
    }
}

/// <summary>
/// Consumes the map-entry reports and logs them. One handler for the family: none of the ones it
/// still owns has a response, a domain effect or a command, so several near-identical classes
/// would carry no information that this one does not.
/// </summary>
/// <remarks>
/// SECURITY_STATUS left the family on 2026-08-25. It is decoded here still -- the body is opaque
/// either way -- but A21 answers it, so it is handled by <see cref="SecurityStatusHandler"/>
/// instead. Keeping it here would have meant giving the whole family a sender for one member's
/// sake, and the family's point is that its members have no reply.
/// </remarks>
public sealed class MapEntryReportHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ILogger<MapEntryReportHandler> logger)
    : ClientMessageHandler<IMapEntryReport>
{
    protected override ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        IMapEntryReport message,
        CancellationToken cancellationToken)
    {
        var attributed = onlineUsers.TryGetByConnection(
            context.Connection.ConnectionId, out var onlineUser) && onlineUser is not null;
        var detail = message switch
        {
            SetUdpIpPortReport report => $"SET_UDP_IP_PORT {Convert.ToHexString(report.Body)}",
            InformNoticeSecondReport report => $"INFORM_NOTICE_2ND value={report.Value}",
            CeraQueryReport => "CERA (empty body)",
            RuntimeTingStatisticReport report => $"STATICS_RUNTIME_TING value={report.Value}",
            _ => message.GetType().Name,
        };

        logger.LogInformation(
            "Map entry report {ConnectionId} {Attribution}: {Detail}",
            context.Connection.ConnectionId,
            attributed ? $"account={onlineUser!.AccountId}" : "unattributed",
            detail);

        return ValueTask.CompletedTask;
    }
}

/// <summary>Answers CMD 0x0252 <c>SECURITY_STATUS</c> with the six zero bytes A21 expects.</summary>
/// <remarks>
/// The client sends this while a dungeon loads and the reference server answers every one of them
/// with a fixed six-byte zero body -- no envelope acceptance byte in front, which is why this does
/// not go through <c>GameCmdEnvelope</c>: the observed body starts with a zero, not a one.
///
/// Nothing is read out of the request. Its body is opaque here and upstream alike; what the client
/// needs is the answer, not an interpretation of the question.
/// </remarks>
public sealed record SecurityStatusAcknowledgement : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSecurityStatusAcknowledgement;
}

internal sealed class SecurityStatusAcknowledgementEncoder
    : GameCmdMessageEncoder<SecurityStatusAcknowledgement>
{
    private static readonly byte[] ObservedBody = new byte[6];

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        SecurityStatusAcknowledgement message) =>
        OnePayload(GameProtocol.SecurityStatusType, ObservedBody);
}

/// <summary>Logs the report and answers it.</summary>
public sealed class SecurityStatusHandler(
    IGameMessageSender sender,
    ILogger<SecurityStatusHandler> logger)
    : ClientMessageHandler<SecurityStatusReport>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        SecurityStatusReport message,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Security status {ConnectionId}: {Body}",
            context.Connection.ConnectionId,
            Convert.ToHexString(message.Body));
        await sender.SendCmdAsync(
            context.Connection.ConnectionId,
            new SecurityStatusAcknowledgement(),
            cancellationToken).ConfigureAwait(false);
    }
}
