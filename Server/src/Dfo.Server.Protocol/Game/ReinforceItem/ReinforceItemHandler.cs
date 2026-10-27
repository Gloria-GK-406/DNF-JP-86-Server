using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ReinforceItem;

public sealed class ReinforceItemHandler(ICommandBus commands) : ClientMessageHandler<ReinforceItemRequest>, IProtocolCommandMessageHandler<GameReinforceItemCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ReinforceItemRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameReinforceItemCommand(context, message), ct).ConfigureAwait(false);
}
