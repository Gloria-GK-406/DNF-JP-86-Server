using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;

namespace Dfo.Server.Protocol.Game.BuyItem;

public sealed class BuyItemHandler(ICommandBus commands) : ClientMessageHandler<BuyItemRequest>, IProtocolCommandMessageHandler<GameBuyItemCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, BuyItemRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameBuyItemCommand(context, message), cancellationToken).ConfigureAwait(false);
}
