using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Channel.Connect;

namespace Dfo.Server.Application.Tests.Commands;

public sealed class CommandHandlerArchitectureTests
{
    [Fact]
    public void ProductionCommandHandlers_DoNotDependOnCommandBus()
    {
        var violations = typeof(ChannelConnectHandler).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && type.GetInterfaces().Any(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))
            .Where(type => type.GetConstructors().Any(constructor =>
                constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(ICommandBus))))
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(violations);
    }
}
