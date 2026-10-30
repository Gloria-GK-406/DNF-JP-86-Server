using Dfo.Server.Application.Dispatching;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;
using Dfo.Server.Application.Commands;

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
        OnePayload(message, GameCmdEnvelope.Refuse(1));
}

public sealed record GameGenCeraTicketCommand(NetworkDispatchContext Dispatch, GenCeraTicketRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameGenCeraTicketCommandHandler(IGameCommandOutbox outbox, ILogger<GameGenCeraTicketCommandHandler> logger)
    : ICommandHandler<GameGenCeraTicketCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameGenCeraTicketCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        logger.LogInformation("CERA web ticket {ConnectionId}: unavailable, request bytes={PayloadLength}",
            context.Connection.ConnectionId, message.PayloadLength);
        // A21 consumes only the result envelope on failure. It does not display a failure itself,
        // so a separate server notice supplies feedback without issuing a fabricated web ticket.
        await outbox.RegisterCmdAsync(context.Connection.ConnectionId, new GenCeraTicketUnavailable(), cancellationToken)
            .ConfigureAwait(false);
        await outbox.RegisterNotiAsync(context.Connection.ConnectionId,
            new GameServerNoticeMessage("黑钻权益由服务器统一设置，无需购买。"), cancellationToken).ConfigureAwait(false);
        return false;
    }
}

public sealed class GenCeraTicketHandler(ICommandBus commands) : ClientMessageHandler<GenCeraTicketRequest>, IProtocolCommandMessageHandler<GameGenCeraTicketCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GenCeraTicketRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGenCeraTicketCommand(context, message), ct).ConfigureAwait(false);
}
