using Dfo.Server.Abstractions.DomainEvents;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Parties;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonSelection;
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
                + "00000000000004000000000500020000"
                + "0100000000"
                + "0200000000"
                + "FFFF000000FFFF000000FFFF000000"
                + "FFFF000000FFFF000000FFFF000000"
                + "00000000"),
            body);
        Assert.Equal((ushort)1, BitConverter.ToUInt16(body, 21));
        Assert.Equal((byte)PartyCategory.Normal, body[18]);
        Assert.Equal((byte)0, body[62]);
        Assert.Equal((byte)0, body[64]);
    }

    [Fact]
    public void Full_roster_marks_a_leader_in_slot_one_without_reordering_members()
    {
        var snapshot = Snapshot() with { LeaderWireUserId = Invitee.WireUserId };

        var body = PartyRosterEncoder.EncodeBody(PartyRosterMessage.Refresh(snapshot));

        Assert.Equal((ushort)Inviter.WireUserId, BitConverter.ToUInt16(body, 21));
        Assert.Equal((ushort)Invitee.WireUserId, BitConverter.ToUInt16(body, 26));
        Assert.Equal((byte)1, body[62]);
    }

    [Fact]
    public void Host_change_roster_uses_type_two_and_omits_the_information_block()
    {
        var snapshot = Snapshot() with { LeaderWireUserId = Invitee.WireUserId };

        var body = PartyRosterEncoder.EncodeBody(PartyRosterMessage.ChangeHost(snapshot));

        Assert.Equal(49, body.Length);
        Assert.Equal((byte)2, body[4]);
        Assert.Equal((ushort)Inviter.WireUserId, BitConverter.ToUInt16(body, 5));
        Assert.Equal((ushort)Invitee.WireUserId, BitConverter.ToUInt16(body, 10));
        Assert.Equal((byte)1, body[46]);
        Assert.Equal((byte)0, body[48]);
    }

    [Fact]
    public void Full_roster_forwards_the_inline_title_before_the_canonical_settings_tail()
    {
        var snapshot = Snapshot() with { Title = new PartyTitle([0x61, 0x62]) };

        Assert.Equal(
            Hex(
                "0100341200"
                + "000200000061620004000000000500020000"
                + "0100000000"
                + "0200000000"
                + "FFFF000000FFFF000000FFFF000000"
                + "FFFF000000FFFF000000FFFF000000"
                + "00000000"),
            PartyRosterEncoder.EncodeBody(PartyRosterMessage.Refresh(snapshot)));
    }

    [Fact]
    public void Settings_only_body_matches_both_current_a21_information_branches()
    {
        var zeroInfo = Snapshot(new PartySettings(
            [0, 0, 4, 0, 0, 0, 0, 5, 0, (byte)PartyCategory.Cycle, 0x34, 0x12]))
            with { Title = new PartyTitle(Hex("E697A0E9989FE4BC8DE5908D")) };
        Assert.Equal(
            Hex(
                "0100341201"
                + "000C000000E697A0E9989FE4BC8DE5908D"
                + "0004000000000500013412"
                + "00"),
            PartySettingsEncoder.EncodeBody(new PartySettingsMessage(zeroInfo)));

        var indexedInfo = Snapshot(new PartySettings(
            [1, 0, 4, 0, 0, 0, 0, 5, 0, (byte)PartyCategory.Normal, 0, 0]))
            with { Title = new PartyTitle([0x61, 0x62]) };
        Assert.Equal(
            Hex("010034120101000400000000050002000000"),
            PartySettingsEncoder.EncodeBody(new PartySettingsMessage(indexedInfo)));
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
        var delivery = Delivery(bus);
        var snapshot = Snapshot();
        var plan = new PartyNotificationPlan(
            [Invitee.CharacterId],
            snapshot,
            Acceptance: Acceptance(),
            ClearPartyId: snapshot.PartyId,
            ClearPartyGeneration: snapshot.Generation,
            ClearPartyRevision: snapshot.Revision);

        await delivery.DeliverAsync(plan, CancellationToken.None);

        Assert.Collection(
            bus.Publications,
            value =>
            {
                AssertPublication<PartyRosterChanged>(value, 2);
                Assert.True(Assert.IsType<PartyRosterChanged>(value.Notification).ClearsParty);
            },
            value => AssertPublication<PartyRealtimeChanged>(value, 1, 2),
            value => AssertPublication<PartyInviteAccepted>(value, 1),
            value => AssertPublication<PartyEndpointsChanged>(value, 1, 2),
            value => AssertPublication<PartyRosterChanged>(value, 1, 2),
            value => AssertPublication<PartyRealtimeChanged>(value, 1, 2));
        Assert.Same(
            snapshot,
            Assert.IsType<PartyRealtimeChanged>(bus.Publications[1].Notification).Snapshot);
        Assert.Same(
            snapshot,
            Assert.IsType<PartyEndpointsChanged>(bus.Publications[3].Notification).Snapshot);
        Assert.Same(
            snapshot,
            Assert.IsType<PartyRosterChanged>(bus.Publications[4].Notification).Snapshot);
        Assert.Same(
            snapshot,
            Assert.IsType<PartyRealtimeChanged>(bus.Publications[5].Notification).Snapshot);
    }

    [Fact]
    public async Task TownObserversReceiveOneRosterRefreshAfterTheMemberProjection()
    {
        var bus = new RecordingNotificationBus();
        var delivery = Delivery(bus);
        var snapshot = Snapshot();

        await delivery.DeliverAsync(new PartyNotificationPlan(
            [],
            snapshot,
            Acceptance: Acceptance(),
            DirectoryChange: ScopeChange()), CancellationToken.None);

        Assert.Equal(6, bus.Publications.Count);
        Assert.Collection(
            bus.Publications.Take(5),
            value => AssertPublication<PartyRealtimeChanged>(value, 1, 2),
            value => AssertPublication<PartyInviteAccepted>(value, 1),
            value => AssertPublication<PartyEndpointsChanged>(value, 1, 2),
            value => AssertPublication<PartyRosterChanged>(value, 1, 2),
            value => AssertPublication<PartyRealtimeChanged>(value, 1, 2));
        var observerProjection = bus.Publications[5];
        Assert.IsType<PartyRosterChanged>(observerProjection.Notification);
        Assert.Equal(
            new[] { Observer },
            Assert.IsType<NotificationAudience.TheseSessions>(observerProjection.Audience).Sessions);
    }

    [Fact]
    public async Task ATransientObserverFailureRetriesWithinOneMutationDelivery()
    {
        var bus = new FailingObserverNotificationBus(failureCount: 1);
        var delivery = Delivery(bus);
        var plan = new PartyNotificationPlan(
            [],
            Snapshot(),
            DirectoryChange: ScopeChange());

        await delivery.DeliverAsync(plan, CancellationToken.None);

        Assert.Equal(2, bus.ObserverAttempts);
        Assert.Equal(1, bus.SuccessfulObserverAttempts);
    }

    [Fact]
    public async Task APersistentlyFailedObserverLeavesTheSameRevisionRetryableLater()
    {
        var bus = new FailingObserverNotificationBus(failureCount: 2);
        var delivery = Delivery(bus);
        var plan = new PartyNotificationPlan(
            [],
            Snapshot(),
            DirectoryChange: ScopeChange());

        await delivery.DeliverAsync(plan, CancellationToken.None);
        Assert.Equal(2, bus.ObserverAttempts);
        Assert.Equal(0, bus.SuccessfulObserverAttempts);

        await delivery.DeliverAsync(plan, CancellationToken.None);

        Assert.Equal(3, bus.ObserverAttempts);
        Assert.Equal(1, bus.SuccessfulObserverAttempts);
    }

    [Fact]
    public async Task RequesterOnlyRealtimeDoesNotCommitTheFullMutationRevision()
    {
        var bus = new RecordingNotificationBus();
        var streams = new PartyProjectionStreamRegistry();
        var delivery = Delivery(bus, streams);
        var snapshot = Snapshot() with { Revision = 4 };

        await delivery.DeliverRealtimeAsync(Inviter, snapshot, CancellationToken.None);
        bus.Publications.Clear();
        await delivery.DeliverAsync(
            new PartyNotificationPlan([], snapshot, DirectoryChange: ScopeChange()),
            CancellationToken.None);

        Assert.Collection(
            bus.Publications,
            value => AssertPublication<PartyRealtimeChanged>(value, 1, 2),
            value => AssertPublication<PartyEndpointsChanged>(value, 1, 2),
            value => AssertPublication<PartyRosterChanged>(value, 1, 2),
            value => AssertPublication<PartyRosterChanged>(value, 3));
    }

    [Fact]
    public async Task Direct_creation_projects_the_complete_state_while_update_projects_only_settings()
    {
        var bus = new RecordingNotificationBus();
        var delivery = Delivery(bus);
        var template = Snapshot();
        var created = template with { Revision = 1, Members = [template.Members[0]] };

        await delivery.DeliverAsync(new PartyNotificationPlan(
            [],
            created,
            SettingsChange: PartySettingsChangeKind.Created), CancellationToken.None);

        Assert.Collection(
            bus.Publications,
            value => AssertPublication<PartyRealtimeChanged>(value, 1),
            value => AssertPublication<PartyEndpointsChanged>(value, 1),
            value => AssertPublication<PartyRosterChanged>(value, 1));

        var updated = created with { Revision = 2 };
        await delivery.DeliverAsync(new PartyNotificationPlan(
            [],
            updated,
            SettingsChange: PartySettingsChangeKind.Updated,
            DirectoryChange: ScopeChange()), CancellationToken.None);

        Assert.Collection(
            bus.Publications.Skip(3),
            settings => AssertPublication<PartySettingsChanged>(settings, 1),
            observer => AssertPublication<PartyRosterChanged>(observer, 2, 3));
        Assert.Same(
            updated,
            Assert.IsType<PartySettingsChanged>(bus.Publications[3].Notification).Snapshot);
    }

    [Fact]
    public async Task Invalid_plan_shapes_are_rejected_before_any_publication()
    {
        var snapshot = Snapshot();
        PartyNotificationPlan[] invalidPlans =
        [
            new(
                [],
                snapshot,
                Invitation: Invitation(),
                SettingsChange: PartySettingsChangeKind.Created),
            new([], SettingsChange: PartySettingsChangeKind.Created),
            new([], snapshot, ClearPartyId: 0x1234, SettingsChange: PartySettingsChangeKind.Updated),
            new([], snapshot, SettingsChange: (PartySettingsChangeKind)99),
            new([], Acceptance: Acceptance()),
            new([], DirectoryChange: ScopeChange()),
            new(
                [Inviter.CharacterId],
                snapshot,
                Acceptance: Acceptance(),
                ClearPartyId: snapshot.PartyId,
                ClearPartyGeneration: snapshot.Generation,
                ClearPartyRevision: snapshot.Revision),
            new(
                [Invitee.CharacterId],
                ClearPartyId: 0x1234,
                ClearPartyGeneration: snapshot.Generation),
        ];

        foreach (var plan in invalidPlans)
        {
            var bus = new RecordingNotificationBus();
            var delivery = Delivery(bus);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await delivery.DeliverAsync(plan, CancellationToken.None));
            Assert.Empty(bus.Publications);
        }
    }

    [Fact]
    public async Task Settings_update_arriving_before_creation_promotes_latest_snapshot_to_full_state()
    {
        var bus = new RecordingNotificationBus();
        var delivery = Delivery(bus);
        var template = Snapshot();
        var created = template with { Revision = 1, Members = [template.Members[0]] };
        var updated = template with { Revision = 2 };

        await delivery.DeliverAsync(new PartyNotificationPlan(
            [], updated, SettingsChange: PartySettingsChangeKind.Updated), CancellationToken.None);
        await delivery.DeliverAsync(new PartyNotificationPlan(
            [], created, SettingsChange: PartySettingsChangeKind.Created), CancellationToken.None);

        Assert.Collection(
            bus.Publications,
            value => AssertPublication<PartyRealtimeChanged>(value, 1, 2),
            value => AssertPublication<PartyEndpointsChanged>(value, 1, 2),
            value => AssertPublication<PartyRosterChanged>(value, 1, 2));
        Assert.All(
            bus.Publications,
            value => Assert.Equal(2, SnapshotRevision(value.Notification)));
    }

    [Fact]
    public async Task Settings_update_overtaking_acceptance_gives_the_new_member_full_state_and_keeps_the_ack()
    {
        var bus = new RecordingNotificationBus();
        var delivery = Delivery(bus);
        var template = Snapshot();
        var created = template with { Revision = 1, Members = [template.Members[0]] };
        await delivery.DeliverAsync(new PartyNotificationPlan(
            [], created, SettingsChange: PartySettingsChangeKind.Created), CancellationToken.None);
        bus.Publications.Clear();

        var accepted = template with { Revision = 2 };
        var updated = template with { Revision = 3 };
        await delivery.DeliverAsync(new PartyNotificationPlan(
            [], updated, SettingsChange: PartySettingsChangeKind.Updated), CancellationToken.None);
        await delivery.DeliverAsync(new PartyNotificationPlan(
            [], accepted, Acceptance: Acceptance()),
            CancellationToken.None);

        Assert.Collection(
            bus.Publications,
            value => AssertPublication<PartyRealtimeChanged>(value, 1, 2),
            value => AssertPublication<PartyEndpointsChanged>(value, 1, 2),
            value => AssertPublication<PartyRosterChanged>(value, 1, 2),
            value => AssertPublication<PartyInviteAccepted>(value, 1));
        Assert.All(
            bus.Publications.Take(3),
            value => Assert.Equal(3, SnapshotRevision(value.Notification)));
    }

    [Fact]
    public async Task Settings_update_overtaking_leave_still_clears_the_leaver_and_rejoin_gets_full_state()
    {
        var bus = new RecordingNotificationBus();
        var delivery = Delivery(bus);
        var template = Snapshot();
        await delivery.DeliverAsync(new PartyNotificationPlan([], template), CancellationToken.None);
        bus.Publications.Clear();

        var survivor = template with { Revision = 4, Members = [template.Members[0]] };
        var updated = survivor with { Revision = 5 };
        await delivery.DeliverAsync(new PartyNotificationPlan(
            [], updated, SettingsChange: PartySettingsChangeKind.Updated), CancellationToken.None);
        await delivery.DeliverAsync(new PartyNotificationPlan(
            [Invitee.CharacterId],
            survivor,
            ClearPartyId: survivor.PartyId,
            ClearPartyGeneration: survivor.Generation,
            ClearPartyRevision: survivor.Revision), CancellationToken.None);

        Assert.Collection(
            bus.Publications,
            value => AssertPublication<PartySettingsChanged>(value, 1),
            value =>
            {
                AssertPublication<PartyRosterChanged>(value, 2);
                Assert.True(Assert.IsType<PartyRosterChanged>(value.Notification).ClearsParty);
            });

        bus.Publications.Clear();
        var rejoined = template with { Revision = 6 };
        await delivery.DeliverAsync(new PartyNotificationPlan(
            [], rejoined, SettingsChange: PartySettingsChangeKind.Updated), CancellationToken.None);

        Assert.Collection(
            bus.Publications,
            value => AssertPublication<PartyRealtimeChanged>(value, 1, 2),
            value => AssertPublication<PartyEndpointsChanged>(value, 1, 2),
            value => AssertPublication<PartyRosterChanged>(value, 1, 2));
    }

    [Fact]
    public async Task Newer_rejoin_suppresses_a_stale_clear_for_the_exact_current_member()
    {
        var bus = new RecordingNotificationBus();
        var delivery = Delivery(bus);
        var template = Snapshot();
        await delivery.DeliverAsync(new PartyNotificationPlan(
            [], template with { Revision = 1 }), CancellationToken.None);

        await delivery.DeliverAsync(new PartyNotificationPlan(
            [],
            template with { Revision = 3 },
            Acceptance: Acceptance()),
            CancellationToken.None);
        bus.Publications.Clear();

        var staleSurvivor = template with
        {
            Revision = 2,
            Members = [template.Members[0]],
        };
        await delivery.DeliverAsync(new PartyNotificationPlan(
            [Invitee.CharacterId],
            staleSurvivor,
            ClearPartyId: staleSurvivor.PartyId,
            ClearPartyGeneration: staleSurvivor.Generation,
            ClearPartyRevision: staleSurvivor.Revision), CancellationToken.None);

        Assert.Empty(bus.Publications);
    }

    [Fact]
    public async Task Newer_leave_suppresses_a_stale_acceptance_ack_for_the_missing_invitee()
    {
        var bus = new RecordingNotificationBus();
        var delivery = Delivery(bus);
        var template = Snapshot();
        await delivery.DeliverAsync(new PartyNotificationPlan(
            [], template with { Revision = 1 }), CancellationToken.None);

        var survivor = template with
        {
            Revision = 3,
            Members = [template.Members[0]],
        };
        await delivery.DeliverAsync(new PartyNotificationPlan(
            [Invitee.CharacterId],
            survivor,
            ClearPartyId: survivor.PartyId,
            ClearPartyGeneration: survivor.Generation,
            ClearPartyRevision: survivor.Revision), CancellationToken.None);
        bus.Publications.Clear();

        await delivery.DeliverAsync(new PartyNotificationPlan(
            [],
            template with { Revision = 2 },
            Acceptance: Acceptance()),
            CancellationToken.None);

        Assert.Empty(bus.Publications);
    }

    [Fact]
    public async Task Older_settings_revision_arriving_after_newer_is_suppressed()
    {
        var bus = new RecordingNotificationBus();
        var delivery = Delivery(bus);
        await delivery.DeliverAsync(new PartyNotificationPlan(
            [], Snapshot() with { Revision = 1 },
            SettingsChange: PartySettingsChangeKind.Created), CancellationToken.None);
        bus.Publications.Clear();
        var older = Snapshot() with { Revision = 3 };
        var newer = Snapshot() with { Revision = 4 };

        await delivery.DeliverAsync(new PartyNotificationPlan(
            [], newer, SettingsChange: PartySettingsChangeKind.Updated), CancellationToken.None);
        await delivery.DeliverAsync(new PartyNotificationPlan(
            [], older, SettingsChange: PartySettingsChangeKind.Updated), CancellationToken.None);

        var publication = Assert.Single(bus.Publications);
        Assert.Equal(4, Assert.IsType<PartySettingsChanged>(publication.Notification).Snapshot.Revision);
    }

    [Fact]
    public async Task Invite_is_published_only_to_its_exact_target()
    {
        var bus = new RecordingNotificationBus();
        var delivery = Delivery(bus);

        await delivery.DeliverAsync(new PartyNotificationPlan(
            [],
            Invitation: Invitation()),
            CancellationToken.None);

        var publication = Assert.Single(bus.Publications);
        AssertPublication<PartyInviteIssued>(publication, 2);
    }

    [Fact]
    public async Task CapturedInviteAudienceNeverRetargetsToAReplacementConnection()
    {
        var bus = new RecordingNotificationBus();
        var sessions = new SessionDirectory(Inviter, Invitee, Observer);
        var presence = new FixedPresence(Inviter, Invitee, Observer);
        var delivery = new PartyNotificationDelivery(
            bus,
            new PartyProjectionStreamRegistry(),
            sessions,
            presence);
        var plan = new PartyNotificationPlan([], Invitation: Invitation());
        var captured = PartyNotificationRecipientCapture.Capture(plan, sessions, presence);
        var replacement = Invitee with
        {
            ConnectionId = new NetworkConnectionId("replacement-invitee"),
            ConnectionGeneration = 2,
        };
        sessions.Select(replacement);

        await delivery.DeliverAsync(plan, captured, CancellationToken.None);

        var publication = Assert.Single(bus.Publications);
        Assert.Equal(
            [Invitee],
            Assert.IsType<NotificationAudience.TheseSessions>(publication.Audience).Sessions);
        Assert.DoesNotContain(replacement,
            Assert.IsType<NotificationAudience.TheseSessions>(publication.Audience).Sessions);
    }

    [Fact]
    public async Task DelayedPreMovePlanCannotRepublishItsOldDirectoryScope()
    {
        var parties = new PartyService(
            new FixedPartyMemberStatus(),
            new PartyModeRuleService(),
            TimeProvider.System,
            NoPartyEndpoints.Instance);
        var created = parties.CreateOrUpdateSettings(
            Character(Inviter),
            DefaultSettings(),
            PartyTitle.Empty);
        Assert.True(created.Succeeded);
        var plan = created.Notifications;
        var sessions = new SessionDirectory(Inviter, Invitee, Observer);
        var presence = new FixedPresence(Inviter, Invitee, Observer);
        var captured = PartyNotificationRecipientCapture.Capture(plan, sessions, presence);
        parties.UpdateDirectoryPresence(
            Inviter.CharacterId,
            new PartyDirectoryScope(11, 1, 2));
        var bus = new RecordingNotificationBus();
        var delivery = new PartyNotificationDelivery(
            bus,
            new PartyProjectionStreamRegistry(),
            sessions,
            presence,
            parties);

        await delivery.DeliverAsync(plan, captured, CancellationToken.None);

        Assert.DoesNotContain(bus.Publications, publication =>
            publication.Notification is PartyRosterChanged
            && Assert.IsType<NotificationAudience.TheseSessions>(publication.Audience)
                .Sessions.Any(session => session == Invitee || session == Observer));
    }

    [Fact]
    public async Task Leaving_member_clear_and_survivor_refresh_use_disjoint_audiences()
    {
        var bus = new RecordingNotificationBus();
        var delivery = Delivery(bus);
        var survivor = new PartySnapshot(
            0x1234,
            7,
            4,
            1,
            DefaultSettings(),
            PartyTitle.Empty,
            [new(Character(Inviter), 0, Endpoint(), 73)]);

        await delivery.DeliverAsync(new PartyNotificationPlan(
            [Invitee.CharacterId],
            survivor,
            ClearPartyId: 0x1234,
            ClearPartyGeneration: survivor.Generation,
            ClearPartyRevision: survivor.Revision,
            DirectoryChange: ScopeChange()), CancellationToken.None);

        Assert.Collection(
            bus.Publications,
            value =>
            {
                AssertPublication<PartyRosterChanged>(value, 2);
                Assert.True(Assert.IsType<PartyRosterChanged>(value.Notification).ClearsParty);
            },
            value => AssertPublication<PartyRealtimeChanged>(value, 1),
            value => AssertPublication<PartyEndpointsChanged>(value, 1),
            value => AssertPublication<PartyRosterChanged>(value, 1),
            value =>
            {
                AssertPublication<PartyRosterChanged>(value, 2, 3);
                Assert.False(Assert.IsType<PartyRosterChanged>(value.Notification).ClearsParty);
            });
    }

    [Fact]
    public async Task Manual_leadership_transfer_projects_only_the_native_host_change()
    {
        var bus = new RecordingNotificationBus();
        var transfer = new PartySnapshot(
            0x1234, 7, 4, Invitee.WireUserId, DefaultSettings(), PartyTitle.Empty,
            [new(Character(Inviter), 0, Endpoint(), 73),
             new(Character(Invitee), 1, null, 45)]);

        await Delivery(bus).DeliverAsync(new PartyNotificationPlan(
            [], transfer,
            DirectoryChange: ScopeChange(),
            LeadershipTransfer: transfer), CancellationToken.None);

        Assert.Collection(bus.Publications,
            publication => AssertPublication<PartyLeadershipChanged>(publication, 1, 2),
            publication => AssertPublication<PartyRosterChanged>(publication, 3));
        var hostChange = Assert.IsType<PartyLeadershipChanged>(bus.Publications[0].Notification);

        var messages = await new PartyLeadershipChangedHandler(
                new SessionDirectory(Inviter, Invitee))
            .ComposeAsync(
                new NotificationRecipient(Inviter.CharacterId, Inviter.ConnectionId),
                hostChange,
                CancellationToken.None);
        Assert.Collection(messages,
            value => Assert.Equal((byte)1, Assert.IsType<UdpHostNotification>(value).HostSlot),
            value => Assert.True(Assert.IsType<PartyRosterMessage>(value).HostChanged));
    }

    [Fact]
    public async Task Leader_leave_rekeys_client_party_by_clearing_old_id_before_new_full_roster()
    {
        var bus = new RecordingNotificationBus();
        var survivor = Snapshot() with
        {
            PartyId = 0x5678,
            Revision = 5,
            LeaderWireUserId = Invitee.WireUserId,
            Members = [new(Character(Invitee), 0, null, 45)],
        };
        await Delivery(bus).DeliverAsync(new PartyNotificationPlan(
            [Inviter.CharacterId, Invitee.CharacterId], survivor,
            ClearPartyId: 0x1234,
            ClearPartyGeneration: survivor.Generation,
            ClearPartyRevision: survivor.Revision,
            DirectoryChange: ScopeChange()), CancellationToken.None);

        Assert.Collection(bus.Publications,
            value =>
            {
                AssertPublication<PartyRosterChanged>(value, 1, 2);
                Assert.Equal((ushort)0x1234,
                    Assert.IsType<PartyRosterChanged>(value.Notification).ClearPartyId);
            },
            value => AssertPublication<PartyRealtimeChanged>(value, 2),
            value => AssertPublication<PartyEndpointsChanged>(value, 2),
            value =>
            {
                AssertPublication<PartyRosterChanged>(value, 2);
                Assert.Equal((ushort)0x5678,
                    Assert.IsType<PartyRosterChanged>(value.Notification).Snapshot!.PartyId);
            },
            value =>
            {
                AssertPublication<PartyRosterChanged>(value, 3);
                Assert.Equal((ushort)0x1234,
                    Assert.IsType<PartyRosterChanged>(value.Notification).ClearPartyId);
            },
            value =>
            {
                AssertPublication<PartyRosterChanged>(value, 3);
                Assert.Equal((ushort)0x5678,
                    Assert.IsType<PartyRosterChanged>(value.Notification).Snapshot!.PartyId);
            });
    }

    [Fact]
    public async Task DisbandClearsMembersBeforeUnrelatedObserversWithoutDuplicateRecipients()
    {
        var bus = new RecordingNotificationBus();
        var delivery = Delivery(bus);
        var snapshot = Snapshot();

        await delivery.DeliverAsync(new PartyNotificationPlan(
            [Inviter.CharacterId, Invitee.CharacterId],
            ClearPartyId: snapshot.PartyId,
            ClearPartyGeneration: snapshot.Generation,
            ClearPartyRevision: snapshot.Revision,
            DirectoryChange: new PartyDirectoryChange(Scope(), null)), CancellationToken.None);

        Assert.Collection(
            bus.Publications,
            members =>
            {
                AssertPublication<PartyRosterChanged>(members, 1, 2);
                Assert.True(Assert.IsType<PartyRosterChanged>(members.Notification).ClearsParty);
            },
            observer =>
            {
                AssertPublication<PartyRosterChanged>(observer, 3);
                Assert.True(Assert.IsType<PartyRosterChanged>(observer.Notification).ClearsParty);
            });
    }

    [Fact]
    public async Task Revisions_of_one_party_generation_are_delivered_as_contiguous_phases()
    {
        var bus = new BlockingNotificationBus();
        var streams = new PartyProjectionStreamRegistry();
        var olderDeliveryService = Delivery(bus, streams);
        var newerDeliveryService = Delivery(bus, streams);
        var older = Snapshot() with { Revision = 3 };
        var newer = Snapshot() with { Revision = 4 };

        var olderDelivery = olderDeliveryService.DeliverAsync(
            new PartyNotificationPlan([], older), CancellationToken.None).AsTask();
        await bus.FirstPublication.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var newerDelivery = newerDeliveryService.DeliverAsync(
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
        var delivery = Delivery(bus);
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
        var sessions = new SessionDirectory(Inviter, Invitee, Observer);
        var snapshot = Snapshot();
        var inviterRecipient = new NotificationRecipient(
            Inviter.CharacterId, Inviter.ConnectionId);
        var inviteeRecipient = new NotificationRecipient(
            Invitee.CharacterId, Invitee.ConnectionId);

        Assert.Single(await new PartyInviteIssuedHandler(sessions).ComposeAsync(
            inviteeRecipient,
            new PartyInviteIssued(Invitation()),
            CancellationToken.None));
        Assert.Single(await new PartyInviteAcceptedHandler(sessions).ComposeAsync(
            inviterRecipient,
            new PartyInviteAccepted(Acceptance()),
            CancellationToken.None));
        Assert.Single(await new PartyRealtimeChangedHandler(sessions).ComposeAsync(
            inviteeRecipient, new PartyRealtimeChanged(snapshot), CancellationToken.None));
        Assert.Single(await new PartyEndpointsChangedHandler(sessions).ComposeAsync(
            inviteeRecipient, new PartyEndpointsChanged(snapshot), CancellationToken.None));
        Assert.Single(await new PartyRosterChangedHandler(sessions).ComposeAsync(
            inviteeRecipient,
            PartyRosterChanged.Refresh(snapshot, [Invitee.CharacterId]),
            CancellationToken.None));
        Assert.Single(await new PartySettingsChangedHandler(sessions).ComposeAsync(
            inviteeRecipient, new PartySettingsChanged(snapshot), CancellationToken.None));
        Assert.Single(await new PartyRosterChangedHandler(sessions).ComposeAsync(
            new NotificationRecipient(Observer.CharacterId, Observer.ConnectionId),
            PartyRosterChanged.Refresh(snapshot, [Observer.CharacterId]),
            CancellationToken.None));

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
            new PartyInviteIssued(Invitation()),
            CancellationToken.None));
        Assert.Empty(await new PartyInviteAcceptedHandler(sessions).ComposeAsync(
            inviterRecipient,
            new PartyInviteAccepted(Acceptance()),
            CancellationToken.None));
        Assert.Empty(await new PartyRealtimeChangedHandler(sessions).ComposeAsync(
            inviteeRecipient, new PartyRealtimeChanged(snapshot), CancellationToken.None));
        Assert.Empty(await new PartyEndpointsChangedHandler(sessions).ComposeAsync(
            inviteeRecipient, new PartyEndpointsChanged(snapshot), CancellationToken.None));
        Assert.Empty(await new PartyRosterChangedHandler(sessions).ComposeAsync(
            inviteeRecipient,
            PartyRosterChanged.Refresh(snapshot, [Invitee.CharacterId]),
            CancellationToken.None));
        Assert.Empty(await new PartySettingsChangedHandler(sessions).ComposeAsync(
            inviteeRecipient, new PartySettingsChanged(snapshot), CancellationToken.None));
    }

    private static void AssertPublication<TNotification>(
        Publication publication,
        params int[] characterIds)
    {
        Assert.IsType<TNotification>(publication.Notification);
        var audience = Assert.IsType<NotificationAudience.TheseSessions>(publication.Audience);
        Assert.Equal(characterIds, audience.Sessions.Select(value => value.CharacterId.Value));
    }

    private static long SnapshotRevision(object notification) => notification switch
    {
        PartyRealtimeChanged value => value.Snapshot.Revision,
        PartyEndpointsChanged value => value.Snapshot.Revision,
        PartyRosterChanged { Snapshot: { } snapshot } => snapshot.Revision,
        PartySettingsChanged value => value.Snapshot.Revision,
        _ => throw new InvalidDataException(
            $"Notification '{notification.GetType().Name}' has no party snapshot."),
    };

    private static PartySnapshot Snapshot(PartySettings? settings = null) => new(
        0x1234,
        7,
        3,
        1,
        settings ?? DefaultSettings(),
        PartyTitle.Empty,
        ImmutableArray.Create(
            new PartyMemberSnapshot(Character(Inviter), 0, Endpoint(), 73),
            new PartyMemberSnapshot(Character(Invitee), 1, null, 45)));

    private static PartyCharacterSnapshot Character(SelectedSessionIdentity session) =>
        new(session.AccountId, session.CharacterId, session.WireUserId, Scope());

    private static PartyDirectoryScope Scope() => new(11, 1, 1);

    private static PartyDirectoryChange ScopeChange() => new(Scope(), Scope());

    private static PartyInviteNotification Invitation() =>
        new(Invitee.CharacterId, Inviter.WireUserId, Profile());

    private static PartyAcceptanceNotification Acceptance() =>
        new(
            Inviter.CharacterId,
            Invitee.CharacterId,
            Inviter.WireUserId,
            Invitee.WireUserId);

    private static PartyNotificationDelivery Delivery(
        IEventService bus,
        PartyProjectionStreamRegistry? streams = null)
    {
        var sessions = new SessionDirectory(Inviter, Invitee, Observer);
        return new PartyNotificationDelivery(
            bus,
            streams ?? new PartyProjectionStreamRegistry(),
            sessions,
            new FixedPresence(Inviter, Invitee, Observer));
    }

    private static PartySettings DefaultSettings() => new(
        [0, 0, 4, 0, 0, 0, 0, 5, 0, (byte)PartyCategory.Normal, 0, 0]);

    private static PartyInviteDisplayState Profile() => new(
        ReviveCoinCount: 64,
        RemainingFatigue: 188,
        Stamina: 0,
        AntiMagic: 0);

    private static SessionEndpoint Endpoint() => new(
        5,
        new(172, 17, 208, 1),
        new(172, 17, 208, 1),
        30976,
        1472);

    private static SelectedSessionIdentity Inviter { get; } = SelectedSessionIdentity.Create(
        new AccountId(101), new CharacterId(1), new NetworkConnectionId("inviter"), 1, 10011);

    private static SelectedSessionIdentity Invitee { get; } = SelectedSessionIdentity.Create(
        new AccountId(202), new CharacterId(2), new NetworkConnectionId("invitee"), 1, 10011);

    private static SelectedSessionIdentity Observer { get; } = SelectedSessionIdentity.Create(
        new AccountId(303), new CharacterId(3), new NetworkConnectionId("observer"), 1, 10011);

    private static byte[] Hex(string value) => Convert.FromHexString(value);

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

    private sealed class FailingObserverNotificationBus(int failureCount) : Dfo.Server.Testing.ProjectionEventService
    {
        public int ObserverAttempts { get; private set; }

        public int SuccessfulObserverAttempts { get; private set; }

        public override ValueTask<NotificationDelivery> ProjectAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
        {
            if (notification is not PartyRosterChanged
                || audience is not NotificationAudience.TheseSessions exact
                || !exact.Sessions.SequenceEqual([Observer]))
            {
                return ValueTask.FromResult(new NotificationDelivery([]));
            }

            ObserverAttempts++;
            var outcome = ObserverAttempts <= failureCount
                ? NotificationOutcome.Failed
                : NotificationOutcome.Delivered;
            if (outcome == NotificationOutcome.Delivered)
                SuccessfulObserverAttempts++;
            return ValueTask.FromResult(new NotificationDelivery(
                [new NotificationAttempt(Observer.CharacterId, outcome)]));
        }
    }

    private sealed class FixedPartyMemberStatus : IPartyMemberStatusDomainService
    {
        public ValueTask<PartyInviteDisplayState> InvitationOfAsync(
            PartyCharacterSnapshot character,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(Profile());

        public byte CurrentHpPercentOf(CharacterId characterId) => 73;
    }

    private sealed class NoPartyEndpoints : IPartyMemberSessionEndpointSource
    {
        public static NoPartyEndpoints Instance { get; } = new();

        public SessionEndpoint? FindThroughCurrentSession(CharacterId characterId) => null;
    }

    private sealed class BlockingNotificationBus : Dfo.Server.Testing.ProjectionEventService
    {
        public List<Publication> Publications { get; } = [];

        public TaskCompletionSource FirstPublication { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirst { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<NotificationDelivery> ProjectAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
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
