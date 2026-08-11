using Dfo.Server.Application.OnlineUsers;

namespace Dfo.Server.Application.Commands.Pipeline;

internal sealed class OnlineUserExecutionBehavior<TResult>(OnlineUserExecutionGate gate)
    : ICommandPipelineBehavior<TResult>
    where TResult : notnull
{
    public int Order => 50;

    public async ValueTask<TResult> ExecuteAsync(
        CommandExecutionContext context,
        ICommand command,
        CommandExecutionDelegate<TResult> next,
        CancellationToken cancellationToken)
    {
        if (context.OnlineUserAccessMode == UserAccessMode.Unscoped)
        {
            return await next().ConfigureAwait(false);
        }

        if (command is not IOnlineUserCommand onlineUserCommand)
        {
            throw new InvalidOperationException(
                $"Command '{context.CommandType.Name}' is registered as " +
                $"'{context.OnlineUserAccessMode}' but does not implement " +
                $"'{nameof(IOnlineUserCommand)}'.");
        }

        await using var lease = await gate.AcquireAsync(
            onlineUserCommand.OnlineUserId,
            context.OnlineUserAccessMode,
            cancellationToken).ConfigureAwait(false);
        return await next().ConfigureAwait(false);
    }
}
