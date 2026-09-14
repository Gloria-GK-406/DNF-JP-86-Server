using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class SessionPresenceScopeTests
{
    [Fact]
    public void Global_scope_returns_every_presence_in_stable_character_order()
    {
        var presence = Presence();

        var result = presence.Query(SessionPresenceScope.Global);

        Assert.Equal([10, 20, 30, 40, 50, 60, 70], result.Select(value => value.CharacterId.Value));
    }

    [Fact]
    public void Composed_scope_requires_every_selected_dimension()
    {
        var presence = Presence();
        var scope = SessionPresenceScope.VisibleTownArea(
            listenerPort: 10011,
            townId: 1,
            areaId: 2,
            excludingCharacterId: new CharacterId(10));

        var result = presence.Query(scope);

        var only = Assert.Single(result);
        Assert.Equal(new CharacterId(20), only.CharacterId);
        Assert.Equal(10011, only.ListenerPort);
    }

    [Fact]
    public void Channel_and_town_can_be_composed_without_an_area()
    {
        var presence = Presence();
        var scope = new SessionPresenceScope(
            SessionPresenceScopeFlags.SameChannel | SessionPresenceScopeFlags.SameTown,
            listenerPort: 10011,
            townId: 1);

        Assert.Equal(
            [10, 20, 40, 50, 70],
            presence.Query(scope).Select(value => value.CharacterId.Value));
    }

    [Fact]
    public void Channel_and_town_constraints_are_independently_composable()
    {
        var presence = Presence();
        var sameChannel = new SessionPresenceScope(
            SessionPresenceScopeFlags.SameChannel,
            listenerPort: 10011);
        var sameTown = new SessionPresenceScope(
            SessionPresenceScopeFlags.SameTown,
            townId: 1);

        Assert.Equal(
            [10, 20, 40, 50, 60, 70],
            presence.Query(sameChannel).Select(value => value.CharacterId.Value));
        Assert.Equal(
            [10, 20, 30, 40, 50, 70],
            presence.Query(sameTown).Select(value => value.CharacterId.Value));
    }

    [Fact]
    public void Boolean_dimensions_can_be_composed_without_numeric_anchors()
    {
        var presence = Presence();
        var scope = new SessionPresenceScope(
            SessionPresenceScopeFlags.StandingInTown
            | SessionPresenceScopeFlags.SharesTownPresence);

        Assert.Equal(
            [10, 20, 30, 60, 70],
            presence.Query(scope).Select(value => value.CharacterId.Value));
    }

    [Fact]
    public void A_replacement_presence_keeps_its_exact_session_generation()
    {
        var presence = new SessionPresenceDomainService();
        var old = Session(characterId: 20, generation: 1);
        var replacement = Session(characterId: 20, generation: 2);
        presence.Admit(At(20, session: old));
        presence.Admit(At(20, session: replacement));

        var current = Assert.Single(presence.Query(SessionPresenceScope.Global));

        Assert.Equal(replacement, current.Session);
    }

    [Fact]
    public async Task Party_scope_callbacks_follow_presence_write_order_for_one_character()
    {
        var parties = new PartyService(
            new StubPartyMemberStatusDomainService(),
            new PartyModeRuleService(),
            TimeProvider.System,
            EmptyPartyMemberSessionEndpointSource.Instance);
        var sink = new BlockingPartyDirectoryPresenceSink(parties);
        var presence = new SessionPresenceDomainService(sink);
        var session = Session(characterId: 20, generation: 1);
        var firstScope = new PartyDirectoryScope(11, 1, 1);
        var staleScope = new PartyDirectoryScope(11, 1, 2);
        var latestScope = new PartyDirectoryScope(11, 1, 3);
        presence.Admit(new SessionPresence(
            session.CharacterId, 10011, 1, 1, true, session, ChannelId: 11));
        Assert.True(parties.CreateOrUpdateSettings(
            new PartyCharacterSnapshot(
                session.AccountId, session.CharacterId, session.WireUserId, firstScope),
            new PartyModeRuleService().DefaultSettings,
            PartyTitle.Empty).Succeeded);
        sink.BlockNextUpdate();

        var staleMove = Task.Run(() => presence.RelocateExact(
            session, staleScope.TownId, staleScope.AreaId, standingInTown: true));
        await sink.BlockedUpdateEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var latestMove = Task.Run(() => presence.RelocateExact(
            session, latestScope.TownId, latestScope.AreaId, standingInTown: true));

        sink.ReleaseBlockedUpdate();
        Assert.True(await staleMove.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(await latestMove.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Empty(parties.Query(new PartyDirectoryQuery(staleScope)));
        Assert.Single(parties.Query(new PartyDirectoryQuery(latestScope)));
        Assert.Equal(latestScope, sink.Scopes.Last());
    }

    [Theory]
    [InlineData(SessionPresenceScopeFlags.SameChannel, null, null, null)]
    [InlineData(SessionPresenceScopeFlags.None, 10011, null, null)]
    [InlineData(SessionPresenceScopeFlags.SameTown, null, null, null)]
    [InlineData(SessionPresenceScopeFlags.None, null, 1, null)]
    [InlineData(SessionPresenceScopeFlags.SameArea, null, null, 2)]
    [InlineData(SessionPresenceScopeFlags.SameTown, null, 1, 2)]
    public void Missing_or_extra_anchors_are_rejected(
        SessionPresenceScopeFlags flags,
        int? listenerPort,
        int? townId,
        int? areaId) =>
        Assert.ThrowsAny<ArgumentException>(() =>
            new SessionPresenceScope(
                flags,
                listenerPort,
                townId is null ? null : checked((byte)townId.Value),
                areaId is null ? null : checked((byte)areaId.Value)));

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Invalid_listener_ports_are_rejected(int listenerPort) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SessionPresenceScope(
                SessionPresenceScopeFlags.SameChannel,
                listenerPort));

    [Fact]
    public void Unknown_flags_are_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SessionPresenceScope((SessionPresenceScopeFlags)(1 << 20)));

    private static SessionPresenceDomainService Presence()
    {
        var presence = new SessionPresenceDomainService();
        presence.Admit(At(30, listener: 10012));
        presence.Admit(At(10));
        presence.Admit(At(20));
        presence.Admit(At(40, standing: false));
        presence.Admit(At(50, shared: false));
        presence.Admit(At(60, town: 2));
        presence.Admit(At(70, area: 3));
        return presence;
    }

    private static SessionPresence At(
        int characterId,
        int listener = 10011,
        byte town = 1,
        byte area = 2,
        bool standing = true,
        bool shared = true,
        SelectedSessionIdentity? session = null) =>
        new(new CharacterId(characterId), listener, town, area, standing,
            session, shared);

    private static SelectedSessionIdentity Session(int characterId, long generation) =>
        SelectedSessionIdentity.Create(
            new Dfo.Server.Data.Accounts.AccountId(characterId),
            new CharacterId(characterId),
            new Dfo.Server.Data.Networking.NetworkConnectionId($"conn-{generation}"),
            generation,
            listenerPort: 10011);

    private sealed class BlockingPartyDirectoryPresenceSink(IPartyDirectoryPresenceSink inner)
        : IPartyDirectoryPresenceSink
    {
        private readonly ManualResetEventSlim release = new(initialState: false);
        private int blockNext;

        public TaskCompletionSource BlockedUpdateEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public List<PartyDirectoryScope?> Scopes { get; } = [];

        public void BlockNextUpdate() => Interlocked.Exchange(ref blockNext, 1);

        public void ReleaseBlockedUpdate() => release.Set();

        public void UpdateDirectoryPresence(CharacterId characterId, PartyDirectoryScope? scope)
        {
            if (Interlocked.Exchange(ref blockNext, 0) == 1)
            {
                BlockedUpdateEntered.TrySetResult();
                release.Wait(TimeSpan.FromSeconds(2));
            }

            inner.UpdateDirectoryPresence(characterId, scope);
            lock (Scopes)
                Scopes.Add(scope);
        }
    }
}
