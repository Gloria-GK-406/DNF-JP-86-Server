using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;

namespace Dfo.Server.Protocol.Game.DeleteItem;

public sealed class DeleteItemHandler(ICommandBus commands) : ClientMessageHandler<DeleteItemRequest>, IProtocolCommandMessageHandler<GameDeleteItemCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        DeleteItemRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameDeleteItemCommand(context, message), cancellationToken).ConfigureAwait(false);
}
