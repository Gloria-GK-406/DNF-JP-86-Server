using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.AuctionService;

/// <summary>
/// NOTI 0x00B7, the legacy <c>AUCTION_NOTIFY_AUCTION_SERVICE</c>: is an auction service open?
/// </summary>
/// <remarks>
/// Outbound only, pushed during login. The reference server sends it twice, once per service type,
/// both reported open -- two separate frames rather than one carrying a list, so this message
/// models a single service and the caller sends both.
///
/// There is no auction implementation behind this. It is sent because the reference server sends
/// it at login and the roster screen would not open character creation without the full login
/// batch; which member of that batch the client actually requires has not been isolated.
/// </remarks>
public sealed record AuctionServiceStateNotice(byte ServiceType, bool Open) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameAuctionServiceState;
}

internal sealed class AuctionServiceStateNoticeEncoder
    : GameNotiMessageEncoder<AuctionServiceStateNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        AuctionServiceStateNotice message) =>
        OnePayload(message,
            new[] { message.ServiceType, message.Open ? (byte)0x01 : (byte)0x00 });
}
