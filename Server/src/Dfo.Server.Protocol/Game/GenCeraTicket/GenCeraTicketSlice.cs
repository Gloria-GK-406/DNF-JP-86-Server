using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.GenCeraTicket;

public sealed record GenCeraTicketRequest(GameMessageMetadata Metadata, int PayloadLength) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameGenCeraTicket;
}

internal sealed class GenCeraTicketMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GenCeraTicketRequest(metadata, body.Length);
}

internal sealed record GenCeraTicketUnavailable : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGenCeraTicket;
}

internal sealed class GenCeraTicketResponseEncoder : GameCmdMessageEncoder<GenCeraTicketUnavailable>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GenCeraTicketUnavailable message) =>
        OnePayload(0x0041, GameCmdEnvelope.Refuse(1));
}

public sealed class GenCeraTicketHandler(IGameMessageSender sender, ILogger<GenCeraTicketHandler> logger)
    : ClientMessageHandler<GenCeraTicketRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        GenCeraTicketRequest message, CancellationToken cancellationToken)
    {
        logger.LogInformation("CERA web ticket {ConnectionId}: unavailable, request bytes={PayloadLength}",
            context.Connection.ConnectionId, message.PayloadLength);
        // A21 consumes only the result envelope on failure. It does not display a failure itself,
        // so a separate server notice supplies feedback without issuing a fabricated web ticket.
        await sender.SendCmdAsync(context.Connection.ConnectionId, new GenCeraTicketUnavailable(), cancellationToken)
            .ConfigureAwait(false);
        await sender.SendNotiAsync(context.Connection.ConnectionId,
            new GameServerNoticeMessage("黑钻网页服务暂未开放。"), cancellationToken).ConfigureAwait(false);
    }
}
