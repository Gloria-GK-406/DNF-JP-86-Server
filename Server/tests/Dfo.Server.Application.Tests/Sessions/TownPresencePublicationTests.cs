using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Testing;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class TownPresencePublicationTests
{
    [Fact]
    public async Task Published_second_character_receives_the_first_and_targets_it_for_arrival()
    {
        var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state);
        var presence = new SessionPresenceDomainService();
        var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System);
        var currentViews = new FixedCurrentViews();
        var service = new SelectCharacterSessionEntryService(
            sessions, currentViews, presence, new PvpLobbyDomainService(), positions);
        var first = await EntryAsync(1, 60, "Game:first", x: 100);
        var second = await EntryAsync(2, 61, "Game:second", x: 300);

        sessions.EnterWorldWithConnection(state, first.Session);
        service.Publish(first.Session, first.Result);
        currentViews.Publish(first.Session, first.Result);
        sessions.EnterWorldWithConnection(state, second.Session);
        service.Publish(second.Session, second.Result);
        currentViews.Publish(second.Session, second.Result);

        var plan = Assert.IsType<TownParticipantPublicationPlan>(
            await service.PlanTownPublicationAsync(second.Session, second.Result, CancellationToken.None));
        Assert.Equal([new CharacterId(60)], plan.Audience.Select(value => value.CharacterId));
        Assert.Equal(new CharacterId(61), plan.State.Newcomer.Character.Identity.CharacterId);
        var existing = Assert.Single(plan.State.ExistingCharacters);
        Assert.Equal(new CharacterId(60), existing.Character.Identity.CharacterId);
        Assert.Equal((short)100, existing.Spawn.X);
        Assert.Equal((short)300, plan.State.Newcomer.Spawn.X);
    }

    [Fact]
    public async Task Private_gate_room_never_builds_a_cross_character_publication()
    {
        var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state);
        var presence = new SessionPresenceDomainService();
        var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System);
        var currentViews = new FixedCurrentViews();
        var service = new SelectCharacterSessionEntryService(
            sessions, currentViews, presence, new PvpLobbyDomainService(), positions);
        var first = await EntryAsync(1, 60, "Game:first", x: 100, sharesTownPresence: false);
        var second = await EntryAsync(2, 61, "Game:second", x: 300, sharesTownPresence: false);

        sessions.EnterWorldWithConnection(state, first.Session);
        service.Publish(first.Session, first.Result);
        currentViews.Publish(first.Session, first.Result);
        sessions.EnterWorldWithConnection(state, second.Session);
        service.Publish(second.Session, second.Result);
        currentViews.Publish(second.Session, second.Result);

        Assert.Null(await service.PlanTownPublicationAsync(second.Session, second.Result, CancellationToken.None));
    }

    [Fact]
    public async Task A_character_on_another_listener_but_the_same_logical_channel_is_published()
    {
        var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state);
        var presence = new SessionPresenceDomainService();
        var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System);
        var currentViews = new FixedCurrentViews();
        var service = new SelectCharacterSessionEntryService(
            sessions, currentViews, presence, new PvpLobbyDomainService(), positions);
        var first = await EntryAsync(1, 60, "Game:first", x: 100, listenerPort: 10012);
        var second = await EntryAsync(2, 61, "Game:second", x: 300, listenerPort: 10011);

        sessions.EnterWorldWithConnection(state, first.Session);
        service.Publish(first.Session, first.Result);
        currentViews.Publish(first.Session, first.Result);
        sessions.EnterWorldWithConnection(state, second.Session);
        service.Publish(second.Session, second.Result);
        currentViews.Publish(second.Session, second.Result);

        var plan = Assert.IsType<TownParticipantPublicationPlan>(
            await service.PlanTownPublicationAsync(second.Session, second.Result, CancellationToken.None));
        Assert.Equal([first.Session], plan.Audience);
    }

    [Fact]
    public async Task A_stale_presence_is_not_redirected_to_its_replacement_on_another_listener()
    {
        var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state);
        var presence = new SessionPresenceDomainService();
        var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System);
        var currentViews = new FixedCurrentViews();
        var service = new SelectCharacterSessionEntryService(
            sessions, currentViews, presence, new PvpLobbyDomainService(), positions);
        var old = await EntryAsync(1, 60, "Game:old", x: 100, listenerPort: 10011);
        var newcomer = await EntryAsync(2, 61, "Game:newcomer", x: 300, listenerPort: 10011);

        sessions.EnterWorldWithConnection(state, old.Session);
        service.Publish(old.Session, old.Result);
        currentViews.Publish(old.Session, old.Result);
        _ = sessions.InWorld(
            state, old.Session.AccountId, old.Session.CharacterId, "Game:replacement", listenerPort: 10012);
        sessions.EnterWorldWithConnection(state, newcomer.Session);
        service.Publish(newcomer.Session, newcomer.Result);
        currentViews.Publish(newcomer.Session, newcomer.Result);

        Assert.Null(await service.PlanTownPublicationAsync(newcomer.Session, newcomer.Result, CancellationToken.None));
    }

    private static async ValueTask<(SelectedSessionIdentity Session, Dfo.Server.Data.Game.SelectCharacterResult Result)>
        EntryAsync(
            int accountId,
            int characterId,
            string connectionId,
            short x,
            bool sharesTownPresence = true,
            int listenerPort = 10011)
    {
        var result = await CharacterEntryInventoryTests.CreateLifecycleResultAsync(
            new AccountId(accountId), characterId, 0);
        var spawn = result.World.Channel.Spawn with
        {
            TownId = 1,
            AreaId = 0,
            X = x,
            Y = 200,
            Direction = 5,
            SharesTownPresence = sharesTownPresence,
        };
        result = result with
        {
            Character = result.Character with
            {
                Position = new CharacterPositionState(
                    spawn.TownId, spawn.AreaId, spawn.X, spawn.Y, spawn.Direction),
            },
            World = result.World with
            {
                Channel = result.World.Channel with { Spawn = spawn },
            },
        };
        var session = SelectedSessionIdentity.Create(
            new AccountId(accountId),
            new CharacterId(characterId),
            new NetworkConnectionId(connectionId),
            connectionGeneration: 2,
            listenerPort);
        return (session, result);
    }

    private sealed class FixedCurrentViews : ISelectedCharacterCurrentViewDomainService
    {
        private readonly Dictionary<SelectedSessionIdentity, SelectCharacterResult> entries = [];

        public void Publish(SelectedSessionIdentity session, SelectCharacterResult result) =>
            entries[session] = result;

        public ValueTask<SelectCharacterResult?> TryLoadAsync(
            SelectedSessionIdentity session, CancellationToken cancellationToken) =>
            ValueTask.FromResult(entries.GetValueOrDefault(session));
    }
}
