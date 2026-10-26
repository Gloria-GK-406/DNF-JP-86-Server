using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
namespace Dfo.Server.Protocol.Game.Exit;
public sealed class GameExitHandler(ICommandBus commands) : ClientMessageHandler<GameExitRequest>, IProtocolCommandMessageHandler<GameExitCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GameExitRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameExitCommand(context, message), ct).ConfigureAwait(false);
}
public sealed record GameExitCommand(NetworkDispatchContext Dispatch, GameExitRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameExitCommandHandler(IGameCommandOutbox outbox) : ICommandHandler<GameExitCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameExitCommand command, CancellationToken ct)
    {
        // EXIT also precedes channel switching and preserves the reusable login credential.
        await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId, new GameExitResponse(true), ct).ConfigureAwait(false);
        return true;
    }
}
