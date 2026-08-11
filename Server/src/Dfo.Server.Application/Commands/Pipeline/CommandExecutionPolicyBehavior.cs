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
            CommandExecutionKind.Transactional => throw new InvalidOperationException(
                $"Command '{context.CommandType.Name}' requires {nameof(CommandExecutionKind.Transactional)} " +
                "execution, but no unit-of-work policy has been registered."),
            _ => throw new InvalidOperationException(
                $"Unsupported command execution kind '{context.ExecutionKind}'."),
        };
}
