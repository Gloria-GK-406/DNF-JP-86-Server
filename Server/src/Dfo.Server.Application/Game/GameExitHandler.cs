using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Game;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Game;

public sealed class GameExitHandler(
    INetworkMessageSender sender,
    ICommandBus commandBus) : ClientMessageHandler<GameExitRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        GameExitRequest message,
        CancellationToken cancellationToken)
    {
        var result = await commandBus.SendAsync(
            new AcknowledgeGameExitCommand(), cancellationToken).ConfigureAwait(false);
        await sender.SendAsync(
            context.Connection.ConnectionId,
            new GameExitResponse(result.Succeeded),
            cancellationToken).ConfigureAwait(false);
    }
}
