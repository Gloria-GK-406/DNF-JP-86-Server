using Dfo.Server.Application;
using Dfo.Server.Abstractions.Hosting;
using Dfo.Server.Application.Commands;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.CreateCharacter;
using Dfo.Server.Protocol.Game.DeleteCharacter;
using Dfo.Server.Protocol.Game.Login;
using Dfo.Server.Protocol.Game.ReturnToSelection;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.TaggedIdReport;
using Dfo.Server.Protocol.Game.UseStackable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

    [Fact]
    public void All_four_Channel_protocols_are_bound_to_whole_commands()
    {
        using var provider = BuildProductionGraph();
        var registrations = provider.GetServices<CommandHandlerRegistration>().ToArray();
        (Type Command, Dfo.Server.Data.Networking.ClientMessageType Message)[] channels =
        [
            (typeof(Dfo.Server.Protocol.Channel.Connect.ChannelConnectCommand), Dfo.Server.Data.Networking.ClientMessageType.ChannelConnect),
            (typeof(Dfo.Server.Protocol.Channel.ScriptVersion.ChannelScriptVersionCommand), Dfo.Server.Data.Networking.ClientMessageType.ChannelScriptVersion),
            (typeof(Dfo.Server.Protocol.Channel.Script.ChannelScriptCommand), Dfo.Server.Data.Networking.ClientMessageType.ChannelScript),
            (typeof(Dfo.Server.Protocol.Channel.Info.ChannelInfoCommand), Dfo.Server.Data.Networking.ClientMessageType.ChannelInfo),
        ];
        foreach (var (command, message) in channels)
        {
            var registration = Assert.Single(registrations, row => row.CommandType == command);
            Assert.Equal(CommandRouteKind.Protocol, registration.Route);
            Assert.Equal(message, Assert.Single(registration.ProtocolMessages!).MessageType);
        }
        Assert.NotNull(provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>());
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

    /// <summary>
    /// That the commands which can reach a persistent write are the transactional kind.
    /// </summary>
    /// <remarks>
    /// Only <c>Transactional</c> makes <c>UnitOfWorkBehavior</c> open a domain transaction, and
    /// without one a handler's writes each settle on their own with nobody able to take them back.
    /// The repository convention names these ten for exactly that reason.
    /// <para>
    /// This is pinned rather than left to the convention because entry save now depends on it for
    /// player data. It takes away every pile the character still holds and writes them again inside
    /// the same save, so the rows that used to be safe to lose -- the ones a lease had let go of --
    /// are no longer the only ones in the delete. Change the kind on <c>Prepare</c> and that pair
    /// stops being one change: the delete settles, the rewrite does not, and the piles are gone with
    /// nothing said. Before this assertion existed that edit left the whole suite green.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_commands_that_reach_a_persistent_write_are_transactional()
    {
        using var provider = BuildProductionGraph();
        var registrations = provider.GetServices<CommandHandlerRegistration>()
            .ToDictionary(value => value.CommandType, value => value.ExecutionKind);

        Type[] reachPersistentWrites =
        [
            typeof(RedeemGameLoginCommand),
            typeof(CreateCharacterCommand),
            typeof(DeleteCharacterCommand),
            typeof(PrepareSelectedCharacterSessionCommand),
            typeof(CleanupExpiredCharacterItemsCommand),
            typeof(PrepareSelectedCharacterStateCommand),
            typeof(ApplySelectedCharacterDailyRefillCommand),
            typeof(SynchronizeSelectedCharacterQuestsCommand),
            typeof(ReviveSelectedCharacterPetCommand),
            typeof(MoveItemSpaceCommand),
            typeof(UseStackableCommand),
            typeof(GrantTutorialRewardCommand),
        ];

        foreach (var command in reachPersistentWrites)
        {
            Assert.True(
                registrations.TryGetValue(command, out var kind),
                $"{command.Name} is not registered at all.");
            Assert.Equal(CommandExecutionKind.Transactional, kind);
        }
    }

    [Fact]
    public void GameTicketRedemptionIsTransactional()
    {
        using var provider = BuildProductionGraph();
        var registration = Assert.Single(
            provider.GetServices<CommandHandlerRegistration>(),
            value => value.CommandType == typeof(RedeemGameLoginCommand));

        Assert.Equal(CommandExecutionKind.Transactional, registration.ExecutionKind);
    }

    [Fact]
    public void Return_saves_in_a_transaction_and_cleans_up_outside_it()
    {
        using var provider = BuildProductionGraph();
        var registrations = provider.GetServices<CommandHandlerRegistration>()
            .ToDictionary(value => value.CommandType);
        Assert.Equal(CommandExecutionKind.Transactional,
            registrations[typeof(PrepareReturnToSelectionCommand)].ExecutionKind);
        Assert.Equal(CommandExecutionKind.Workflow,
            registrations[typeof(CompleteReturnToSelectionCommand)].ExecutionKind);
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
    /// <para>
    /// Logging is registered because every real host has it -- <c>Host.CreateApplicationBuilder</c>
    /// adds it before any module runs. Leaving it out would make this graph unfaithful in the one
    /// direction that matters here: a service that logs would fail to resolve in a test and resolve
    /// perfectly in production.
    /// </para>
    /// </remarks>
    private static ServiceProvider BuildProductionGraph()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // GameServer provides this host fact; the module-only fixture has no game listener.
        services.AddSingleton<IGameServerReadiness, DisabledGameReadiness>();
        var configuration = new ConfigurationBuilder().Build();
        new ApplicationModule().ConfigureServices(services, configuration);
        new InfrastructureModule().ConfigureServices(services, configuration);
        new PersistenceModule().ConfigureServices(services, configuration);
        new Dfo.Server.Networking.NetworkingModule().ConfigureServices(services, configuration);
        new ProtocolModule().ConfigureServices(services, configuration);
        return services.BuildServiceProvider();
    }

    private sealed class DisabledGameReadiness : IGameServerReadiness
    {
        public bool NetworkEnabled => false;
        public bool IsReady => false;
    }
}
