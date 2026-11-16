using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
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
using Dfo.Server.Protocol.Tests.Outbox;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// Cleanup stages preserve their registration and delivery order in the real outbox.
/// </summary>
/// <remarks>
/// The delivery runs as a job against the real outbox (T-006): the cases read the entries it registered,
/// in one ordered group (XO-5), the owner's first and Required (DR-10).
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
        await using var outbound = Outbound(Peer(9));

        await outbound.RunAsync(async () => await Delivery(outbound).DeliverAsync(
            new SelectedSessionCleanupNotificationPlan(
                Owner, [StoreClosed], [Peer(9)], [Departed]),
            CancellationToken.None));

        Assert.Equal(["owner", "peers"], Order(outbound));
        var entries = outbound.Recorder.Entries;
        Assert.Equal([1, 2], entries.Select(entry => entry.Order!.Value.Sequence));
        Assert.Same(entries[0].Order!.Value.Group, entries[1].Order!.Value.Group);
        Assert.Equal([OutboundDelivery.Required, OutboundDelivery.BestEffort], entries.Select(entry => entry.Delivery));
    }

    [Fact]
    public async Task Cleanup_stages_register_owner_town_party_conversation_then_friends()
    {
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
        await using var outbound = Outbound(Peer(9), partyMember, townObserver);
        var notifications = outbound.Projections;
        var owner = SelectedSessionIdentity.Create(new AccountId(42), new CharacterId(42), Owner, 1, 10011);
        var conversation = new Dfo.Server.Data.Chat.ChatConversationDeparture(new(5),
            new(owner, "a"u8.ToArray()), new(townObserver, "b"u8.ToArray()));
        var friendDeparture = new SelectedSessionFriendDeparture(owner.CharacterId, "a"u8.ToArray(), [townObserver.CharacterId]);

        await outbound.RunAsync(async () =>
        await ProtocolAlgorithmFixture.Create<CleanupNotificationDelivery>(outbound.Game, notifications,
            new PartyProjectionStreamRegistry(), new FixedPresence(partyMember, townObserver), outbound.Ordering).DeliverAsync(
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
                        [partyMember, townObserver])]))
            {
                ConversationDepartures = [conversation],
                FriendDeparture = friendDeparture,
            },
            CancellationToken.None));

        Assert.Equal(["owner", "peers", "party", "party", "peers"], Order(outbound));
        var published = notifications.Published.ToArray();
        Assert.Equal(5, published.Length);
        Assert.IsType<SessionCleanupAnnounced>(published[0].Notification);
        Assert.IsType<PartyRosterChanged>(published[1].Notification);
        Assert.IsType<PartyRosterChanged>(published[2].Notification);
        Assert.Same(conversation, Assert.IsType<ChatConversationLeft>(published[3].Notification).Departure);
        Assert.Equal([townObserver], Assert.IsType<NotificationAudience.TheseSessions>(published[3].Audience).Sessions);
        Assert.IsType<FriendWentOffline>(published[4].Notification);
        Assert.Equal([townObserver.CharacterId], Assert.IsType<NotificationAudience.TheseCharacters>(published[4].Audience).CharacterIds);
        Assert.Equal([1, 2, 3, 4, 5], outbound.Recorder.Entries.Select(entry => entry.Order!.Value.Sequence));
    }

    [Fact]
    public async Task An_empty_plan_remains_harmless_with_a_cancelled_token()
    {
        await using var outbound = Outbound();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await outbound.RunAsync(async () => await Delivery(outbound).DeliverAsync(
            new SelectedSessionCleanupNotificationPlan(Owner, [], [], []), cancellation.Token));

        Assert.Empty(outbound.Recorder.Entries);
        Assert.Empty(outbound.Projections.Published);
    }

    [Fact]
    public async Task A_cancelled_owner_registration_propagates_the_token_and_stops_later_stages()
    {
        await using var outbound = Outbound(Peer(9));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await outbound.RunAsync(async () =>
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await Delivery(outbound).DeliverAsync(
                    new SelectedSessionCleanupNotificationPlan(Owner, [StoreClosed], [Peer(9)], [Departed]), cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
        });

        Assert.Empty(outbound.Recorder.Entries);
        Assert.Empty(outbound.Projections.Published);
    }

    [Fact]
    public async Task An_owner_mapping_failure_propagates_without_projecting_later_stages()
    {
        await using var outbound = Outbound(Peer(9));
        await outbound.RunAsync(async () =>
            await Assert.ThrowsAsync<InvalidDataException>(async () => await Delivery(outbound).DeliverAsync(
                new SelectedSessionCleanupNotificationPlan(Owner,
                    [new SelectedSessionCleanupNotice((SelectedSessionCleanupNoticeKind)99, 42)], [Peer(9)], [Departed]),
                CancellationToken.None)));

        Assert.Empty(outbound.Recorder.Entries);
        Assert.Empty(outbound.Projections.Published);
    }

    /// <summary>What each half becomes, and that town peers keep their sampled sessions.</summary>
    [Fact]
    public async Task Each_half_becomes_the_frames_it_always_did()
    {
        var peer = Peer(9);
        await using var outbound = Outbound(peer);
        var notifications = outbound.Projections;

        await outbound.RunAsync(async () => await Delivery(outbound).DeliverAsync(
            new SelectedSessionCleanupNotificationPlan(
                Owner, [StoreClosed], [peer], [StoreClosed, Departed]),
            CancellationToken.None));

        var direct = outbound.Recorder.Entries.Single(entry => entry.Connection.ConnectionId == Owner);
        Assert.Equal(
            "ExpertStoreClosedMessage",
            Assert.Single(direct.Messages).GetType().Name);
        Assert.Equal(OutboundDelivery.Required, direct.Delivery);

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

    /// <summary>Each conversation the departure ended is announced last, to its remaining member only.</summary>
    [Fact]
    public async Task Ended_conversations_are_announced_to_the_member_left_behind_after_everything_else()
    {
        var peer = Peer(9);
        var remaining = Peer(10);
        await using var outbound = Outbound(peer, remaining);
        var notifications = outbound.Projections;
        var owner = SelectedSessionIdentity.Create(new AccountId(42), new CharacterId(42), Owner, 1, 10011);
        var departure = new Dfo.Server.Data.Chat.ChatConversationDeparture(new(5),
            new(owner, "a"u8.ToArray()), new(remaining, "b"u8.ToArray()));

        await outbound.RunAsync(async () => await Delivery(outbound).DeliverAsync(
            new SelectedSessionCleanupNotificationPlan(Owner, [StoreClosed], [peer], [Departed])
            {
                ConversationDepartures = [departure],
            },
            CancellationToken.None));

        var published = notifications.Published.ToArray();
        Assert.Equal(2, published.Length);
        Assert.IsType<SessionCleanupAnnounced>(published[0].Notification);
        Assert.Equal([remaining], Assert.IsType<NotificationAudience.TheseSessions>(published[1].Audience).Sessions);
        Assert.Same(departure, Assert.IsType<Dfo.Server.Protocol.Game.ChatConversationLeft>(published[1].Notification).Departure);
    }

    /// <summary>RQ-051 on the disconnect route: the leaver's friends in the world are told it left, after everything else.</summary>
    [Fact]
    public async Task A_friend_departure_is_announced_to_the_observers_after_everything_else()
    {
        var peer = Peer(9);
        await using var outbound = Outbound(peer);
        var notifications = outbound.Projections;
        var departure = new SelectedSessionFriendDeparture(new CharacterId(42), "a"u8.ToArray(), [new CharacterId(11), new CharacterId(12)]);

        await outbound.RunAsync(async () => await Delivery(outbound).DeliverAsync(
            new SelectedSessionCleanupNotificationPlan(Owner, [StoreClosed], [peer], [Departed])
            {
                FriendDeparture = departure,
            },
            CancellationToken.None));

        var published = notifications.Published.ToArray();
        Assert.Equal(2, published.Length);
        Assert.IsType<SessionCleanupAnnounced>(published[0].Notification);
        Assert.Equal([new CharacterId(11), new CharacterId(12)],
            Assert.IsType<NotificationAudience.TheseCharacters>(published[1].Audience).CharacterIds);
        var offline = Assert.IsType<FriendWentOffline>(published[1].Notification);
        Assert.Equal(new CharacterId(42), offline.Character);
        Assert.Equal("a"u8.ToArray(), offline.Name);
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
        await using var outbound = Outbound(Peer(9));
        var notifications = outbound.Projections;

        await outbound.RunAsync(async () => await Delivery(outbound).DeliverAsync(
            new SelectedSessionCleanupNotificationPlan(
                Owner,
                owner ? [StoreClosed] : [],
                peers ? [Peer(9)] : [],
                peers ? [Departed] : []),
            CancellationToken.None));

        Assert.Equal(owner ? 1 : 0, outbound.Recorder.Entries.Count(entry => entry.Connection.ConnectionId == Owner));
        Assert.Equal(peers ? 1 : 0, notifications.Published.Count);
    }

    /// <summary>XO-5: an owner slow to take his notices, within the limit, is still told first.</summary>
    [Fact]
    public async Task A_slow_owner_is_delivered_before_the_peers_and_the_party()
    {
        var peer = Peer(9);
        await using var outbound = Outbound(peer);
        outbound.Transport.Slow(Owner, TimeSpan.FromMilliseconds(300));

        await outbound.RunAsync(async () => await Delivery(outbound).DeliverAsync(
            new SelectedSessionCleanupNotificationPlan(Owner, [StoreClosed], [peer], [Departed]),
            CancellationToken.None));
        await outbound.SettledAsync();

        Assert.True(outbound.Delivered(Owner) >= 0);
        Assert.True(outbound.Delivered(peer.ConnectionId) > outbound.Delivered(Owner),
            "the peers were delivered before the owner");
    }

    /// <summary>XO-5 / DR-10: an owner whose notices are not delivered stops the rest of the cleanup.</summary>
    [Fact]
    public async Task An_owner_whose_delivery_fails_skips_the_peers_and_the_party()
    {
        var peer = Peer(9);
        await using var outbound = Outbound(peer);
        outbound.Transport.Fail(Owner);

        await outbound.RunAsync(async () => await Delivery(outbound).DeliverAsync(
            new SelectedSessionCleanupNotificationPlan(Owner, [StoreClosed], [peer], [Departed]),
            CancellationToken.None));
        await outbound.SettledAsync();

        Assert.Equal([OutboundOutcome.Failed], outbound.Outcomes(Owner));
        Assert.Equal([OutboundOutcome.Skipped], outbound.Outcomes(peer.ConnectionId));
        Assert.Equal(-1, outbound.Delivered(peer.ConnectionId));
    }

    private static XoDelivery Outbound(params SelectedSessionIdentity[] others)
    {
        var outbound = new XoDelivery();
        outbound.Connect(Owner);
        outbound.Connect(others);
        return outbound;
    }

    private static CleanupNotificationDelivery Delivery(XoDelivery outbound) =>
        ProtocolAlgorithmFixture.Create<CleanupNotificationDelivery>(
            outbound.Game, outbound.Projections, new PartyProjectionStreamRegistry(), outbound.Ordering);

    /// <summary>What was registered, in registration order, as owner / peers / party.</summary>
    private static IReadOnlyList<string> Order(XoDelivery outbound) =>
    [
        .. outbound.Recorder.Entries.Select(entry => entry.Connection.ConnectionId == Owner ? "owner"
            : entry.Messages.Single() is XoProjected { Notification: nameof(PartyRosterChanged) } ? "party" : "peers"),
    ];

    private static SelectedSessionIdentity Peer(int characterId) =>
        SelectedSessionIdentity.Create(
            new AccountId(characterId),
            new CharacterId(characterId),
            new NetworkConnectionId($"Game:peer-{characterId}"),
            1,
            10011);

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
}
