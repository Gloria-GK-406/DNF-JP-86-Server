using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;

namespace Dfo.Server.Protocol.Game.CeraShopPurchase;

public sealed class CeraShopPurchaseHandler(ICommandBus commands) : ClientMessageHandler<CeraShopPurchaseRequest>,
    IProtocolCommandMessageHandler<GameCeraShopPurchaseCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        CeraShopPurchaseRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameCeraShopPurchaseCommand(context, message), cancellationToken).ConfigureAwait(false);
}
