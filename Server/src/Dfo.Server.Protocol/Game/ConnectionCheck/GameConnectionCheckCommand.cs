using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Protocol.Game.ConnectionCheck;

public sealed record GameConnectionCheckCommand(NetworkDispatchContext Dispatch, ConnectionCheckRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameConnectionCheckCommandHandler(IGameCommandOutbox sender) : ICommandHandler<GameConnectionCheckCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameConnectionCheckCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        await sender.RegisterCmdAsync(
            context.Connection.ConnectionId,
            new ConnectionCheckResponse(),
            cancellationToken).ConfigureAwait(false);
        return true;
    }
}
