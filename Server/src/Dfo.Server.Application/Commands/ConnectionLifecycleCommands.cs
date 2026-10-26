using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Commands;

public interface INetworkConnectionLifecycleHandler
{
    ValueTask ConnectedAsync(CommandExecutionContext context, NetworkConnectionContext connection, CancellationToken token);
    ValueTask DisconnectedAsync(CommandExecutionContext context, NetworkConnectionContext connection, CancellationToken token);
}
public sealed record ConnectedLifecycleCommand(NetworkConnectionContext Connection) : ICommand<bool>;
public sealed record DisconnectedLifecycleCommand(NetworkConnectionContext Connection) : ICommand<bool>;
public sealed record CloseLifecycleDepthRootCommand(GameplayCallChain Refused) : ICommand<bool>;
public interface ICommandConnectionLifecycle
{
    ValueTask<bool> ConnectedAsync(CommandExecutionContext context, NetworkConnectionContext connection, CancellationToken token);
    ValueTask<bool> DisconnectedAsync(CommandExecutionContext context, NetworkConnectionContext connection, CancellationToken token);
    bool CloseDepthRoot(CommandExecutionContext context, GameplayCallChain refused);
}
internal sealed class CommandConnectionLifecycle(IEnumerable<INetworkConnectionLifecycleHandler> handlers, GameplayDepthGuard guard)
    : ICommandConnectionLifecycle
{
    private readonly INetworkConnectionLifecycleHandler[] handlers = [.. handlers];
    private static void Demand(CommandExecutionContext context, Type command)
    {
        var request = context.DemandRequest();
        if (context.CommandType != command || request is not CommandRequestContext.TrustedInternal { Origin.Kind: GameplayCallSource.Lifecycle })
            throw new InvalidOperationException("Connection lifecycle dispatch requires its exact internal command.");
    }
    public async ValueTask<bool> ConnectedAsync(CommandExecutionContext context, NetworkConnectionContext connection, CancellationToken token)
    {
        Demand(context, typeof(ConnectedLifecycleCommand));
        foreach (var handler in handlers) await handler.ConnectedAsync(context, connection, token).ConfigureAwait(false);
        return true;
    }
    public async ValueTask<bool> DisconnectedAsync(CommandExecutionContext context, NetworkConnectionContext connection, CancellationToken token)
    {
        Demand(context, typeof(DisconnectedLifecycleCommand));
        foreach (var handler in handlers) await handler.DisconnectedAsync(context, connection, token).ConfigureAwait(false);
        return true;
    }
    public bool CloseDepthRoot(CommandExecutionContext context, GameplayCallChain refused)
    {
        Demand(context, typeof(CloseLifecycleDepthRootCommand));
        guard.CloseRootConnection(refused);
        return true;
    }
}
internal sealed class ConnectedLifecycleCommandHandler(ICommandConnectionLifecycle lifecycle) : ICommandHandler<ConnectedLifecycleCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, ConnectedLifecycleCommand command, CancellationToken token) =>
        lifecycle.ConnectedAsync(context, command.Connection, token);
}
internal sealed class DisconnectedLifecycleCommandHandler(ICommandConnectionLifecycle lifecycle) : ICommandHandler<DisconnectedLifecycleCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, DisconnectedLifecycleCommand command, CancellationToken token) =>
        lifecycle.DisconnectedAsync(context, command.Connection, token);
}
internal sealed class CloseLifecycleDepthRootCommandHandler(ICommandConnectionLifecycle lifecycle) : ICommandHandler<CloseLifecycleDepthRootCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, CloseLifecycleDepthRootCommand command, CancellationToken token) =>
        ValueTask.FromResult(lifecycle.CloseDepthRoot(context, command.Refused));
}
