namespace Dfo.Server.Application.Commands.Pipeline;

internal sealed class CommandExecutionPolicyBehavior<TResult> : ICommandPipelineBehavior<TResult>
    where TResult : notnull
{
    public int Order => 100;

    public ValueTask<TResult> ExecuteAsync(
        CommandExecutionContext context,
        ICommand command,
        CommandExecutionDelegate<TResult> next,
        CancellationToken cancellationToken) => context.ExecutionKind switch
        {
            CommandExecutionKind.Direct => next(),
            CommandExecutionKind.Workflow => next(),
            // The transaction was opened further out, by the unit-of-work behaviour, so by the
            // time execution reaches here the command is already inside it.
            CommandExecutionKind.Transactional => next(),
            _ => throw new InvalidOperationException(
                $"Unsupported command execution kind '{context.ExecutionKind}'."),
        };
}
