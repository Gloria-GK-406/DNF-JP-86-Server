using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Outworld;

public sealed class OutworldHandler(ICommandBus commands) : ClientMessageHandler<OutworldRequest>, IProtocolCommandMessageHandler<GameOutworldCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, OutworldRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameOutworldCommand(context, message), ct).ConfigureAwait(false);
}
