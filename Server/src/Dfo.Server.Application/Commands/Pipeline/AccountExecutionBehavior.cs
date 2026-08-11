using Dfo.Server.Application.OnlineUsers;

namespace Dfo.Server.Application.Commands.Pipeline;

internal sealed class AccountExecutionBehavior<TResult>(AccountExecutionGate gate)
    : ICommandPipelineBehavior<TResult>
    where TResult : notnull
{
    public int Order => 55;

    public async ValueTask<TResult> ExecuteAsync(
        CommandExecutionContext context,
        ICommand command,
        CommandExecutionDelegate<TResult> next,
        CancellationToken cancellationToken)
    {
        if (context.AccountAccessMode == UserAccessMode.Unscoped)
        {
            return await next().ConfigureAwait(false);
        }

        if (command is not IAccountCommand accountCommand)
        {
            throw new InvalidOperationException(
                $"Command '{context.CommandType.Name}' is registered as account " +
                $"'{context.AccountAccessMode}' but does not implement " +
                $"'{nameof(IAccountCommand)}'.");
        }

        await using var lease = await gate.AcquireAsync(
            accountCommand.AccountId,
            context.AccountAccessMode,
            cancellationToken).ConfigureAwait(false);
        return await next().ConfigureAwait(false);
    }
}
