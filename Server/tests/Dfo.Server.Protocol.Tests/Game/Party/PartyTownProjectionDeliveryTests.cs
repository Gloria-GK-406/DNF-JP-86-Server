using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.Party;

public sealed class PartyTownProjectionDeliveryTests
{
    [Fact]
    public async Task A_leader_area_change_refreshes_members_and_clears_the_old_directory_observers()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var previousScope = new PartyDirectoryScope(11, 1, 1);
            var currentScope = new PartyDirectoryScope(11, 1, 2);
            var snapshot = new PartySnapshot(
                0x1234,
                7,
                4,
                Leader.WireUserId,
                new PartySettings([0, 0, 4, 0, 0, 0, 0, 5, 0, (byte)PartyCategory.Normal, 0, 0]),
                PartyTitle.Empty,
                ImmutableArray.Create(
                    new PartyMemberSnapshot(
                        new PartyCharacterSnapshot(
                            Leader.AccountId, Leader.CharacterId, Leader.WireUserId, currentScope),
                        1, null, 100),
                    new PartyMemberSnapshot(Character(Member), 0, null, 100)));
            var bus = new RecordingNotificationBus();
            var delivery = new PartyTownProjectionDelivery(
                new StubParties((Leader, snapshot), (Member, snapshot)),
                MovedPresence(),
                new FixedSelectedSessions(),
                bus,
                new PartyProjectionStreamRegistry());

            await delivery.RefreshMovedPartyAsync(
                Leader, previousScope, CancellationToken.None);

            Assert.Collection(
                bus.Publications,
                publication =>
                {
                    Assert.Equal(
                        [Leader, Member],
                        Assert.IsType<NotificationAudience.TheseSessions>(publication.Audience).Sessions);
                    Assert.False(Assert.IsType<PartyRosterChanged>(publication.Notification).ClearsParty);
                },
                publication =>
                {
                    Assert.Equal(
                        [Observer],
                        Assert.IsType<NotificationAudience.TheseSessions>(publication.Audience).Sessions);
                    Assert.True(Assert.IsType<PartyRosterChanged>(publication.Notification).ClearsParty);
                },
                publication =>
                {
                    Assert.Equal(
                        [NoParty],
                        Assert.IsType<NotificationAudience.TheseSessions>(publication.Audience).Sessions);
                    Assert.False(Assert.IsType<PartyRosterChanged>(publication.Notification).ClearsParty);
                });
        });
    }

    [Fact]
    public async Task RepresentedMembersOfOnePartyProduceOneRefreshForExactObservers()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var snapshot = Snapshot(revision: 3);
            var parties = new StubParties(
                (Leader, snapshot),
                (Member, snapshot));
            var bus = new RecordingNotificationBus();
            var delivery = new PartyTownProjectionDelivery(
                parties,
                Presence(),
                new FixedSelectedSessions(),
                bus,
                new PartyProjectionStreamRegistry());

            await delivery.RefreshObservedPartiesAsync(
                [Observer, Leader, Observer],
                CancellationToken.None);

            var publication = Assert.Single(bus.Publications);
            Assert.Equal(
                new[] { Observer },
                Assert.IsType<NotificationAudience.TheseSessions>(publication.Audience).Sessions);
            var roster = Assert.IsType<PartyRosterChanged>(publication.Notification);
            Assert.Same(snapshot, roster.Snapshot);
            Assert.False(roster.ClearsParty);
        });
    }

    [Fact]
    public async Task ACommittedRevisionStillRefreshesANewObserverButAnOlderSnapshotDoesNot()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var streams = new PartyProjectionStreamRegistry();
            streams.GetOrAdd(7).LatestRevision = 3;
            var currentBus = new RecordingNotificationBus();
            var current = new PartyTownProjectionDelivery(
                new StubParties((Leader, Snapshot(revision: 3))),
                Presence(),
                new FixedSelectedSessions(),
                currentBus,
                streams);

            await current.RefreshObservedPartiesAsync(
                [Observer], CancellationToken.None);

            Assert.Single(currentBus.Publications);

            streams.GetOrAdd(7).LatestRevision = 4;
            var staleBus = new RecordingNotificationBus();
            var stale = new PartyTownProjectionDelivery(
                new StubParties((Leader, Snapshot(revision: 3))),
                Presence(),
                new FixedSelectedSessions(),
                staleBus,
                streams);

            await stale.RefreshObservedPartiesAsync(
                [Observer], CancellationToken.None);

            Assert.Empty(staleBus.Publications);
        });
    }

    [Fact]
    public async Task ReadRepairRetriesATransientRosterFailureFromOneTopLevelCall()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var snapshot = Snapshot(revision: 3);
            var bus = new FailFirstRosterNotificationBus();
            var delivery = new PartyTownProjectionDelivery(
                new StubParties((Leader, snapshot)),
                Presence(),
                new FixedSelectedSessions(),
                bus,
                new PartyProjectionStreamRegistry());

            await delivery.RefreshObservedPartiesAsync(
                [Observer], CancellationToken.None);

            Assert.Equal(2, bus.Attempts);
            Assert.Equal(1, bus.Successes);
        });
    }

    [Fact]
    public async Task TownReadRepairCannotSupersedePendingLeaderRekeyClearForSurvivor()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var oldSnapshot = Snapshot(revision: 3);
            var newSnapshot = Snapshot(revision: 4) with
            {
                PartyId = 0x5678,
                LeaderWireUserId = Member.WireUserId,
                Members = ImmutableArray.Create(
                    new PartyMemberSnapshot(Character(Member), 0, null, 100)),
            };
            var streams = new PartyProjectionStreamRegistry();
            var bus = new RecordingNotificationBus();
            var sessions = new FixedSelectedSessions();
            var presence = Presence();
            var mutation = global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<global::Dfo.Server.Protocol.Game.Party.PartyNotificationDelivery>(bus, streams, sessions, presence);
            await mutation.DeliverAsync(
                new PartyNotificationPlan([], oldSnapshot), CancellationToken.None);
            bus.Publications.Clear();

            var town = new PartyTownProjectionDelivery(
                new MutableParties(Member, newSnapshot), presence, sessions, bus, streams);
            await town.RefreshMovedPartyAsync(Member, Character(Member).DirectoryScope,
                CancellationToken.None);
            await mutation.DeliverAsync(
                new PartyNotificationPlan(
                    [Leader.CharacterId, Member.CharacterId], newSnapshot,
                    ClearPartyId: oldSnapshot.PartyId,
                    ClearPartyGeneration: oldSnapshot.Generation,
                    ClearPartyRevision: newSnapshot.Revision),
                CancellationToken.None);

            var survivorRosters = bus.Publications
                .Where(value => value.Notification is PartyRosterChanged
                    && Assert.IsType<NotificationAudience.TheseSessions>(value.Audience)
                        .Sessions.Contains(Member))
                .Select(value => Assert.IsType<PartyRosterChanged>(value.Notification))
                .ToArray();
            Assert.Contains(survivorRosters, value => value.ClearsParty
                && value.ClearPartyId == oldSnapshot.PartyId);
            Assert.Contains(survivorRosters, value => !value.ClearsParty
                && value.Snapshot?.PartyId == newSnapshot.PartyId);
            Assert.True(Array.FindIndex(survivorRosters, value => value.ClearsParty)
                < Array.FindIndex(survivorRosters, value => !value.ClearsParty
                    && value.Snapshot?.PartyId == newSnapshot.PartyId));
        });
    }

    private static PartySnapshot Snapshot(long revision, long generation = 7) => new(
        0x1234,
        generation,
        revision,
        Leader.WireUserId,
        new PartySettings([0, 0, 4, 0, 0, 0, 0, 5, 0, (byte)PartyCategory.Normal, 0, 0]),
        PartyTitle.Empty,
        ImmutableArray.Create(
            new PartyMemberSnapshot(Character(Leader), 0, null, 100),
            new PartyMemberSnapshot(Character(Member), 1, null, 100)));

    private static PartyCharacterSnapshot Character(SelectedSessionIdentity session) =>
        new(
            session.AccountId,
            session.CharacterId,
            session.WireUserId,
            new PartyDirectoryScope(11, 1, 1));

    private static FixedPresence Presence() =>
        new(Leader, Member, Observer, NoParty);

    private static FixedPresence MovedPresence() =>
        new(
            (Leader, (byte)1, (byte)2),
            (Member, (byte)1, (byte)1),
            (Observer, (byte)1, (byte)1),
            (NoParty, (byte)1, (byte)2));

    private static SelectedSessionIdentity Session(int id, string connection) =>
        SelectedSessionIdentity.Create(
            new AccountId(id),
            new CharacterId(id),
            new NetworkConnectionId(connection),
            1,
            10011);

    private static SelectedSessionIdentity Leader { get; } = Session(1, "leader");
    private static SelectedSessionIdentity Member { get; } = Session(2, "member");
    private static SelectedSessionIdentity Observer { get; } = Session(3, "observer");
    private static SelectedSessionIdentity NoParty { get; } = Session(4, "no-party");

    private sealed record Publication(NotificationAudience Audience, object Notification);

    private sealed class RecordingNotificationBus : Dfo.Server.Testing.ProjectionEventService
    {
        public List<Publication> Publications { get; } = [];

        public override ValueTask<NotificationDelivery> ProjectAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
        {
            Publications.Add(new(audience, notification));
            return ValueTask.FromResult(new NotificationDelivery([]));
        }
    }

    private sealed class FailFirstRosterNotificationBus : Dfo.Server.Testing.ProjectionEventService
    {
        public int Attempts { get; private set; }

        public int Successes { get; private set; }

        public override ValueTask<NotificationDelivery> ProjectAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
        {
            Attempts++;
            var outcome = Attempts == 1
                ? NotificationOutcome.Failed
                : NotificationOutcome.Registered;
            if (outcome == NotificationOutcome.Registered)
                Successes++;
            return ValueTask.FromResult(new NotificationDelivery(
                [new NotificationAttempt(Observer.CharacterId, outcome)]));
        }
    }

    private sealed class StubParties(
        params (SelectedSessionIdentity Session, PartySnapshot Snapshot)[] snapshots)
        : IPartyService
    {
        public PartyMembershipSnapshot? FindByKey(PartyKey key) => null;
        private readonly PartySnapshot[] parties = snapshots
            .Select(value => value.Snapshot)
            .DistinctBy(value => (value.PartyId, value.Generation))
            .ToArray();

        public void UpdateDirectoryPresence(CharacterId characterId, PartyDirectoryScope? scope) { }

        public IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query) => parties;

        public PartySnapshot FindParty(PartyCharacterSnapshot character) =>
            parties.SingleOrDefault(snapshot => snapshot.Members.Any(
                member => member.CharacterId == character.CharacterId))
            ?? PartySnapshot.Virtual(character, new PartySettings(new byte[PartySettings.WireLength]));

        public PartyOperationResult CreateOrUpdateSettings(
            PartyCharacterSnapshot character,
            PartySettings settings,
            PartyTitle title) => throw new NotSupportedException();

        public PartyOperationResult RequestInvite(
            PartyCharacterSnapshot inviter,
            PartyCharacterSnapshot target,
            PartyInviteDisplayState display) => throw new NotSupportedException();

        public PartyOperationResult AcceptInvite(
            PartyCharacterSnapshot invitee,
            PartyCharacterSnapshot inviter) => throw new NotSupportedException();

        public PartyOperationResult TransferLeadership(
            CharacterId currentLeaderCharacterId,
            CharacterId nextLeaderCharacterId) => throw new NotSupportedException();

        public PartyOperationResult RemoveMember(
            CharacterId currentLeaderCharacterId,
            CharacterId targetCharacterId) => throw new NotSupportedException();

        public PartyOperationResult LeaveParty(
            CharacterId characterId) => throw new NotSupportedException();

        public PartyOperationResult CleanupCharacter(
            CharacterId characterId) => throw new NotSupportedException();

    }

    private sealed class MutableParties(
        SelectedSessionIdentity member,
        PartySnapshot current) : IPartyService
    {
        public PartyMembershipSnapshot? FindByKey(PartyKey key) => null;
        public int ReadCount { get; private set; }

        public PartySnapshot Current { get; set; } = current;

        public void UpdateDirectoryPresence(CharacterId characterId, PartyDirectoryScope? scope) { }

        public IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query)
        {
            ReadCount++;
            return [Current];
        }

        public PartySnapshot FindParty(PartyCharacterSnapshot character) =>
            character.CharacterId == member.CharacterId ? Current
                : PartySnapshot.Virtual(character, new PartySettings(new byte[PartySettings.WireLength]));

        public PartyOperationResult CreateOrUpdateSettings(
            PartyCharacterSnapshot character,
            PartySettings settings,
            PartyTitle title) => throw new NotSupportedException();

        public PartyOperationResult RequestInvite(
            PartyCharacterSnapshot inviter,
            PartyCharacterSnapshot target,
            PartyInviteDisplayState display) => throw new NotSupportedException();

        public PartyOperationResult AcceptInvite(
            PartyCharacterSnapshot invitee,
            PartyCharacterSnapshot inviter) => throw new NotSupportedException();

        public PartyOperationResult TransferLeadership(
            CharacterId currentLeaderCharacterId,
            CharacterId nextLeaderCharacterId) => throw new NotSupportedException();

        public PartyOperationResult RemoveMember(
            CharacterId currentLeaderCharacterId,
            CharacterId targetCharacterId) => throw new NotSupportedException();

        public PartyOperationResult LeaveParty(
            CharacterId characterId) => throw new NotSupportedException();

        public PartyOperationResult CleanupCharacter(
            CharacterId characterId) => throw new NotSupportedException();

    }

    private sealed class FixedPresence : ISessionPresenceDomainService
    {
        private readonly Dictionary<CharacterId, SessionPresence> values;

        public FixedPresence(params SelectedSessionIdentity[] sessions)
            : this(sessions.Select(session => (session, (byte)1, (byte)1)).ToArray())
        {
        }

        public FixedPresence(
            params (SelectedSessionIdentity Session, byte TownId, byte AreaId)[] sessions)
        {
            values = sessions.ToDictionary(
                value => value.Session.CharacterId,
                value => new SessionPresence(
                    value.Session.CharacterId,
                    value.Session.ListenerPort,
                    value.TownId,
                    value.AreaId,
                    true,
                    value.Session,
                    ChannelId: 11));
        }

        public void Admit(SessionPresence value) => values[value.CharacterId] = value;

        public bool Withdraw(CharacterId characterId) => values.Remove(characterId);

        public bool WithdrawExact(SelectedSessionIdentity session) =>
            values.TryGetValue(session.CharacterId, out var value)
            && value.Session == session
            && values.Remove(session.CharacterId);

        public bool RelocateExact(
            SelectedSessionIdentity session,
            byte townId,
            byte areaId,
            bool standingInTown,
            bool sharesTownPresence = true) => throw new NotSupportedException();

        public SessionPresence? Find(CharacterId characterId) =>
            values.GetValueOrDefault(characterId);

        public IReadOnlyList<SessionPresence> Query(SessionPresenceScope scope) =>
            values.Values
                .Where(value => value.CharacterId != scope.ExcludingCharacterId)
                .Where(value => scope.ChannelId is null || value.ChannelId == scope.ChannelId)
                .Where(value => scope.TownId is null || value.TownId == scope.TownId)
                .Where(value => scope.AreaId is null || value.AreaId == scope.AreaId)
                .Where(value => !scope.Flags.HasFlag(SessionPresenceScopeFlags.StandingInTown)
                    || value.StandingInTown)
                .Where(value => !scope.Flags.HasFlag(SessionPresenceScopeFlags.SharesTownPresence)
                    || value.SharesTownPresence)
                .OrderBy(value => value.CharacterId.Value)
                .ToArray();
    }

    private sealed class FixedSelectedSessions : ISelectedSessionDomainService
    {
        // Test double: the current selection counts as being in the world, and
        // remembers which sessions were fenced.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            Dfo.Server.Data.World.SelectedSessionIdentity, bool> testDoubleFences = new();
        void Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.EnterWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) { }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.Fence(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
            && testDoubleFences.TryAdd(session, true) | true;
        Dfo.Server.Data.World.SelectedSessionStanding? Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.StandingOf(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
                ? new(Dfo.Server.Data.World.SelectedSessionPhase.InWorld, testDoubleFences.ContainsKey(session), false)
                : null;
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsInWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session)
                is { Phase: Dfo.Server.Data.World.SelectedSessionPhase.InWorld };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsSelected(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session) is { ConnectionRevoked: false };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Characters.CharacterId characterId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByCharacter(characterId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            ushort wireUserId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByWireUserId(wireUserId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Networking.NetworkConnectionId connectionId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByConnection(connectionId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }

        private readonly SelectedSessionIdentity[] values = [Leader, Member, Observer, NoParty];

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => null;
        public bool ReleaseExact(SelectedSessionIdentity session) => false;
        public SelectedSessionIdentity? Find(AccountId accountId) =>
            values.SingleOrDefault(value => value.AccountId == accountId);
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) =>
            values.SingleOrDefault(value => value.CharacterId == characterId);
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) =>
            values.SingleOrDefault(value => value.WireUserId == wireUserId);
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) =>
            values.SingleOrDefault(value => value.ConnectionId == connectionId);
    }
}
