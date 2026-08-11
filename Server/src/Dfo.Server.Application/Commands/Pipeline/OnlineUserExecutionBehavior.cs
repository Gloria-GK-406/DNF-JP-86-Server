using Dfo.Server.Application.OnlineUsers;

namespace Dfo.Server.Application.Commands.Pipeline;

internal sealed class OnlineUserExecutionBehavior(OnlineUserExecutionGate gate)
    : ICommandPipelineBehavior
{
    public int Order => 50;

    public async ValueTask<object?> ExecuteAsync(
        CommandExecutionContext context,
        ICommand command,
        CommandExecutionDelegate next,
        CancellationToken cancellationToken)
    {
        if (context.OnlineUserAccessMode == UserAccessMode.Unscoped)
        {
            return await next().ConfigureAwait(false);
        }

        if (command is not IOnlineUserCommand onlineUserCommand)
        {
            throw new InvalidOperationException(
                $"Command '{context.CommandType}' is registered as " +
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
