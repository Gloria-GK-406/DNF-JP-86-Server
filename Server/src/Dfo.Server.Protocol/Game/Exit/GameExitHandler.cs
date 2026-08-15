using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Exit;

public sealed class GameExitHandler(
    IGameMessageSender sender,
    ICommandBus commandBus) : ClientMessageHandler<GameExitRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        GameExitRequest message,
        CancellationToken cancellationToken)
    {
        var result = await commandBus.SendAsync(
            new AcknowledgeGameExitCommand(), cancellationToken).ConfigureAwait(false);
        await sender.SendCmdAsync(
            context.Connection.ConnectionId,
            new GameExitResponse(result.Succeeded),
            cancellationToken).ConfigureAwait(false);
    }
}
