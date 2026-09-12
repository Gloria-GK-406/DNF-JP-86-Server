using System.Collections.Immutable;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Party;

namespace Dfo.Server.Protocol.Tests.Game.Party;

public sealed class PartyNotificationTests
{
    [Fact]
    public void Invite_and_acceptance_bodies_match_the_party_shapes()
    {
        Assert.Equal(
            Hex("01000040000000BC0000000000"),
            PartyInviteRequestEncoder.EncodeBody(new PartyInviteRequestMessage(1, Profile())));
        Assert.Equal(
            Hex("02000001000000"),
            PartyInviteAcceptedEncoder.EncodeBody(new PartyInviteAcceptedMessage(2, 1)));
    }

    [Fact]
    public void Realtime_body_preserves_member_order_health_and_slots()
    {
        Assert.Equal(
            Hex("02010049000002002D0001"),
            PartyRealtimeEncoder.EncodeBody(new PartyRealtimeMessage(Snapshot())));
    }

    [Fact]
    public void Endpoint_body_uses_reported_octets_network_order_port_and_safe_unknown_fallback()
    {
        var body = PartyEndpointsEncoder.EncodeBody(new PartyEndpointsMessage(Snapshot()));

        Assert.Equal(45, body.Length);
        Assert.Equal(
            Hex(
                "02"
                + "0100AC11D001AC11D00179000100000005C005000000"
                + "02007F0000017F00000127100200000000DC05000000"),
            body);
    }

    [Fact]
    public void Full_roster_is_one_a21_block_with_eight_slots_and_no_extra_records()
    {
        var body = PartyRosterEncoder.EncodeBody(PartyRosterMessage.Refresh(Snapshot()));

        Assert.Equal(65, body.Length);
        Assert.Equal(
            Hex(
                "0100341200"
                + "0000000000000400000000050000FFFF"
                + "0100000000"
                + "0200000000"
                + "FFFF000000FFFF000000FFFF000000"
                + "FFFF000000FFFF000000FFFF000000"
                + "00000000"),
            body);
        Assert.Equal((ushort)1, BitConverter.ToUInt16(body, 21));
        Assert.Equal((byte)0, body[62]);
        Assert.Equal((byte)0, body[64]);
    }

    [Fact]
    public void Clear_roster_uses_the_known_type_three_party_block()
    {
        Assert.Equal(
            Hex("0100341203"),
            PartyRosterEncoder.EncodeBody(PartyRosterMessage.Clear(0x1234)));
    }

    [Fact]
    public async Task Formation_is_published_in_cross_recipient_phases()
    {
        var bus = new RecordingNotificationBus();
        var delivery = new PartyNotificationDelivery(bus);
        var snapshot = Snapshot();
        var plan = new PartyNotificationPlan(
            [],
            snapshot,
            Acceptance: new PartyAcceptanceNotification(Inviter, Invitee));

        await delivery.DeliverAsync(plan, CancellationToken.None);

        Assert.Collection(
            bus.Publications,
            value => AssertPublication<PartyRealtimeChanged>(value, 1, 2),
            value => AssertPublication<PartyInviteAccepted>(value, 1),
            value => AssertPublication<PartyEndpointsChanged>(value, 1, 2),
            value => AssertPublication<PartyRosterChanged>(value, 1, 2),
            value => AssertPublication<PartyRealtimeChanged>(value, 1, 2));
        Assert.Same(
            snapshot,
            Assert.IsType<PartyRealtimeChanged>(bus.Publications[0].Notification).Snapshot);
        Assert.Same(
            snapshot,
            Assert.IsType<PartyEndpointsChanged>(bus.Publications[2].Notification).Snapshot);
        Assert.Same(
            snapshot,
            Assert.IsType<PartyRosterChanged>(bus.Publications[3].Notification).Snapshot);
        Assert.Same(
            snapshot,
            Assert.IsType<PartyRealtimeChanged>(bus.Publications[4].Notification).Snapshot);
    }

    [Fact]
    public async Task Invite_is_published_only_to_its_exact_target()
    {
        var bus = new RecordingNotificationBus();
        var delivery = new PartyNotificationDelivery(bus);

        await delivery.DeliverAsync(new PartyNotificationPlan(
            [],
            Invitation: new PartyInviteNotification(Invitee, Inviter, Profile())),
            CancellationToken.None);

        var publication = Assert.Single(bus.Publications);
        AssertPublication<PartyInviteIssued>(publication, 2);
    }

    [Fact]
    public async Task Leaving_member_clear_and_survivor_refresh_use_disjoint_audiences()
    {
        var bus = new RecordingNotificationBus();
        var delivery = new PartyNotificationDelivery(bus);
        var survivor = new PartySnapshot(
            0x1234,
            7,
            4,
            1,
            [new(Inviter, 0, Endpoint(), 73)]);

        await delivery.DeliverAsync(new PartyNotificationPlan(
            [Invitee],
            survivor,
            ClearPartyId: 0x1234,
            ClearPartyGeneration: survivor.Generation,
            ClearPartyRevision: survivor.Revision), CancellationToken.None);

        Assert.Collection(
            bus.Publications,
            value =>
            {
                AssertPublication<PartyRosterChanged>(value, 2);
                Assert.True(Assert.IsType<PartyRosterChanged>(value.Notification).ClearsParty);
            },
            value => AssertPublication<PartyRealtimeChanged>(value, 1),
            value => AssertPublication<PartyEndpointsChanged>(value, 1),
            value => AssertPublication<PartyRosterChanged>(value, 1));
    }

    [Fact]
    public async Task Revisions_of_one_party_generation_are_delivered_as_contiguous_phases()
    {
        var bus = new BlockingNotificationBus();
        var delivery = new PartyNotificationDelivery(bus);
        var older = Snapshot() with { Revision = 3 };
        var newer = Snapshot() with { Revision = 4 };

        var olderDelivery = delivery.DeliverAsync(
            new PartyNotificationPlan([], older), CancellationToken.None).AsTask();
        await bus.FirstPublication.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var newerDelivery = delivery.DeliverAsync(
            new PartyNotificationPlan([], newer), CancellationToken.None).AsTask();

        Assert.Single(bus.Publications);
        Assert.False(newerDelivery.IsCompleted);
        bus.ReleaseFirst.TrySetResult();
        await Task.WhenAll(olderDelivery, newerDelivery).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            [3L, 3L, 3L, 4L, 4L, 4L],
            bus.Publications.Select(value => SnapshotRevision(value.Notification)));
    }

    [Fact]
    public async Task An_older_revision_arriving_after_a_newer_one_is_suppressed()
    {
        var bus = new RecordingNotificationBus();
        var delivery = new PartyNotificationDelivery(bus);
        var older = Snapshot() with { Revision = 3 };
        var newer = Snapshot() with { Revision = 4 };

        await delivery.DeliverAsync(new PartyNotificationPlan([], newer), CancellationToken.None);
        await delivery.DeliverAsync(new PartyNotificationPlan([], older), CancellationToken.None);

        Assert.Equal(3, bus.Publications.Count);
        Assert.All(
            bus.Publications,
            value => Assert.Equal(4, SnapshotRevision(value.Notification)));
    }

    [Fact]
    public async Task Notification_handlers_project_only_to_the_exact_snapshot_sessions()
    {
        var sessions = new SessionDirectory(Inviter, Invitee);
        var snapshot = Snapshot();
        var inviterRecipient = new NotificationRecipient(
            Inviter.CharacterId, Inviter.ConnectionId);
        var inviteeRecipient = new NotificationRecipient(
            Invitee.CharacterId, Invitee.ConnectionId);

        Assert.Single(await new PartyInviteIssuedHandler(sessions).ComposeAsync(
            inviteeRecipient,
            new PartyInviteIssued(new PartyInviteNotification(Invitee, Inviter, Profile())),
            CancellationToken.None));
        Assert.Single(await new PartyInviteAcceptedHandler(sessions).ComposeAsync(
            inviterRecipient,
            new PartyInviteAccepted(new PartyAcceptanceNotification(Inviter, Invitee)),
            CancellationToken.None));
        Assert.Single(await new PartyRealtimeChangedHandler(sessions).ComposeAsync(
            inviteeRecipient, new PartyRealtimeChanged(snapshot), CancellationToken.None));
        Assert.Single(await new PartyEndpointsChangedHandler(sessions).ComposeAsync(
            inviteeRecipient, new PartyEndpointsChanged(snapshot), CancellationToken.None));
        Assert.Single(await new PartyRosterChangedHandler(sessions).ComposeAsync(
            inviteeRecipient, PartyRosterChanged.Refresh(snapshot), CancellationToken.None));

        sessions.Select(Inviter with
        {
            ConnectionId = new NetworkConnectionId("new-inviter"),
            ConnectionGeneration = 2,
        });
        sessions.Select(Invitee with
        {
            ConnectionId = new NetworkConnectionId("new-invitee"),
            ConnectionGeneration = 2,
        });

        Assert.Empty(await new PartyInviteIssuedHandler(sessions).ComposeAsync(
            inviteeRecipient,
            new PartyInviteIssued(new PartyInviteNotification(Invitee, Inviter, Profile())),
            CancellationToken.None));
        Assert.Empty(await new PartyInviteAcceptedHandler(sessions).ComposeAsync(
            inviterRecipient,
            new PartyInviteAccepted(new PartyAcceptanceNotification(Inviter, Invitee)),
            CancellationToken.None));
        Assert.Empty(await new PartyRealtimeChangedHandler(sessions).ComposeAsync(
            inviteeRecipient, new PartyRealtimeChanged(snapshot), CancellationToken.None));
        Assert.Empty(await new PartyEndpointsChangedHandler(sessions).ComposeAsync(
            inviteeRecipient, new PartyEndpointsChanged(snapshot), CancellationToken.None));
        Assert.Empty(await new PartyRosterChangedHandler(sessions).ComposeAsync(
            inviteeRecipient, PartyRosterChanged.Refresh(snapshot), CancellationToken.None));
    }

    private static void AssertPublication<TNotification>(
        Publication publication,
        params int[] characterIds)
    {
        Assert.IsType<TNotification>(publication.Notification);
        var audience = Assert.IsType<NotificationAudience.TheseCharacters>(publication.Audience);
        Assert.Equal(characterIds, audience.CharacterIds.Select(value => value.Value));
    }

    private static long SnapshotRevision(object notification) => notification switch
    {
        PartyRealtimeChanged value => value.Snapshot.Revision,
        PartyEndpointsChanged value => value.Snapshot.Revision,
        PartyRosterChanged { Snapshot: { } snapshot } => snapshot.Revision,
        _ => throw new InvalidDataException(
            $"Notification '{notification.GetType().Name}' has no party snapshot."),
    };

    private static PartySnapshot Snapshot() => new(
        0x1234,
        7,
        3,
        1,
        ImmutableArray.Create(
            new PartyMemberSnapshot(Inviter, 0, Endpoint(), 73),
            new PartyMemberSnapshot(Invitee, 1, null, 45)));

    private static PartyInviteDisplayState Profile() => new(
        ReviveCoinCount: 64,
        RemainingFatigue: 188,
        Stamina: 0,
        AntiMagic: 0);

    private static PartyEndpoint Endpoint() => new(
        5,
        new(172, 17, 208, 1),
        new(172, 17, 208, 1),
        30976,
        1472);

    private static SelectedSessionIdentity Inviter { get; } = SelectedSessionIdentity.Create(
        new AccountId(101), new CharacterId(1), new NetworkConnectionId("inviter"), 1, 10011);

    private static SelectedSessionIdentity Invitee { get; } = SelectedSessionIdentity.Create(
        new AccountId(202), new CharacterId(2), new NetworkConnectionId("invitee"), 1, 10011);

    private static byte[] Hex(string value) => Convert.FromHexString(value);

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

    private sealed class BlockingNotificationBus : INotificationBus
    {
        public List<Publication> Publications { get; } = [];

        public TaskCompletionSource FirstPublication { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirst { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<NotificationDelivery> PublishAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
            where TNotification : class, INotification<TNotification>
        {
            Publications.Add(new(audience, notification));
            if (Publications.Count == 1)
            {
                FirstPublication.TrySetResult();
                await ReleaseFirst.Task.WaitAsync(cancellationToken);
            }
            return new NotificationDelivery([]);
        }
    }

    private sealed class SessionDirectory(params SelectedSessionIdentity[] initial)
        : ISelectedSessionDomainService
    {
        private readonly Dictionary<AccountId, SelectedSessionIdentity> sessions =
            initial.ToDictionary(value => value.AccountId);

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session)
        {
            sessions.TryGetValue(session.AccountId, out var displaced);
            sessions[session.AccountId] = session;
            return displaced;
        }

        public bool ReleaseExact(SelectedSessionIdentity session) =>
            sessions.TryGetValue(session.AccountId, out var current)
            && current == session
            && sessions.Remove(session.AccountId);

        public SelectedSessionIdentity? Find(AccountId accountId) =>
            sessions.GetValueOrDefault(accountId);

        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) =>
            sessions.Values.SingleOrDefault(value => value.CharacterId == characterId);

        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) =>
            sessions.Values.SingleOrDefault(value => value.WireUserId == wireUserId);

        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) =>
            sessions.Values.SingleOrDefault(value => value.ConnectionId == connectionId);
    }
}
