using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Exit;

public sealed record AcknowledgeGameExitCommand(NetworkConnectionId ConnectionId) : ICommand<AcknowledgeGameExitResult>;

public sealed record AcknowledgeGameExitResult(bool Succeeded);

public sealed class AcknowledgeGameExitCommandHandler(ILoginSessionDomainService sessions)
    : ICommandHandler<AcknowledgeGameExitCommand, AcknowledgeGameExitResult>
{
    public ValueTask<AcknowledgeGameExitResult> HandleAsync(
        CommandExecutionContext context,
        AcknowledgeGameExitCommand command,
        CancellationToken cancellationToken)
    {
        sessions.EndSession(command.ConnectionId);
        return ValueTask.FromResult(new AcknowledgeGameExitResult(true));
    }
}
