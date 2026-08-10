using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Channel;

namespace Dfo.Server.Application.Tests.Commands.Channel;

public sealed class EstablishChannelConnectionCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_ReturnsTheDateBasedEncryptionKey()
    {
        var handler = new EstablishChannelConnectionCommandHandler(
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(8))));
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

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.CreateCustomTimeZone("Test/UTC+8", TimeSpan.FromHours(8), "Test/UTC+8", "Test/UTC+8");
    }
}
