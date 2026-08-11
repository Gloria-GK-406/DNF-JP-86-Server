using Dfo.Server.Application.OnlineUsers;

namespace Dfo.Server.Application.Commands.Pipeline;

internal sealed class AccountExecutionBehavior(AccountExecutionGate gate)
    : ICommandPipelineBehavior
{
    public int Order => 55;

    public async ValueTask<object> ExecuteAsync(
        CommandExecutionContext context,
        ICommand command,
        CommandExecutionDelegate next,
        CancellationToken cancellationToken)
    {
        if (context.AccountAccessMode == UserAccessMode.Unscoped)
        {
            return await next().ConfigureAwait(false);
        }

        if (command is not IAccountCommand accountCommand)
        {
            throw new InvalidOperationException(
                $"Command '{context.CommandType}' is registered as account " +
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
