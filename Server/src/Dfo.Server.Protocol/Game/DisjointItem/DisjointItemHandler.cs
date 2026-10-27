using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.DisjointItem;

public sealed class DisjointItemHandler(ICommandBus commands) : ClientMessageHandler<DisjointItemRequest>, IProtocolCommandMessageHandler<GameDisjointItemCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, DisjointItemRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameDisjointItemCommand(context, message), ct).ConfigureAwait(false);
}
