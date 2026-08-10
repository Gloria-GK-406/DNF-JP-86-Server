namespace Dfo.Server.Application.Commands.Pipeline;

internal sealed class CommandExecutionPolicyBehavior : ICommandPipelineBehavior
{
    public int Order => 100;

    public ValueTask<object?> ExecuteAsync(
        CommandExecutionContext context,
        ICommand command,
        CommandExecutionDelegate next,
        CancellationToken cancellationToken) => context.ExecutionKind switch
        {
            CommandExecutionKind.Direct => next(),
            CommandExecutionKind.Workflow => next(),
            CommandExecutionKind.Transactional => throw new InvalidOperationException(
                $"Command '{context.CommandType}' requires {nameof(CommandExecutionKind.Transactional)} " +
                "execution, but no unit-of-work policy has been registered."),
            _ => throw new InvalidOperationException(
                $"Unsupported command execution kind '{context.ExecutionKind}'."),
        };
}
