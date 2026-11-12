using Dfo.Server.Application.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Testing;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class SelectedSessionPartyCleanupTests
{
    [Fact]
    public async Task Member_cleanup_clears_the_departing_member_and_refreshes_survivors()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = new Fixture();
            var leader = fixture.Admit(1);
            var member = fixture.Admit(2);
            var observer = fixture.Admit(3);
            fixture.Form(leader, member);

            var plan = await TestEffectFootprints.Run(member, cap => fixture.Cleanup.CleanupAsync(
                member, includeOwner: false, cap, CancellationToken.None));

            Assert.Equal(
                [member.CharacterId],
                plan.PartyNotifications.ClearAudience.ToArray());
            var survivors = Assert.IsType<Dfo.Server.Data.Parties.PartySnapshot>(
                plan.PartyNotifications.Snapshot);
            Assert.Equal(
                [leader.WireUserId],
                survivors.Members.Select(value => value.WireUserId));
            Assert.Equal(
                new PartyDirectoryChange(new PartyDirectoryScope(11, 1, 2), new PartyDirectoryScope(11, 1, 2)),
                plan.PartyNotifications.DirectoryChange);
            Assert.True(fixture.Parties.FindParty(member).IsVirtual);
            Assert.False(fixture.Parties.FindParty(leader).IsVirtual);
        });
    }

    [Fact]
    public async Task Leader_cleanup_promotes_the_next_member_and_rekeys_every_old_member()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = new Fixture();
            var leader = fixture.Admit(1);
            var member = fixture.Admit(2);
            var observer = fixture.Admit(3);
            fixture.Form(leader, member);
            var oldParty = fixture.Parties.FindParty(leader);

            var plan = await TestEffectFootprints.Run(leader, cap => fixture.Cleanup.CleanupAsync(
                leader, includeOwner: false, cap, CancellationToken.None));

            var survivors = Assert.IsType<Dfo.Server.Data.Parties.PartySnapshot>(
                plan.PartyNotifications.Snapshot);
            Assert.Equal(member.WireUserId, survivors.LeaderWireUserId);
            Assert.Equal(member.CharacterId, Assert.Single(survivors.Members).CharacterId);
            Assert.Equal(oldParty.Key, survivors.Key);
            Assert.NotEqual(oldParty.LeadershipVersion, survivors.LeadershipVersion);
            Assert.Equal(oldParty.Key, plan.PartyNotifications.ClearPartyKey);
            Assert.Equal(oldParty.LeadershipVersion, plan.PartyNotifications.ClearLeadershipVersion);
            Assert.Equal(
                [leader.CharacterId, member.CharacterId],
                plan.PartyNotifications.ClearAudience.ToArray());
            Assert.Equal(
                new PartyDirectoryChange(
                    new PartyDirectoryScope(11, 1, 2),
                    new PartyDirectoryScope(11, 1, 2)),
                plan.PartyNotifications.DirectoryChange);
            Assert.True(fixture.Parties.FindParty(leader).IsVirtual);
            Assert.False(fixture.Parties.FindParty(member).IsVirtual);
        });
    }

    [Fact]
    public async Task Late_old_session_cleanup_cannot_remove_a_replacement_party()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = new Fixture();
            var oldLeader = fixture.Admit(1);
            var oldMember = fixture.Admit(2);
            fixture.Form(oldLeader, oldMember);
            await TestEffectFootprints.Run(oldLeader, cap => fixture.Cleanup.CleanupAsync(
                oldLeader, includeOwner: false, cap, CancellationToken.None));

            var replacement = fixture.Admit(1, connection: "replacement");
            var replacementMember = fixture.Admit(3);
            fixture.Form(replacement, replacementMember);

            var stalePlan = await TestEffectFootprints.Run(oldLeader, cap => fixture.Cleanup.CleanupAsync(
                oldLeader, includeOwner: false, cap, CancellationToken.None));

            Assert.Empty(stalePlan.PartyNotifications.ClearAudience);
            Assert.Null(stalePlan.PartyNotifications.Snapshot);
            Assert.False(fixture.Parties.FindParty(replacement).IsVirtual);
            Assert.False(fixture.Parties.FindParty(replacementMember).IsVirtual);
        });
    }

    [Fact]
    public async Task Later_cleanup_failure_preserves_the_already_resolved_party_projection()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = new Fixture(failAfterPartyCleanup: true);
            var leader = fixture.Admit(1);
            var member = fixture.Admit(2);
            fixture.Form(leader, member);

            var failure = await Assert.ThrowsAsync<SelectedSessionCleanupFailure>(async () =>
                await TestEffectFootprints.Run(member, cap => fixture.Cleanup.CleanupAsync(
                    member, includeOwner: false, cap, CancellationToken.None)));

            Assert.Equal(
                [member.CharacterId],
                failure.NotificationPlan.PartyNotifications.ClearAudience.ToArray());
            Assert.NotNull(failure.NotificationPlan.PartyNotifications.Snapshot);
        });
    }

    [Fact]
    public async Task Cleanup_notifies_only_town_peers_on_the_same_listener()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = new Fixture();
            var owner = fixture.Admit(1);
            var sameListener = fixture.Admit(2);
            _ = fixture.Admit(3, listenerPort: 10012);

            var plan = await TestEffectFootprints.Run(owner, cap => fixture.Cleanup.CleanupAsync(
                owner, includeOwner: false, cap, CancellationToken.None));

            Assert.Equal(
                [sameListener.CharacterId],
                plan.PeerAudience.Select(value => value.CharacterId));
        });
    }

    [Fact]
    public async Task Cleanup_ends_every_conversation_of_the_session_and_announces_it_only_to_members_still_on_their_window()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = new Fixture();
            var leaver = fixture.Admit(1);
            var stays = fixture.Admit(2);
            var gone = fixture.Admit(3);
            var bystander = fixture.Admit(4);
            var withStays = fixture.Conversations.Open(new(leaver, [0x61]), new(stays, [0x62]))!;
            var withGone = fixture.Conversations.Open(new(gone, [0x63]), new(leaver, [0x61]))!;
            var unrelated = fixture.Conversations.Open(new(stays, [0x62]), new(bystander, [0x64]))!;
            fixture.Release(gone);

            var plan = await TestEffectFootprints.Run(leaver, cap => fixture.Cleanup.CleanupAsync(
                leaver, includeOwner: false, cap, CancellationToken.None));

            var departure = Assert.Single(plan.ConversationDepartures);
            Assert.Equal(withStays.Id, departure.Conversation);
            Assert.Equal(leaver, departure.Leaver.Session);
            Assert.Equal(stays, departure.Remaining.Session);
            Assert.Null(fixture.Conversations.Find(withStays.Id));
            Assert.Null(fixture.Conversations.Find(withGone.Id));
            Assert.Same(unrelated, fixture.Conversations.Find(unrelated.Id));
        });
    }

    /// <summary>RQ-051: the leaver's name and its friends still in the world are taken before the session goes.</summary>
    [Fact]
    public async Task Cleanup_records_the_friends_in_the_world_to_tell_that_the_character_left()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = new Fixture();
            var leaver = fixture.Admit(1);
            _ = fixture.Admit(2);
            var released = fixture.Admit(3);
            _ = fixture.Admit(4);
            fixture.Friends.Named(1, "leaver").Friend(owner: 2, target: 1).Friend(owner: 3, target: 1).Friend(owner: 5, target: 1)
                .Friend(owner: 1, target: 4);
            fixture.Release(released);

            var plan = await TestEffectFootprints.Run(leaver, cap => fixture.Cleanup.CleanupAsync(
                leaver, includeOwner: false, cap, CancellationToken.None));

            var departure = Assert.IsType<SelectedSessionFriendDeparture>(plan.FriendDeparture);
            Assert.Equal(new CharacterId(1), departure.Character);
            Assert.Equal("leaver"u8.ToArray(), departure.Name);
            Assert.Equal([new CharacterId(2)], departure.Observers);
        });
    }

    [Fact]
    public async Task Cleanup_with_no_friend_in_the_world_records_no_friend_departure()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = new Fixture();
            var leaver = fixture.Admit(1);
            fixture.Friends.Named(1, "leaver").Friend(owner: 5, target: 1);

            var plan = await TestEffectFootprints.Run(leaver, cap => fixture.Cleanup.CleanupAsync(
                leaver, includeOwner: false, cap, CancellationToken.None));

            Assert.Null(plan.FriendDeparture);
        });
    }

    /// <summary>
    /// A failed read costs the friend notice only. It is not a cleanup failure: return to selection
    /// would close the connection over it and a replacing selection would roll back the steps before it.
    /// </summary>
    [Fact]
    public async Task A_failed_friend_read_drops_only_the_friend_notice()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = new Fixture();
            var leaver = fixture.Admit(1);
            var stays = fixture.Admit(2);
            var conversation = fixture.Conversations.Open(new(leaver, [0x61]), new(stays, [0x62]))!;
            fixture.Friends.Failure = new IOException("relation read failed");

            var plan = await TestEffectFootprints.Run(leaver, cap =>
                fixture.Cleanup.CleanupAsync(leaver, includeOwner: false, cap, CancellationToken.None));

            Assert.Null(plan.FriendDeparture);
            Assert.Equal(conversation.Id, Assert.Single(plan.ConversationDepartures).Conversation);
            Assert.False(fixture.IsSelected(leaver));
        });
    }

    /// <summary>A session that never entered the world was never announced online, so its end is not announced either.</summary>
    [Fact]
    public async Task A_session_that_never_entered_the_world_records_no_friend_departure()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var fixture = new Fixture();
            _ = fixture.Admit(2);
            var selected = fixture.SelectOnly(1);
            fixture.Friends.Named(1, "leaver").Friend(owner: 2, target: 1);

            var plan = await TestEffectFootprints.Run(selected, cap => fixture.Cleanup.CleanupAsync(
                selected, includeOwner: true, cap, CancellationToken.None));

            Assert.Null(plan.FriendDeparture);
        });
    }

    private sealed class Fixture
    {
        private readonly SelectedSessionDomainService sessions;
        private readonly Dfo.Server.Application.Users.OnlineStateRegistry state;
        private readonly SessionPresenceDomainService presence = new();

        public Fixture(bool failAfterPartyCleanup = false)
        {
            sessions = Dfo.Server.Testing.TestSessions.Directory(out state);
            Parties = new PartyServiceTestAdapter(
                sessions,
                presence,
                new StubPartyMemberStatusDomainService(),
                new PartyModeRuleService(),
                TimeProvider.System);
            Cleanup = new SelectedSessionCleanupService(
                sessions,
                new CharacterContainerDomainService(TestRequired.TransactionScope()),
                presence,
                new ExpertStoreDomainService(),
                new PvpLobbyDomainService(),
                [new KeptPositions()],
                failAfterPartyCleanup
                    ? new ThrowingRuntime(presence.Locations)
                    : new OnlineCharacterRuntimeDomainService(TimeProvider.System, presence.Locations),
                new StubChannelEntryPolicy(),
                Parties.Service,
                TestParties.Runs(),
                [], TestRequired.TransactionScope(), creatures: TestRequired.Absent<Dfo.Server.Abstractions.Creatures.ICreatureRuntimeDomainService>(), illusionTower: TestRequired.Absent<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerDomainService>(), illusionTowerDepartures: [], housing: TestRequired.Absent<Dfo.Server.Abstractions.Marriage.IMarriageHousingSessionDomainService>(), bossTower: TestRequired.Absent<Dfo.Server.Abstractions.World.BossTower.IBossTowerDomainService>(), bossTowerDepartures: [], conversations: Conversations, relations: Friends, identities: Friends, logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<SelectedSessionCleanupService>.Instance);
        }

        public PartyServiceTestAdapter Parties { get; }

        public Dfo.Server.Application.Chat.ChatConversationDomainService Conversations { get; } = new();

        public TestCharacterRelations Friends { get; } = new();

        public bool IsSelected(SelectedSessionIdentity session) => sessions.StandingOf(session) is not null;

        /// <summary>Selected and not yet in the world, as a selection is before publication.</summary>
        public SelectedSessionIdentity SelectOnly(int value)
        {
            var online = state.RegisterConnection(new NetworkConnectionContext(
                new NetworkConnectionId($"party-{value}"), NetworkEndpointKind.Game, 10011));
            state.Authenticate(online.OnlineUserId, new AccountId(value));
            var session = SelectedSessionIdentity.Create(new AccountId(value), new CharacterId(value),
                online.Connection.ConnectionId, state.GetRequiredUser(online.OnlineUserId).Generation, 10011);
            Assert.Null(sessions.Select(session));
            return session;
        }

        public void Release(SelectedSessionIdentity session) => Assert.True(sessions.ReleaseExact(session));

        public SelectedSessionCleanupService Cleanup { get; }

        public SelectedSessionIdentity Admit(
            int value,
            string? connection = null,
            int listenerPort = 10011)
        {
            var session = sessions.InWorld(
                state,
                new AccountId(value),
                new CharacterId(value),
                connection ?? $"party-{value}",
                listenerPort);
            presence.Admit(new SessionPresence(
                session.CharacterId, listenerPort, 1, 2, true, session,
                ChannelId: listenerPort - 10_000));
            return session;
        }

        public void Form(SelectedSessionIdentity leader, SelectedSessionIdentity member)
        {
            Assert.True(Parties.RequestInvite(
                leader,
                member.WireUserId,
                new Dfo.Server.Data.Parties.PartyInviteDisplayState(64, 188, 0, 0)).Succeeded);
            Assert.True(Parties.AcceptInvite(member, leader.WireUserId).Succeeded);
        }
    }

    private sealed class KeptPositions : ISelectedSessionPositionPersistence
    {
        public ValueTask PersistAsync(AccountId accountId, CharacterId characterId,
            CharacterPositionState position, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    /// <summary>A step after party cleanup that fails: ending the online character's record.</summary>
    private sealed class ThrowingRuntime(OnlineCharacterLocationDomainService? locations = null) : IOnlineCharacterRuntimeDomainService
    {
        private readonly OnlineCharacterRuntimeDomainService inner =
            new(TimeProvider.System, locations ?? new OnlineCharacterLocationDomainService());

        public TownAreaMembershipChange Begin(SelectedSessionIdentity session, TownAreaKey area, CharacterPositionState position, bool sharesTownPresence, byte hpPercent) => inner.Begin(session, area, position, sharesTownPresence, hpPercent);
        public bool EndExact(SelectedSessionIdentity session) =>
            throw new InvalidOperationException("fault after party cleanup");
        public CharacterPositionState? PositionOf(SelectedSessionIdentity session) => inner.PositionOf(session);
        public CharacterPositionState? PositionOf(CharacterId characterId) => inner.PositionOf(characterId);
        public TownAreaMembershipChange? CrossInto(SelectedSessionIdentity session, TownAreaKey destination, short x, short y, bool sharesTownPresence, byte? direction = null) => inner.CrossInto(session, destination, x, y, sharesTownPresence, direction);
        public TownAreaMovement? StepTo(SelectedSessionIdentity session, short x, short y, byte direction) => inner.StepTo(session, x, y, direction);
        public TownAreaMember? StandingOf(SelectedSessionIdentity session) => inner.StandingOf(session);
        public TownAreaMembershipChange? LeaveTown(SelectedSessionIdentity session, TownAreaDepartureReason reason) => inner.LeaveTown(session, reason);
        public TownAreaMembershipChange? ReturnToTown(SelectedSessionIdentity session, TownAreaKey area, CharacterPositionState position, bool sharesTownPresence) =>
            inner.ReturnToTown(session, area, position, sharesTownPresence);
        public byte HpPercentOf(CharacterId characterId) => inner.HpPercentOf(characterId);
        public bool IsCoolingDown(CharacterId characterId, Dfo.Server.Abstractions.Inventory.ItemCooldownKey key) =>
            inner.IsCoolingDown(characterId, key);
        public void StartCooldown(SelectedSessionIdentity session, Dfo.Server.Abstractions.Inventory.ItemCooldownKey key, TimeSpan duration) =>
            inner.StartCooldown(session, key, duration);
        public void ApplySpiritPotion(SelectedSessionIdentity session, double bonusRate, TimeSpan duration) =>
            inner.ApplySpiritPotion(session, bonusRate, duration);
        public double SpiritPotionBonusRateOf(CharacterId characterId) => inner.SpiritPotionBonusRateOf(characterId);
    }
}
