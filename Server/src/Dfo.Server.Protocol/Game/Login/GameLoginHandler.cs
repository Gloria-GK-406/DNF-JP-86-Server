using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
namespace Dfo.Server.Protocol.Game.Login;

public sealed class GameLoginHandler(ICommandBus commands) : ClientMessageHandler<GameLoginRequest>, IProtocolCommandMessageHandler<GameLoginCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GameLoginRequest message,
        CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameLoginCommand(context, message), cancellationToken).ConfigureAwait(false);
}
