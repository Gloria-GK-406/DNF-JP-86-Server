using Dfo.Server.Application;
using Dfo.Server.Application.Commands;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Protocol.Game.UseStackable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// That every command the server registers can actually be built.
/// </summary>
/// <remarks>
/// Command handlers are registered scoped, so a dependency nobody registered does not stop the host
/// from starting -- <c>ChannelHostStartupTests</c> boots the packaged host and would still pass. It
/// throws the first time a player triggers that command, which for an item command means the first
/// key press of a live session. Resolving each registration's invoker constructs its handler and the
/// whole chain behind it, so the gap closes at build time instead of in front of a player.
/// </remarks>
public sealed class CommandRegistrationTests
{
    [Fact]
    public void Every_registered_command_handler_resolves_from_the_production_graph()
    {
        using var provider = BuildProductionGraph();
        using var scope = provider.CreateScope();
        var registrations = provider.GetServices<CommandHandlerRegistration>().ToArray();

        Assert.NotEmpty(registrations);
        foreach (var registration in registrations)
        {
            var invoker = scope.ServiceProvider.GetService(registration.InvokerType);
            Assert.NotNull(invoker);
        }
    }

    /// <remarks>
    /// Resolving every registration proves each registered command can be built; it says nothing
    /// about a command nobody registered, because there is no registration to walk. The item
    /// handlers' own tests dispatch through a stand-in bus, so an unregistered item command would
    /// leave the whole suite green and throw at the registry on the first key press of a live
    /// session.
    /// </remarks>
    [Fact]
    public void The_item_commands_are_registered()
    {
        using var provider = BuildProductionGraph();
        var registrations = provider.GetServices<CommandHandlerRegistration>().ToArray();

        Assert.Contains(registrations, value => value.CommandType == typeof(UseStackableCommand));
        Assert.Contains(registrations, value => value.CommandType == typeof(MoveItemSpaceCommand));
    }

    /// <remarks>
    /// The registry itself refuses a command registered twice, so resolving it is the cheapest proof
    /// that the production modules together declare no duplicate.
    /// </remarks>
    [Fact]
    public void No_command_is_registered_twice()
    {
        using var provider = BuildProductionGraph();

        Assert.NotNull(provider.GetRequiredService<CommandHandlerRegistry>());
    }

    /// <remarks>
    /// The archive and the database both open lazily, so building the graph touches neither. A
    /// configuration this sparse is deliberate: anything that needed a real file here would be a
    /// dependency doing work in its constructor.
    /// </remarks>
    private static ServiceProvider BuildProductionGraph()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        new ApplicationModule().ConfigureServices(services, configuration);
        new InfrastructureModule().ConfigureServices(services, configuration);
        new PersistenceModule().ConfigureServices(services, configuration);
        new ProtocolModule().ConfigureServices(services, configuration);
        return services.BuildServiceProvider();
    }
}
