using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game.Exit;

namespace Dfo.Server.Application.Tests.Commands.Game;

public sealed class AcknowledgeGameExitCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_ReturnsASuccessAcknowledgement()
    {
        var handler = new AcknowledgeGameExitCommandHandler(new Dfo.Server.Application.Authentication.LoginSessionRegistry(new Dfo.Server.Application.Transactions.DomainTransactionScope(), TimeProvider.System, new Dfo.Server.Application.Authentication.LoginSessionOptions()));
        var command = new AcknowledgeGameExitCommand(new Dfo.Server.Data.Networking.NetworkConnectionId("exit"));
        var context = new CommandExecutionContext(
            Guid.NewGuid(),
            command.GetType(),
            handler.GetType(),
            CommandExecutionKind.Direct);

        var result = await handler.HandleAsync(context, command, CancellationToken.None);

        var typed = Assert.IsType<AcknowledgeGameExitResult>(result);
        Assert.True(typed.Succeeded);
    }
}
