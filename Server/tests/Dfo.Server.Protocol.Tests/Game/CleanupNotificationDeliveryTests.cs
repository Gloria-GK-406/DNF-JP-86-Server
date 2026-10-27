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
/// A departure has two audiences, and they leave by different routes now.
/// </summary>
/// <remarks>
/// The owner's notices go down the connection the departure is happening on; the peers are a
/// fan-out named in exact selected sessions. Six places deliver one of these plans -- four inside a
/// catch (OnlineUserConnectionObserver.cs:102, SelectCharacterHandler.cs:136, :156, :162) -- so
/// the rule that the owner hears first lives here rather than in six hand-written sequences.
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
    public async Task Existing_owner_and_town_notices_finish_before_party_projection()
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
        var partyDelivery = global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<global::Dfo.Server.Protocol.Game.Party.PartyNotificationDelivery>(notifications, new PartyProjectionStreamRegistry(), new FixedSelectedSessions(partyMember, townObserver), new FixedPresence(partyMember, townObserver), outbound.Ordering);

        await outbound.RunAsync(async () =>
        await global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<global::Dfo.Server.Protocol.Game.StateSynchronization.CleanupNotificationDelivery>(outbound.Game, notifications, partyDelivery, outbound.Ordering).DeliverAsync(
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
            CancellationToken.None));

        Assert.Equal(["owner", "peers", "party", "party"], Order(outbound));
        var published = notifications.Published.ToArray();
        Assert.IsType<SessionCleanupAnnounced>(published[0].Notification);
        Assert.IsType<PartyRosterChanged>(published[1].Notification);
        Assert.IsType<PartyRosterChanged>(published[2].Notification);
        Assert.Equal([1, 2, 3, 4], outbound.Recorder.Entries.Select(entry => entry.Order!.Value.Sequence));
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
        global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<global::Dfo.Server.Protocol.Game.StateSynchronization.CleanupNotificationDelivery>(outbound.Game, outbound.Projections, global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<global::Dfo.Server.Protocol.Game.Party.PartyNotificationDelivery>(outbound.Projections), outbound.Ordering);

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

    private sealed class FixedSelectedSessions(params SelectedSessionIdentity[] values)
        : ISelectedSessionDomainService
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
}
