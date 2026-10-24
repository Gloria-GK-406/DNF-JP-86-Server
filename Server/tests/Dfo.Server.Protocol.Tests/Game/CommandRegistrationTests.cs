using Dfo.Server.Protocol.Game.AccountRoster;
using Dfo.Server.Protocol.Game.CharacterName;
using Dfo.Server.Data.Networking;
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
    public void Four_openings_each_have_one_exact_direct_whole_with_only_existing_replay_permission()
    {
        using var provider = BuildProductionGraph();
        var commands = provider.GetServices<CommandHandlerRegistration>().ToArray();
        var registry = provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>();
        var expected = new[]
        {
            (ClientMessageType.GamePackageOpening, typeof(Dfo.Server.Protocol.Game.PackageOpening.PackageOpeningRequest), typeof(Dfo.Server.Protocol.Game.PackageOpening.GamePackageOpeningCommand)),
            (ClientMessageType.GameAvatarPackageOpening, typeof(Dfo.Server.Protocol.Game.AvatarPackageOpening.AvatarPackageOpeningRequest), typeof(Dfo.Server.Protocol.Game.AvatarPackageOpening.GameAvatarPackageOpeningCommand)),
            (ClientMessageType.GameMagicBoxSingleOpening, typeof(Dfo.Server.Protocol.Game.MagicBoxSingleOpening.MagicBoxSingleOpeningRequest), typeof(Dfo.Server.Protocol.Game.MagicBoxSingleOpening.GameMagicBoxSingleOpeningCommand)),
            (ClientMessageType.GameMagicBoxBatchOpening, typeof(Dfo.Server.Protocol.Game.MagicBoxBatchOpening.MagicBoxBatchOpeningRequest), typeof(Dfo.Server.Protocol.Game.MagicBoxBatchOpening.GameMagicBoxBatchOpeningCommand)),
        };
        foreach (var (logical, input, command) in expected)
        {
            var row = Assert.Single(commands, row => row.CommandType == command);
            Assert.Equal(CommandExecutionKind.Direct, row.ExecutionKind); Assert.Equal(CommandRouteKind.Protocol, row.Route);
            Assert.Equal(new[] { new ProtocolCommandMessageBinding(logical, input) }, row.ProtocolMessages);
            Assert.Equal(new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch }, row.RegistrationExits);
            Assert.False(typeof(IRequestSettlementCommand).IsAssignableFrom(command));
            Assert.True(registry.TryGet(logical, out var route)); Assert.Equal(command, route!.ProtocolCommandType);
            Assert.Equal(new[] { input }, route.AllowedMessageClrTypes);
        }
        Assert.DoesNotContain(commands, row => row.CommandType.Name is "PackageOpeningCommand" or "AvatarPackageOpeningCommand" or "MagicBoxSingleOpeningCommand" or "MagicBoxBatchOpeningCommand");
    }

    [Fact]
    public void Five_option_logicals_are_closed_to_three_wholes_with_two_one_two_exact_bindings()
    {
        using var provider = BuildProductionGraph();
        var commands = provider.GetServices<CommandHandlerRegistration>().ToArray();
        var registry = provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>();
        var expected = new[]
        {
            (typeof(Dfo.Server.Protocol.Game.MagicSeal.GameMagicSealCommand), typeof(Dfo.Server.Protocol.Game.MagicSeal.MagicSealRequest), new[] { ClientMessageType.GameUnsealRandomOption, ClientMessageType.GameChangeRandomOption }),
            (typeof(Dfo.Server.Protocol.Game.Forging.GameForgingCommand), typeof(Dfo.Server.Protocol.Game.Forging.ForgingRequest), new[] { ClientMessageType.GameSeparateUpgrade }),
            (typeof(Dfo.Server.Protocol.Game.Outworld.GameOutworldCommand), typeof(Dfo.Server.Protocol.Game.Outworld.OutworldRequest), new[] { ClientMessageType.GamePurifyItem, ClientMessageType.GameInvestAmplifyOption }),
        };
        foreach (var (command, input, logicals) in expected)
        {
            var row = Assert.Single(commands, row => row.CommandType == command);
            Assert.Equal(CommandRouteKind.Protocol, row.Route); Assert.Equal(CommandExecutionKind.Direct, row.ExecutionKind);
            Assert.Equal(logicals.Select(logical => new ProtocolCommandMessageBinding(logical, input)), row.ProtocolMessages);
            Assert.Equal(new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch }, row.RegistrationExits);
            foreach (var logical in logicals)
            {
                Assert.True(registry.TryGet(logical, out var route)); Assert.Equal(command, route!.ProtocolCommandType);
                Assert.Equal(input, route.MessageClrType); Assert.Equal(new[] { input }, route.AllowedMessageClrTypes);
            }
        }
        Assert.DoesNotContain(commands, row => row.CommandType.Name is "MagicSealCommand" or "ForgingCommand" or "OutworldCommand");
    }

    [Fact]
    public void Three_equipment_protocols_each_have_one_exact_whole_and_only_replay_permission()
    {
        using var provider = BuildProductionGraph();
        var commands = provider.GetServices<CommandHandlerRegistration>().ToArray();
        var registry = provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>();
        var expected = new[]
        {
            (ClientMessageType.GameReinforceItem, typeof(Dfo.Server.Protocol.Game.ReinforceItem.ReinforceItemRequest), typeof(Dfo.Server.Protocol.Game.ReinforceItem.GameReinforceItemCommand)),
            (ClientMessageType.GameDisjointItem, typeof(Dfo.Server.Protocol.Game.DisjointItem.DisjointItemRequest), typeof(Dfo.Server.Protocol.Game.DisjointItem.GameDisjointItemCommand)),
            (ClientMessageType.GameEnchantByBead, typeof(Dfo.Server.Protocol.Game.EnchantByBead.EnchantByBeadRequest), typeof(Dfo.Server.Protocol.Game.EnchantByBead.GameEnchantByBeadCommand)),
        };
        foreach (var (logical, input, command) in expected)
        {
            var row = Assert.Single(commands, row => row.CommandType == command);
            Assert.Equal(CommandExecutionKind.Direct, row.ExecutionKind); Assert.Equal(CommandRouteKind.Protocol, row.Route);
            Assert.Equal(new[] { new ProtocolCommandMessageBinding(logical, input) }, row.ProtocolMessages);
            Assert.Equal(new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch }, row.RegistrationExits);
            Assert.True(registry.TryGet(logical, out var route)); Assert.Equal(command, route!.ProtocolCommandType);
            Assert.Equal(new[] { input }, route.AllowedMessageClrTypes);
        }
        Assert.DoesNotContain(commands, row => row.CommandType.Name is "ReinforceItemCommand" or "NpcDisjointItemCommand" or "PortableDisjointItemCommand" or "EnchantByBeadCommand");
    }

    [Fact]
    public void Other_detail_is_bound_to_one_protocol_command_with_its_exact_request()
    {
        using var provider = BuildProductionGraph();
        var registration = Assert.Single(provider.GetServices<CommandHandlerRegistration>(),
            row => row.CommandType == typeof(GameOtherCharacterDetailCommand));
        Assert.Equal(CommandRouteKind.Protocol, registration.Route);
        Assert.Equal(CommandExecutionKind.Direct, registration.ExecutionKind);
        var binding = Assert.Single(registration.ProtocolMessages!);
        Assert.Equal(ClientMessageType.GameGetOtherCharacterDetail, binding.MessageType);
        Assert.Equal(typeof(GetOtherCharacterDetailRequest), binding.MessageClrType);
        Assert.NotNull(provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>());
    }

    [Fact]
    public void Buy_has_one_logical_route_two_exact_shapes_and_only_the_existing_replay_exit()
    {
        using var provider = BuildProductionGraph();
        var registrations = provider.GetServices<CommandHandlerRegistration>().ToArray();
        var whole = Assert.Single(registrations, row => row.CommandType == typeof(Dfo.Server.Protocol.Game.BuyItem.GameBuyItemCommand));
        Assert.Equal(CommandRouteKind.Protocol, whole.Route);
        Assert.Equal(CommandExecutionKind.Direct, whole.ExecutionKind);
        Assert.Equal(new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch }, whole.RegistrationExits);
        Assert.All(whole.ProtocolMessages!, pair => Assert.Equal(ClientMessageType.GameBuyItem, pair.MessageType));
        var family = new[] { typeof(Dfo.Server.Protocol.Game.BuyItem.BuyItemRequest), typeof(Dfo.Server.Protocol.Game.BuyItem.HousingBuyItemRequest) };
        Assert.Equal(family, whole.ProtocolMessages!.Select(pair => pair.MessageClrType));
        Assert.DoesNotContain(registrations, row => row.CommandType.FullName is "Dfo.Server.Protocol.Game.BuyItem.BuyItemCommand" or "Dfo.Server.Protocol.Game.BuyItem.HousingBuyItemCommand");
        Assert.True(provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>().TryGet(ClientMessageType.GameBuyItem, out var route));
        Assert.Equal(typeof(Dfo.Server.Protocol.Game.BuyItem.GameBuyItemCommand), route!.ProtocolCommandType);
        Assert.Equal(typeof(Dfo.Server.Protocol.Game.BuyItem.BuyItemRequest), route.MessageClrType);
        Assert.Equal(family, route.AllowedMessageClrTypes);
    }

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

        Assert.Contains(registrations, value => value.CommandType == typeof(GameUseStackableCommand));
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

        Assert.Equal(CommandExecutionKind.Direct, registrations[typeof(GameUseStackableCommand)]);
        Assert.Equal(CommandRouteKind.Protocol, provider.GetServices<CommandHandlerRegistration>().Single(x => x.CommandType == typeof(GameUseStackableCommand)).Route);
        Type[] reachPersistentWrites =
        [
            typeof(PrepareSelectedCharacterSessionCommand),
            typeof(CleanupExpiredCharacterItemsCommand),
            typeof(PrepareSelectedCharacterStateCommand),
            typeof(ApplySelectedCharacterDailyRefillCommand),
            typeof(SynchronizeSelectedCharacterQuestsCommand),
            typeof(ReviveSelectedCharacterPetCommand),
            typeof(MoveItemSpaceCommand),
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
    public void All_four_roster_protocols_use_whole_commands_and_explicit_write_segments()
    {
        using var provider = BuildProductionGraph();
        var registrations = provider.GetServices<CommandHandlerRegistration>().ToArray();
        (Type Command, ClientMessageType Message, Type Request)[] expected =
        [
            (typeof(GameAccountRosterCommand), ClientMessageType.GameGetAccountCharacterRoster, typeof(GetAccountCharacterRosterRequest)),
            (typeof(GameCheckCharacterNameCommand), ClientMessageType.GameCheckCharacterName, typeof(CheckCharacterNameRequest)),
            (typeof(GameCreateCharacterCommand), ClientMessageType.GameCreateCharacter, typeof(CreateCharacterRequest)),
            (typeof(GameDeleteCharacterCommand), ClientMessageType.GameDeleteCharacter, typeof(DeleteCharacterRequest)),
        ];
        foreach (var (command, message, request) in expected)
        {
            var registration = Assert.Single(registrations, row => row.CommandType == command);
            Assert.Equal(CommandRouteKind.Protocol, registration.Route);
            Assert.Equal(CommandExecutionKind.Direct, registration.ExecutionKind);
            var binding = Assert.Single(registration.ProtocolMessages!);
            Assert.Equal(message, binding.MessageType);
            Assert.Equal(request, binding.MessageClrType);
        }
        Assert.NotNull(provider.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>());
        // CharacterRosterCommandFlowTests and GmInitializationHttpTests exercise the actual write,
        // commit-before-ACK, rollback and post-commit read failure instead of inferring it from Kind.
    }

    [Fact]
    public void GameLoginUsesTheProtocolRouteWithExplicitWorkUnits()
    {
        using var provider = BuildProductionGraph();
        var registration = Assert.Single(
            provider.GetServices<CommandHandlerRegistration>(),
            value => value.CommandType == typeof(GameLoginCommand));

        Assert.Equal(CommandExecutionKind.Direct, registration.ExecutionKind);
        Assert.Equal(CommandRouteKind.Protocol, registration.Route);
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
