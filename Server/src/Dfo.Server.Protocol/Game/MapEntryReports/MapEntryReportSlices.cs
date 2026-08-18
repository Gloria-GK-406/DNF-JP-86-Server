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
/// Consumes the five map-entry reports and logs them. One handler for the family: none of them has
/// a response, a domain effect or a command, so five near-identical classes would carry no
/// information that this one does not.
/// </summary>
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
            SecurityStatusReport report => $"SECURITY_STATUS {Convert.ToHexString(report.Body)}",
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
