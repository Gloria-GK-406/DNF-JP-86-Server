using System.Collections.Immutable;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Party;

namespace Dfo.Server.Protocol.Tests.Game.Party;

public sealed class PartyTownProjectionDeliveryTests
{
    [Fact]
    public async Task RepresentedMembersOfOnePartyProduceOneRefreshForExactObservers()
    {
        var snapshot = Snapshot(revision: 3);
        var parties = new StubParties(
            (Leader, snapshot),
            (Member, snapshot));
        var bus = new RecordingNotificationBus();
        var delivery = new PartyTownProjectionDelivery(
            parties,
            bus,
            new PartyProjectionStreamRegistry());

        await delivery.RefreshObservedPartiesAsync(
            [Observer, Leader, Observer],
            [Member, NoParty, Leader, Member],
            CancellationToken.None);

        var publication = Assert.Single(bus.Publications);
        Assert.Equal(
            new[] { Observer },
            Assert.IsType<NotificationAudience.TheseSessions>(publication.Audience).Sessions);
        var roster = Assert.IsType<PartyRosterChanged>(publication.Notification);
        Assert.Same(snapshot, roster.Snapshot);
        Assert.False(roster.ClearsParty);
    }

    [Fact]
    public async Task ACommittedRevisionStillRefreshesANewObserverButAnOlderSnapshotDoesNot()
    {
        var streams = new PartyProjectionStreamRegistry();
        streams.GetOrAdd(7).LatestRevision = 3;
        var currentBus = new RecordingNotificationBus();
        var current = new PartyTownProjectionDelivery(
            new StubParties((Leader, Snapshot(revision: 3))),
            currentBus,
            streams);

        await current.RefreshObservedPartiesAsync(
            [Observer], [Leader], CancellationToken.None);

        Assert.Single(currentBus.Publications);

        streams.GetOrAdd(7).LatestRevision = 4;
        var staleBus = new RecordingNotificationBus();
        var stale = new PartyTownProjectionDelivery(
            new StubParties((Leader, Snapshot(revision: 3))),
            staleBus,
            streams);

        await stale.RefreshObservedPartiesAsync(
            [Observer], [Leader], CancellationToken.None);

        Assert.Empty(staleBus.Publications);
    }

    [Fact]
    public async Task ReadRepairWaitsForTheMutationGateAndRequeriesCurrentPartyState()
    {
        var streams = new PartyProjectionStreamRegistry();
        var stream = streams.GetOrAdd(7);
        await stream.Gate.WaitAsync(CancellationToken.None);
        var parties = new MutableParties(Leader, Snapshot(revision: 3));
        var bus = new RecordingNotificationBus();
        var delivery = new PartyTownProjectionDelivery(parties, bus, streams);

        try
        {
            var pending = delivery.RefreshObservedPartiesAsync(
                [Observer], [Leader], CancellationToken.None).AsTask();
            Assert.Equal(1, parties.ReadCount);
            Assert.False(pending.IsCompleted);

            parties.Current = Snapshot(revision: 4);
            stream.Gate.Release();
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            if (stream.Gate.CurrentCount == 0)
                stream.Gate.Release();
        }

        var publication = Assert.Single(bus.Publications);
        Assert.Equal(
            4,
            Assert.IsType<PartyRosterChanged>(publication.Notification).Snapshot!.Revision);
        Assert.Equal(2, parties.ReadCount);
    }

    [Fact]
    public async Task ReadRepairRetriesATransientRosterFailureFromOneTopLevelCall()
    {
        var snapshot = Snapshot(revision: 3);
        var bus = new FailFirstRosterNotificationBus();
        var delivery = new PartyTownProjectionDelivery(
            new StubParties((Leader, snapshot)),
            bus,
            new PartyProjectionStreamRegistry());

        await delivery.RefreshObservedPartiesAsync(
            [Observer], [Leader], CancellationToken.None);

        Assert.Equal(2, bus.Attempts);
        Assert.Equal(1, bus.Successes);
    }

    [Fact]
    public async Task NewGenerationMutationCannotPassABlockedOldGenerationReadRepair()
    {
        var oldSnapshot = Snapshot(revision: 3, generation: 7);
        var newSnapshot = Snapshot(revision: 1, generation: 8);
        var streams = new PartyProjectionStreamRegistry();
        var parties = new MutableParties(Leader, oldSnapshot);
        var bus = new BlockingFirstRosterNotificationBus();
        var readRepair = new PartyTownProjectionDelivery(parties, bus, streams);
        var mutation = new PartyNotificationDelivery(bus, streams);

        var oldDelivery = readRepair.RefreshObservedPartiesAsync(
            [Observer], [Leader], CancellationToken.None).AsTask();
        await bus.FirstRosterEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        parties.Current = newSnapshot;

        var newDelivery = mutation.DeliverAsync(
            new PartyNotificationPlan([], newSnapshot, TownObservers: [Observer]),
            CancellationToken.None).AsTask();
        await Task.Yield();

        Assert.False(newDelivery.IsCompleted);
        bus.ReleaseFirstRoster.TrySetResult();
        await Task.WhenAll(oldDelivery, newDelivery).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            [7L, 8L, 8L],
            bus.Rosters.Select(value => value.Snapshot!.Generation));
    }

    private static PartySnapshot Snapshot(long revision, long generation = 7) => new(
        0x1234,
        generation,
        revision,
        Leader.WireUserId,
        new PartySettings([0, 0, 4, 0, 0, 0, 0, 5, 0, (byte)PartyCategory.Normal, 0, 0]),
        PartyTitle.Empty,
        ImmutableArray.Create(
            new PartyMemberSnapshot(Leader, 0, null, 100),
            new PartyMemberSnapshot(Member, 1, null, 100)));

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

    private sealed class RecordingNotificationBus : INotificationBus
    {
        public List<Publication> Publications { get; } = [];

        public ValueTask<NotificationDelivery> PublishAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
            where TNotification : class, INotification<TNotification>
        {
            Publications.Add(new(audience, notification));
            return ValueTask.FromResult(new NotificationDelivery([]));
        }
    }

    private sealed class FailFirstRosterNotificationBus : INotificationBus
    {
        public int Attempts { get; private set; }

        public int Successes { get; private set; }

        public ValueTask<NotificationDelivery> PublishAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
            where TNotification : class, INotification<TNotification>
        {
            Attempts++;
            var outcome = Attempts == 1
                ? NotificationOutcome.Failed
                : NotificationOutcome.Delivered;
            if (outcome == NotificationOutcome.Delivered)
                Successes++;
            return ValueTask.FromResult(new NotificationDelivery(
                [new NotificationAttempt(Observer.CharacterId, outcome)]));
        }
    }

    private sealed class BlockingFirstRosterNotificationBus : INotificationBus
    {
        private int rosterCount;

        public TaskCompletionSource FirstRosterEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstRoster { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public List<PartyRosterChanged> Rosters { get; } = [];

        public async ValueTask<NotificationDelivery> PublishAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
            where TNotification : class, INotification<TNotification>
        {
            if (notification is PartyRosterChanged roster)
            {
                Rosters.Add(roster);
                if (Interlocked.Increment(ref rosterCount) == 1)
                {
                    FirstRosterEntered.TrySetResult();
                    await ReleaseFirstRoster.Task.WaitAsync(cancellationToken);
                }
            }

            return new NotificationDelivery([]);
        }
    }

    private sealed class StubParties(
        params (SelectedSessionIdentity Session, PartySnapshot Snapshot)[] snapshots)
        : IPartyDomainService
    {
        private readonly Dictionary<SelectedSessionIdentity, PartySnapshot> parties =
            snapshots.ToDictionary(value => value.Session, value => value.Snapshot);

        public PartySnapshot? FindParty(SelectedSessionIdentity session) =>
            parties.GetValueOrDefault(session);

        public PartyOperationResult CreateOrUpdateSettingsExact(
            SelectedSessionIdentity session,
            PartySettings settings,
            PartyTitle title) => throw new NotSupportedException();

        public PartyOperationResult UpdateEndpoint(
            SelectedSessionIdentity session,
            PartyEndpoint endpoint) => throw new NotSupportedException();

        public PartyOperationResult RequestInvite(
            SelectedSessionIdentity inviter,
            ushort targetWireUserId,
            PartyInviteDisplayState display) => throw new NotSupportedException();

        public PartyOperationResult AcceptInvite(
            SelectedSessionIdentity invitee,
            ushort inviterWireUserId) => throw new NotSupportedException();

        public PartyOperationResult LeavePartyExact(
            SelectedSessionIdentity session) => throw new NotSupportedException();

        public PartyOperationResult CleanupSessionExact(
            SelectedSessionIdentity session) => throw new NotSupportedException();

        public PartyEndpoint? FindEndpoint(
            SelectedSessionIdentity session) => throw new NotSupportedException();
    }

    private sealed class MutableParties(
        SelectedSessionIdentity member,
        PartySnapshot current) : IPartyDomainService
    {
        public int ReadCount { get; private set; }

        public PartySnapshot Current { get; set; } = current;

        public PartySnapshot? FindParty(SelectedSessionIdentity session)
        {
            ReadCount++;
            return session == member ? Current : null;
        }

        public PartyOperationResult CreateOrUpdateSettingsExact(
            SelectedSessionIdentity session,
            PartySettings settings,
            PartyTitle title) => throw new NotSupportedException();

        public PartyOperationResult UpdateEndpoint(
            SelectedSessionIdentity session,
            PartyEndpoint endpoint) => throw new NotSupportedException();

        public PartyOperationResult RequestInvite(
            SelectedSessionIdentity inviter,
            ushort targetWireUserId,
            PartyInviteDisplayState display) => throw new NotSupportedException();

        public PartyOperationResult AcceptInvite(
            SelectedSessionIdentity invitee,
            ushort inviterWireUserId) => throw new NotSupportedException();

        public PartyOperationResult LeavePartyExact(
            SelectedSessionIdentity session) => throw new NotSupportedException();

        public PartyOperationResult CleanupSessionExact(
            SelectedSessionIdentity session) => throw new NotSupportedException();

        public PartyEndpoint? FindEndpoint(
            SelectedSessionIdentity session) => throw new NotSupportedException();
    }
}
