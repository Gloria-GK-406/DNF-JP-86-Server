using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Tests.Characters;
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
        var sessions = new SelectedSessionDomainService();
        var runtimes = new SelectedSessionRuntimeDirectory();
        var presence = new SessionPresenceDomainService();
        var positions = new CharacterPositionDomainService();
        var service = new SelectCharacterSessionEntryService(
            sessions, runtimes, presence, new PvpLobbyDomainService(), positions);
        var first = await EntryAsync(1, 60, "Game:first", x: 100);
        var second = await EntryAsync(2, 61, "Game:second", x: 300);

        sessions.Select(first.Session);
        service.Publish(first.Session, first.Result);
        sessions.Select(second.Session);
        service.Publish(second.Session, second.Result);

        var plan = Assert.IsType<TownParticipantPublicationPlan>(
            service.PlanTownPublication(second.Session, second.Result));
        Assert.Equal([new CharacterId(60)], plan.Audience);
        Assert.Equal(new CharacterId(61), plan.State.Newcomer.Character.Identity.CharacterId);
        var existing = Assert.Single(plan.State.ExistingCharacters);
        Assert.Equal(new CharacterId(60), existing.Character.Identity.CharacterId);
        Assert.Equal((short)100, existing.Spawn.X);
        Assert.Equal((short)300, plan.State.Newcomer.Spawn.X);
    }

    [Fact]
    public async Task Private_gate_room_never_builds_a_cross_character_publication()
    {
        var sessions = new SelectedSessionDomainService();
        var runtimes = new SelectedSessionRuntimeDirectory();
        var presence = new SessionPresenceDomainService();
        var positions = new CharacterPositionDomainService();
        var service = new SelectCharacterSessionEntryService(
            sessions, runtimes, presence, new PvpLobbyDomainService(), positions);
        var first = await EntryAsync(1, 60, "Game:first", x: 100, sharesTownPresence: false);
        var second = await EntryAsync(2, 61, "Game:second", x: 300, sharesTownPresence: false);

        sessions.Select(first.Session);
        service.Publish(first.Session, first.Result);
        sessions.Select(second.Session);
        service.Publish(second.Session, second.Result);

        Assert.Null(service.PlanTownPublication(second.Session, second.Result));
    }

    private static async ValueTask<(SelectedSessionIdentity Session, Dfo.Server.Data.Game.SelectCharacterResult Result)>
        EntryAsync(
            int accountId,
            int characterId,
            string connectionId,
            short x,
            bool sharesTownPresence = true)
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
            connectionGeneration: 1,
            listenerPort: 10011);
        return (session, result);
    }
}
