using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Channel;

namespace Dfo.Server.Application.Tests.Commands.Channel;

public sealed class EstablishChannelConnectionCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_ReturnsTheDateBasedEncryptionKey()
    {
        var handler = new EstablishChannelConnectionCommandHandler(
            new StubEncryptionKeyProvider("20260810000006"));
        var command = new EstablishChannelConnectionCommand();
        var context = new CommandExecutionContext(
            Guid.NewGuid(),
            command.CommandType,
            typeof(EstablishChannelConnectionCommandHandler),
            CommandExecutionKind.Direct);

        var result = await handler.HandleAsync(context, command, CancellationToken.None);

        var typedResult = Assert.IsType<EstablishChannelConnectionResult>(result);
        Assert.Equal("20260810000006", typedResult.EncryptionKey);
    }

    [Fact]
    public void ProductionCommandHandlers_DoNotDependOnCommandBus()
    {
        var violations = typeof(EstablishChannelConnectionCommandHandler).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(ICommandHandler).IsAssignableFrom(type))
            .Where(type => type.GetConstructors().Any(constructor =>
                constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(ICommandBus))))
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(violations);
    }

    private sealed class StubEncryptionKeyProvider(string key) : IChannelEncryptionKeyProvider
    {
        public string GetCurrentKey() => key;
    }
}
