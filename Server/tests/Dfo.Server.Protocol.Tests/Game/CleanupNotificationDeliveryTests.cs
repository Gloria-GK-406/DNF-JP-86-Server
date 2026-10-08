using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// A departure has two audiences, and they leave by different routes now.
/// </summary>
/// <remarks>
/// The owner's notices go down the connection the departure is happening on; the peers are a
/// fan-out named in exact selected sessions. Six places deliver one of these plans -- four inside a
/// catch (OnlineUserConnectionObserver.cs:102, SelectCharacterHandler.cs:136, :156, :162) -- so
/// the rule that the owner hears first lives here rather than in six hand-written sequences.
/// </remarks>
public sealed class CleanupNotificationDeliveryTests
{
    private static readonly NetworkConnectionId Owner = new("Game:owner");

    private static readonly SelectedSessionCleanupNotice StoreClosed =
        new(SelectedSessionCleanupNoticeKind.ExpertStoreClosed, 42);

    private static readonly SelectedSessionCleanupNotice Departed =
        new(SelectedSessionCleanupNoticeKind.TownDeparture, 42);

    [Fact]
    public async Task The_owner_is_told_before_the_people_around_them()
    {
        var order = new List<string>();
        var sender = new RecordingSender(order);
        var notifications = new RecordingNotifications(order);

        await new CleanupNotificationDelivery(sender, notifications).DeliverAsync(
            new SelectedSessionCleanupNotificationPlan(
                Owner, [StoreClosed], [Peer(9)], [Departed]),
            CancellationToken.None);

        Assert.Equal(["owner", "peers"], order);
    }

    [Fact]
    public async Task Existing_owner_and_town_notices_finish_before_party_projection()
    {
        var order = new List<string>();
        var sender = new RecordingSender(order);
        var notifications = new RecordingNotifications(order);
        var partyMember = SelectedSessionIdentity.Create(
            new AccountId(9),
            new CharacterId(9),
            new NetworkConnectionId("Game:party-peer"),
            1,
            10011);
        var townObserver = SelectedSessionIdentity.Create(
            new AccountId(10),
            new CharacterId(10),
            new NetworkConnectionId("Game:party-observer"),
            1,
            10011);
        var partyDelivery = new PartyNotificationDelivery(
            notifications,
            new PartyProjectionStreamRegistry(),
            new FixedSelectedSessions(partyMember, townObserver),
            new FixedPresence(partyMember, townObserver));

        await new CleanupNotificationDelivery(sender, notifications, partyDelivery).DeliverAsync(
            new SelectedSessionCleanupNotificationPlan(
                Owner,
                [StoreClosed],
                [Peer(9)],
                [Departed],
                new PartyNotificationPlan(
                    [partyMember.CharacterId],
                    ClearPartyId: 7,
                    ClearPartyGeneration: 11,
                    ClearPartyRevision: 3,
                    DirectoryChange: new PartyDirectoryChange(
                        new PartyDirectoryScope(11, 1, 1),
                        null)),
                new PartyNotificationRecipientPlan(
                    [partyMember],
                    [new PartyDirectoryRecipientGroup(
                        new PartyDirectoryScope(11, 1, 1),
                        [partyMember, townObserver])])),
            CancellationToken.None);

        Assert.Equal(["owner", "peers", "party", "party"], order);
        Assert.IsType<SessionCleanupAnnounced>(notifications.Published[0].Notification);
        Assert.IsType<PartyRosterChanged>(notifications.Published[1].Notification);
        Assert.IsType<PartyRosterChanged>(notifications.Published[2].Notification);
    }

    /// <summary>What each half becomes, and that town peers keep their sampled sessions.</summary>
    [Fact]
    public async Task Each_half_becomes_the_frames_it_always_did()
    {
        var sender = new RecordingSender([]);
        var notifications = new RecordingNotifications([]);
        var peer = Peer(9);

        await new CleanupNotificationDelivery(sender, notifications).DeliverAsync(
            new SelectedSessionCleanupNotificationPlan(
                Owner, [StoreClosed], [peer], [StoreClosed, Departed]),
            CancellationToken.None);

        var direct = Assert.Single(sender.Deliveries);
        Assert.Equal(Owner, direct.ConnectionId);
        Assert.Equal(
            "ExpertStoreClosedMessage",
            Assert.Single(direct.Delivery.RequiredDirectMessages).GetType().Name);
        Assert.Empty(direct.Delivery.BestEffortTargets);

        var (audience, notification) = Assert.Single(notifications.Published);
        Assert.Equal(
            [peer], Assert.IsType<NotificationAudience.TheseSessions>(audience).Sessions);
        Assert.Equal(
            [SelectedSessionCleanupNoticeKind.ExpertStoreClosed,
                SelectedSessionCleanupNoticeKind.TownDeparture],
            Assert.IsType<SessionCleanupAnnounced>(notification).Notices
                .Select(value => value.Kind));
    }

    /// <summary>What the peers' half actually becomes on the wire.</summary>
    /// <remarks>
    /// The case above stops at the notification, and that is one seam short: before the migration
    /// a case asserted the peers' frame by name, and moving the fan-out left nothing holding that.
    /// A quick review found it by mapping a departure to the wrong message type and watching the
    /// whole suite stay green. This is the case that goes red instead.
    /// </remarks>
    [Fact]
    public async Task The_peers_notices_become_the_frames_they_always_did()
    {
        var messages = await new SessionCleanupAnnouncedHandler().ComposeAsync(
            new NotificationRecipient(new CharacterId(9), new NetworkConnectionId("Game:peer")),
            new SessionCleanupAnnounced([StoreClosed, Departed]),
            CancellationToken.None);

        Assert.Equal(
            ["ExpertStoreClosedMessage", "TownDepartureMessage"],
            messages.Select(message => message.GetType().Name));
    }

    /// <summary>A notice kind nobody mapped names itself in what it throws.</summary>
    /// <remarks>
    /// It names itself, and that is all this says. Where the throw goes is not the same for both
    /// halves any more: the owner's notices are still mapped at the call site, so a gap there
    /// reaches whoever asked for the delivery. The peers' are mapped inside the handler, which
    /// runs under the notification bus's per-recipient guard -- so a gap there becomes one
    /// recipient's `Failed`, is not retried at this notification's allowance of zero, and is read
    /// by nobody, because no delivery site reads the summary. It is a message with nowhere to
    /// arrive. See the difference list in this Task's record, entry C-D8.
    /// </remarks>
    [Fact]
    public async Task A_notice_kind_nobody_mapped_is_named()
    {
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new SessionCleanupAnnouncedHandler().ComposeAsync(
                new NotificationRecipient(new CharacterId(9), new NetworkConnectionId("Game:peer")),
                new SessionCleanupAnnounced([new SelectedSessionCleanupNotice((SelectedSessionCleanupNoticeKind)99, 42)]),
                CancellationToken.None));

        Assert.Contains("99", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A half with nothing in it is not delivered at all.</summary>
    /// <remarks>
    /// Most cleanups have nothing to announce -- a stale session that lost its registration, an
    /// area with nobody else in it. Sending an empty batch used to be harmless because the
    /// transport dropped it; publishing an empty audience would still resolve and log one, so the
    /// emptiness is checked here instead of downstream.
    /// </remarks>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task An_empty_half_is_not_delivered(bool owner, bool peers)
    {
        var sender = new RecordingSender([]);
        var notifications = new RecordingNotifications([]);

        await new CleanupNotificationDelivery(sender, notifications).DeliverAsync(
            new SelectedSessionCleanupNotificationPlan(
                Owner,
                owner ? [StoreClosed] : [],
                peers ? [Peer(9)] : [],
                peers ? [Departed] : []),
            CancellationToken.None);

        Assert.Equal(owner ? 1 : 0, sender.Deliveries.Count);
        Assert.Equal(peers ? 1 : 0, notifications.Published.Count);
    }

    private static SelectedSessionIdentity Peer(int characterId) =>
        SelectedSessionIdentity.Create(
            new AccountId(characterId),
            new CharacterId(characterId),
            new NetworkConnectionId($"Game:peer-{characterId}"),
            1,
            10011);

    private sealed class FixedSelectedSessions(params SelectedSessionIdentity[] values)
        : ISelectedSessionDomainService
    {
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

    private sealed class FixedPresence(params SelectedSessionIdentity[] sessions)
        : ISessionPresenceDomainService
    {
        private readonly Dictionary<CharacterId, SessionPresence> values = sessions.ToDictionary(
            session => session.CharacterId,
            session => new SessionPresence(
                session.CharacterId,
                session.ListenerPort,
                1,
                1,
                true,
                session,
                ChannelId: 11));

        public void Admit(SessionPresence presence) => values[presence.CharacterId] = presence;
        public bool Withdraw(CharacterId characterId) => values.Remove(characterId);
        public bool WithdrawExact(SelectedSessionIdentity session) => false;
        public bool RelocateExact(
            SelectedSessionIdentity session,
            byte townId,
            byte areaId,
            bool standingInTown,
            bool sharesTownPresence = true) => false;
        public SessionPresence? Find(CharacterId characterId) => values.GetValueOrDefault(characterId);
        public IReadOnlyList<SessionPresence> Query(SessionPresenceScope scope) =>
            values.Values.ToArray();
    }

    private sealed class RecordingSender(List<string> order) : IGameMessageSender
    {
        public List<(NetworkConnectionId ConnectionId, GameExplicitMessageDelivery Delivery)>
            Deliveries { get; } = [];

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken token) =>
            throw new NotSupportedException();

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken token) =>
            throw new NotSupportedException();

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            order.Add("owner");
            Deliveries.Add((requiredDirectConnectionId, delivery));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingNotifications(List<string> order) : Dfo.Server.Testing.ProjectionEventService
    {
        public List<(NotificationAudience Audience, object Notification)> Published { get; } = [];

        public override ValueTask<NotificationDelivery> ProjectAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
        {
            order.Add(notification is PartyRosterChanged ? "party" : "peers");
            Published.Add((audience, notification));
            return ValueTask.FromResult(new NotificationDelivery([]));
        }
    }
}
