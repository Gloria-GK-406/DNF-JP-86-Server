using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Testing;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class SessionAdjacentEntryTests
{
    private static readonly ExpertStoreDomainService Stores = new();

    /// <remarks>
    /// The registry exists whether or not anyone has been admitted to it, so a quiet server
    /// answers "nobody is here" rather than failing or reporting an absent source. This is
    /// the distinction the previous attempt at this outcome could not make.
    /// </remarks>
    [Fact]
    public async Task An_unwritten_registry_reports_an_empty_area_rather_than_failing()
    {
        var service = Service(new SessionPresenceDomainService(), Stores, new PvpLobbyDomainService());

        var result = await service.LoadAsync(Subject(), CancellationToken.None);

        Assert.Empty(result.TownPresence.Others);
        Assert.False(result.ExpertStore.OwnsOpenStore);
    }

    [Fact]
    public async Task The_area_reports_the_other_characters_standing_in_it()
    {
        var registry = new SessionPresenceDomainService();
        registry.Admit(Presence(10));
        registry.Admit(Presence(11));
        var service = Service(registry, Stores, new PvpLobbyDomainService());

        var result = await service.LoadAsync(Subject(characterId: 99), CancellationToken.None);

        Assert.Equal([10, 11], result.TownPresence.Others.Select(other => other.CharacterId.Value));
        Assert.Equal(1, result.TownPresence.TownId);
        Assert.Equal(0, result.TownPresence.AreaId);
    }

    [Fact]
    public async Task A_private_gate_room_does_not_report_another_character_with_the_same_area_ids()
    {
        var registry = new SessionPresenceDomainService();
        registry.Admit(Presence(10, sharesTownPresence: false));
        registry.Admit(Presence(11, sharesTownPresence: false));
        var service = Service(registry, Stores, new PvpLobbyDomainService());

        var result = await service.LoadAsync(Subject(characterId: 99), CancellationToken.None);

        Assert.Empty(result.TownPresence.Others);
    }

    [Fact]
    public async Task The_entering_character_is_not_listed_among_the_others()
    {
        var registry = new SessionPresenceDomainService();
        registry.Admit(Presence(10));
        registry.Admit(Presence(99));
        var service = Service(registry, Stores, new PvpLobbyDomainService());

        var result = await service.LoadAsync(Subject(characterId: 99), CancellationToken.None);

        Assert.Equal([10], result.TownPresence.Others.Select(other => other.CharacterId.Value));
    }

    [Fact]
    public async Task Characters_in_a_different_area_are_not_listed()
    {
        var registry = new SessionPresenceDomainService();
        registry.Admit(Presence(10));
        registry.Admit(Presence(11, areaId: 5));
        registry.Admit(Presence(12, townId: 2));
        var service = Service(registry, Stores, new PvpLobbyDomainService());

        var result = await service.LoadAsync(Subject(characterId: 99), CancellationToken.None);

        Assert.Equal([10], result.TownPresence.Others.Select(other => other.CharacterId.Value));
    }

    /// <remarks>
    /// Town and area bytes are reused by every Game listener. They identify a visible place only
    /// inside one channel, so a character on another listener must not leak into the entry roster.
    /// </remarks>
    [Fact]
    public async Task Characters_on_a_different_listener_are_not_listed()
    {
        var registry = new SessionPresenceDomainService();
        registry.Admit(Presence(10));
        registry.Admit(Presence(11, listenerPort: 10161));
        var service = Service(registry, Stores, new PvpLobbyDomainService());

        var result = await service.LoadAsync(Subject(characterId: 99), CancellationToken.None);

        Assert.Equal([10], result.TownPresence.Others.Select(other => other.CharacterId.Value));
    }

    [Fact]
    public async Task A_stale_presence_is_not_listed_after_its_session_moves_to_another_listener()
    {
        var registry = new SessionPresenceDomainService();
        var oldPresence = Presence(10, listenerPort: 10011);
        registry.Admit(oldPresence);
        var sessions = Dfo.Server.Testing.TestSessions.Directory();
        _ = sessions.Select(oldPresence.Session! with
        {
            ConnectionId = new Dfo.Server.Data.Networking.NetworkConnectionId("Game:replacement"),
            ConnectionGeneration = 2,
            ListenerPort = 10012,
        });
        var service = new SessionAdjacentEntryDomainService(
            registry, sessions, Stores, new PvpLobbyDomainService());

        var result = await service.LoadAsync(Subject(characterId: 99), CancellationToken.None);

        Assert.Empty(result.TownPresence.Others);
    }

    /// <remarks>
    /// A character who walks into a dungeon keeps their last town coordinates. The legacy
    /// directory excludes them for exactly that reason — its own comment calls those fields
    /// leftovers from before they entered — so reporting them would stand someone who is in
    /// a dungeon next to the arriving character.
    /// </remarks>
    [Fact]
    public async Task A_character_who_is_no_longer_standing_in_town_is_not_listed()
    {
        var registry = new SessionPresenceDomainService();
        registry.Admit(Presence(10));
        registry.Admit(Presence(11, standingInTown: false));
        var service = Service(registry, Stores, new PvpLobbyDomainService());

        var result = await service.LoadAsync(Subject(characterId: 99), CancellationToken.None);

        Assert.Equal([10], result.TownPresence.Others.Select(other => other.CharacterId.Value));
    }

    /// <remarks>
    /// The entering character has no presence record at the moment their entry is composed —
    /// admitting them is the lifecycle's job and happens later. An implementation that read
    /// the store off a presence record would therefore answer "no store" for every entering
    /// character, always, which is why this test deliberately admits no presence.
    /// </remarks>
    [Fact]
    public async Task An_open_store_is_reported_for_a_character_with_no_presence_record()
    {
        var stores = new ExpertStoreDomainService();
        stores.Open(new CharacterId(99));
        var presence = new SessionPresenceDomainService();
        var service = Service(presence, stores, new PvpLobbyDomainService());

        var result = await service.LoadAsync(Subject(characterId: 99), CancellationToken.None);

        Assert.Null(presence.Find(new CharacterId(99)));
        Assert.True(result.ExpertStore.OwnsOpenStore);
    }

    [Fact]
    public async Task Someone_elses_open_store_is_not_reported_as_the_entering_characters()
    {
        var stores = new ExpertStoreDomainService();
        stores.Open(new CharacterId(10));
        var registry = new SessionPresenceDomainService();
        registry.Admit(Presence(10));
        var service = Service(registry, stores, new PvpLobbyDomainService());

        var result = await service.LoadAsync(Subject(characterId: 99), CancellationToken.None);

        Assert.False(result.ExpertStore.OwnsOpenStore);
        Assert.Single(result.TownPresence.Others);
    }

    [Fact]
    public void Closing_a_store_removes_it()
    {
        var stores = new ExpertStoreDomainService();
        stores.Open(new CharacterId(10));

        Assert.True(stores.Close(new CharacterId(10)));
        Assert.False(stores.Close(new CharacterId(10)));
        Assert.False(stores.HasOpenStore(new CharacterId(10)));
    }

    /// <remarks>
    /// A projection that must emit a stable sequence needs one, and dictionary enumeration
    /// order is not stable across runs.
    /// </remarks>
    [Fact]
    public async Task The_others_are_reported_in_a_stable_order()
    {
        var registry = new SessionPresenceDomainService();
        foreach (var id in new[] { 30, 10, 20 })
            registry.Admit(Presence(id));
        var service = Service(registry, Stores, new PvpLobbyDomainService());

        var result = await service.LoadAsync(Subject(characterId: 99), CancellationToken.None);

        Assert.Equal([10, 20, 30], result.TownPresence.Others.Select(other => other.CharacterId.Value));
    }

    private static SessionAdjacentSubject Subject(int characterId = 99) => new(
        new CharacterId(characterId),
        new AccountId(1),
        StubChannelEntryPolicy.GeneralChannel,
        TownId: 1,
        AreaId: 0);

    private static SessionPresence Presence(
        int characterId,
        int listenerPort = 10011,
        byte townId = 1,
        byte areaId = 0,
        bool standingInTown = true,
        bool sharesTownPresence = true)
    {
        var id = new CharacterId(characterId);
        var session = SelectedSessionIdentity.Create(
            new AccountId(characterId),
            id,
            new Dfo.Server.Data.Networking.NetworkConnectionId(
                $"Game:{characterId}:{listenerPort}"),
            connectionGeneration: 1,
            listenerPort);
        return new SessionPresence(
            id,
            listenerPort,
            townId,
            areaId,
            standingInTown,
            session,
            sharesTownPresence);
    }

    private static SessionAdjacentEntryDomainService Service(
        SessionPresenceDomainService presence,
        IExpertStoreDomainService stores,
        IPvpLobbyDomainService lobby)
    {
        var sessions = Dfo.Server.Testing.TestSessions.Directory();
        foreach (var candidate in presence.Query(SessionPresenceScope.Global))
        {
            if (candidate.Session is { } exact)
                _ = sessions.Select(exact);
        }

        return new SessionAdjacentEntryDomainService(presence, sessions, stores, lobby);
    }
}
