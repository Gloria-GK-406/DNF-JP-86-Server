namespace Dfo.Server.Application.Commands.Game;

public sealed record AcknowledgeGameExitCommand : ICommand<AcknowledgeGameExitResult>
{
    public ApplicationCommandType CommandType => ApplicationCommandType.AcknowledgeGameExit;
}

public sealed record AcknowledgeGameExitResult(bool Succeeded);

public sealed class AcknowledgeGameExitCommandHandler
    : CommandHandler<AcknowledgeGameExitCommand, AcknowledgeGameExitResult>
{
    protected override ValueTask<AcknowledgeGameExitResult> HandleCommandAsync(
        CommandExecutionContext context,
        AcknowledgeGameExitCommand command,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new AcknowledgeGameExitResult(true));
}
