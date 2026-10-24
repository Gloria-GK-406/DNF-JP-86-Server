using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Forging;

public sealed class ForgingHandler(ICommandBus commands) : ClientMessageHandler<ForgingRequest>, IProtocolCommandMessageHandler<GameForgingCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ForgingRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameForgingCommand(context, message), ct).ConfigureAwait(false);
}
