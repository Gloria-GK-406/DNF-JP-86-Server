using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;

namespace Dfo.Server.Protocol.Game.SellItem;

public sealed class SellItemHandler(ICommandBus commands) : ClientMessageHandler<SellItemRequest>, IProtocolCommandMessageHandler<GameSellItemCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SellItemRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameSellItemCommand(context, message), ct).ConfigureAwait(false);
}
