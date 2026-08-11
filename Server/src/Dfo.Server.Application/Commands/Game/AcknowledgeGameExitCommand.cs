namespace Dfo.Server.Application.Commands.Game;

public sealed record AcknowledgeGameExitCommand : ICommand<AcknowledgeGameExitResult>;

public sealed record AcknowledgeGameExitResult(bool Succeeded);

public sealed class AcknowledgeGameExitCommandHandler
    : ICommandHandler<AcknowledgeGameExitCommand, AcknowledgeGameExitResult>
{
    public ValueTask<AcknowledgeGameExitResult> HandleAsync(
        CommandExecutionContext context,
        AcknowledgeGameExitCommand command,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new AcknowledgeGameExitResult(true));
}
