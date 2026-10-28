
using Microsoft.Extensions.Logging;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.Dungeons;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.Party;

public sealed partial class PartyCommandHandlerTests
{
    [Fact]
    public async Task Leaving_party_in_a_run_detaches_only_the_departing_character()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var parties = new PartyClientProjectionService(new PartyService(
                new FixedPartyMemberStatus(), new PartyModeRuleService(),
                TimeProvider.System, new NoPartyEndpoints()), new PartyClientIdRegistry());
            var scope = new PartyDirectoryScope(11, 1, 1);
            var first = Character(Inviter) with { DirectoryScope = scope };
            var second = Character(Invitee) with { DirectoryScope = scope };
            Assert.True(parties.RequestInvite(first, second,
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(second, first).Succeeded);
            var runs = TestParties.Runs(parties: parties);
            var layout = new DungeonLayout(
                10000, 0, 1, 1, new MazeCell(0, 0), new MazeCell(0, 0),
                61000, 1, new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(61000)});
            var run = runs.PreparePartyEntry(
                [new DungeonRunParticipant(Inviter.AccountId, Inviter.CharacterId, 0, true),
                 new DungeonRunParticipant(Invitee.AccountId, Invitee.CharacterId, 1, false)],
                layout, 0, DungeonCellSeed.CompleteEmpty(layout.StartCell, layout.StartRoomMapId),
                DungeonRunId.New(), DateTimeOffset.UnixEpoch).Run;
            var deliveries = new DungeonRunDeliveryRegistry();
            deliveries.Register(run.RunId, [Inviter, Invitee]);
            var context = DomainCommandFixture.Context(
                Guid.NewGuid(), typeof(object), typeof(object), FixtureUnitKind.Direct);

            var result = await PartyMutationTestDriver.Bus(PartyMutationTestDriver.Handler(
                new SessionDirectory(Inviter, Invitee), parties, Presence, Notifications.Events, runs: runs, deliveries: deliveries))
                .SendAsync(new GameLeavePartyCommand(Dispatch(Inviter), new(GameMessageMetadata.Empty)));

            Assert.True(result);
            Assert.Null(runs.Find(Inviter.CharacterId));
            Assert.Equal(run.RunId, runs.Find(Invitee.CharacterId)!.RunId);
            Assert.Equal([Invitee], deliveries.Find(run.RunId));
        });
    }

    [Fact]
    public async Task Removing_a_member_in_a_run_detaches_only_the_removed_character()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var parties = new PartyClientProjectionService(new PartyService(
                new FixedPartyMemberStatus(), new PartyModeRuleService(),
                TimeProvider.System, new NoPartyEndpoints()), new PartyClientIdRegistry());
            var scope = new PartyDirectoryScope(11, 1, 1);
            var leader = Character(Inviter) with { DirectoryScope = scope };
            var target = Character(Invitee) with { DirectoryScope = scope };
            Assert.True(parties.RequestInvite(leader, target,
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(target, leader).Succeeded);
            var runs = TestParties.Runs(parties: parties);
            var layout = new DungeonLayout(
                10000, 0, 1, 1, new MazeCell(0, 0), new MazeCell(0, 0),
                61000, 1, new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(61000)});
            var run = runs.PreparePartyEntry(
                [new DungeonRunParticipant(Inviter.AccountId, Inviter.CharacterId, 0, true),
                 new DungeonRunParticipant(Invitee.AccountId, Invitee.CharacterId, 1, false)],
                layout, 0, DungeonCellSeed.CompleteEmpty(layout.StartCell, layout.StartRoomMapId),
                DungeonRunId.New(), DateTimeOffset.UnixEpoch).Run;
            var deliveries = new DungeonRunDeliveryRegistry();
            deliveries.Register(run.RunId, [Inviter, Invitee]);
            var context = DomainCommandFixture.Context(
                Guid.NewGuid(), typeof(object), typeof(object), FixtureUnitKind.Direct);

            var result = await PartyMutationTestDriver.Bus(PartyMutationTestDriver.Handler(
                new SessionDirectory(Inviter, Invitee), parties, Presence, Notifications.Events, runs: runs, deliveries: deliveries))
                .SendAsync(new GameRemovePartyMemberCommand(Dispatch(Inviter), new(GameMessageMetadata.Empty, 1)));

            Assert.True(result);
            Assert.Equal(run.RunId, runs.Find(Inviter.CharacterId)!.RunId);
            Assert.Null(runs.Find(Invitee.CharacterId));
            Assert.Equal([Inviter], deliveries.Find(run.RunId));
        });
    }

    [Fact]
    public async Task Commands_are_the_only_route_from_protocol_to_party_writes()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var domain = new RecordingParties();
            var context = DomainCommandFixture.Context(
                Guid.NewGuid(), typeof(object), typeof(object), FixtureUnitKind.Direct);

            var fixture = new RecordingCommandBus(domain: domain);
            var sessions = new SessionDirectory(Inviter, Invitee);
            await DriveRequestPeer(sessions, Presence, fixture, Notifications, NullLogger<RequestPeerHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new RequestPeerRequest(GameMessageMetadata.Empty, Invitee.WireUserId, 0, 64, []), CancellationToken.None);
            await DriveResponsePeer(sessions, Presence, fixture, Notifications, NullLogger<ResponsePeerHandler>.Instance)
                .HandleAsync(Dispatch(Invitee), new ResponsePeerRequest(GameMessageMetadata.Empty, Inviter.WireUserId, 0, 0, []), CancellationToken.None);
            var mutations = PartyMutationTestDriver.Bus(PartyMutationTestDriver.Handler(sessions, domain, Presence, Notifications.Events));
            await mutations.SendAsync(new GameLeavePartyCommand(Dispatch(Invitee), new(GameMessageMetadata.Empty)));
            await mutations.SendAsync(new GameChangePartyLeaderCommand(Dispatch(Inviter), new(GameMessageMetadata.Empty, 1)));
            await mutations.SendAsync(new GameRemovePartyMemberCommand(Dispatch(Inviter), new(GameMessageMetadata.Empty, 1)));
            await DriveCallPartyMemberRealtimeInfo(sessions, fixture, Notifications, NullLogger<CallPartyMemberRealtimeInfoHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new CallPartyMemberRealtimeInfoRequest(GameMessageMetadata.Empty), CancellationToken.None);
            var settings = Settings();
            var title = new PartyTitle([0x41]);
            await DriveSetPartyInfo(sessions, Presence, fixture, Notifications, NullLogger<SetPartyInfoHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new SetPartyInfoRequest(GameMessageMetadata.Empty, settings, title), CancellationToken.None);

            Assert.Equal(
                [
                    "invite:1->2:64",
                    "accept:2<-1",
                    "query:2", // The whole party is located once over its current party facts.
                    "leave:2",
                    "query:1",
                    "query:1", // The ordinary flow then reads the roster for its target.
                    "lead:1->2",
                    "query:1",
                    "query:1",
                    "remove:1->2",
                    "query:1",
                    "settings:1:2:41",
                ],
                domain.Writes);
        });
    }

    [Fact]
    public async Task Party_realtime_query_dispatches_a_command_and_notifies_only_the_requester()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = new SessionDirectory(Inviter, Invitee);
            var snapshot = Snapshot();
            var commands = new RecordingCommandBus(snapshot: snapshot);
            var published = new RecordingNotificationBus();

            await DriveCallPartyMemberRealtimeInfo(
                    sessions,
                    commands,
                    new TestProjection(published),
                    NullLogger<CallPartyMemberRealtimeInfoHandler>.Instance)
                .HandleAsync(
                    Dispatch(Inviter),
                    new CallPartyMemberRealtimeInfoRequest(GameMessageMetadata.Empty),
                    CancellationToken.None);

            Assert.IsType<GameCallPartyMemberRealtimeInfoCommand>(Assert.Single(commands.Commands));
            var publication = Assert.Single(published.Publications);
            Assert.IsType<PartyRealtimeChanged>(publication.Notification);
            var audience = Assert.IsType<NotificationAudience.TheseSessions>(publication.Audience);
            Assert.Equal([Inviter], audience.Sessions);
        });
    }

    [Fact]
    public async Task Party_handlers_dispatch_the_six_party_write_commands()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = new SessionDirectory(Inviter, Invitee);
            var commands = new RecordingCommandBus();

            await DriveRequestPeer(sessions, Presence, commands, Notifications,
                    NullLogger<RequestPeerHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new RequestPeerRequest(
                    GameMessageMetadata.Empty, Invitee.WireUserId, 0, 64, []),
                    CancellationToken.None);
            await DriveResponsePeer(sessions, Presence, commands, Notifications,
                    NullLogger<ResponsePeerHandler>.Instance)
                .HandleAsync(Dispatch(Invitee), new ResponsePeerRequest(
                    GameMessageMetadata.Empty, Inviter.WireUserId, 0, 0, []),
                    CancellationToken.None);
            await DriveLeaveParty(sessions, commands, Notifications,
                    NullLogger<LeavePartyHandler>.Instance)
                .HandleAsync(Dispatch(Invitee), new LeavePartyRequest(GameMessageMetadata.Empty),
                    CancellationToken.None);
            await DriveChangePartyLeader(
                    sessions, new RecordingParties(), commands, Notifications,
                    NullLogger<ChangePartyLeaderHandler>.Instance)
                .HandleAsync(Dispatch(Inviter),
                    new ChangePartyLeaderRequest(GameMessageMetadata.Empty, 1),
                    CancellationToken.None);
            var sender = new RecordingSender();
            await DriveRemovePartyMember(
                    sessions, new RecordingParties(), commands, Notifications, sender,
                    NullLogger<RemovePartyMemberHandler>.Instance)
                .HandleAsync(Dispatch(Inviter),
                    new RemovePartyMemberRequest(GameMessageMetadata.Empty, 1),
                    CancellationToken.None);
            await DriveSetPartyInfo(sessions, Presence, commands, Notifications,
                    NullLogger<SetPartyInfoHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new SetPartyInfoRequest(
                    GameMessageMetadata.Empty, Settings(), new PartyTitle([0x41])),
                    CancellationToken.None);

            Assert.Collection(
                commands.Commands,
                command => Assert.IsType<GameRequestPeerCommand>(command),
                command => Assert.IsType<GameResponsePeerCommand>(command),
                command => Assert.IsType<GameLeavePartyCommand>(command),
                command => Assert.IsType<GameChangePartyLeaderCommand>(command),
                command => Assert.IsType<GameRemovePartyMemberCommand>(command),
                command =>
                {
                    var set = Assert.IsType<GameSetPartyInfoCommand>(command);
                    Assert.Equal(Settings(), set.Input.Settings);
                    Assert.Equal(new PartyTitle([0x41]), set.Input.Title);
                });
        });
    }

    [Fact]
    public async Task Removing_an_empty_slot_returns_the_native_error_without_a_domain_write()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = new SessionDirectory(Inviter, Invitee);
            var commands = new RecordingCommandBus();
            var sender = new RecordingSender();
            var parties = new RecordingParties();

            await DriveRemovePartyMember(
                    sessions,
                    parties,
                    commands,
                    Notifications,
                    sender,
                    NullLogger<RemovePartyMemberHandler>.Instance)
                .HandleAsync(
                    Dispatch(Inviter),
                    new RemovePartyMemberRequest(GameMessageMetadata.Empty, 7),
                    CancellationToken.None);

            Assert.IsType<GameRemovePartyMemberCommand>(Assert.Single(commands.Commands));
            Assert.Equal(["query:1", "query:1"], parties.Writes);
            Assert.IsType<RemovePartyMemberRefusal>(Assert.Single(sender.Sent));
        });
    }

    [Fact]
    public async Task Set_udp_before_character_selection_is_stored_on_the_game_session()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var connection = new NetworkConnectionContext(
                new NetworkConnectionId("pre-selection"), NetworkEndpointKind.Game, 10011);
            var online = AuthenticatedSession(connection);
            var endpoints = new RecordingSessionEndpoints();

            await DriveSetUdpIpPort(
                    new FixedOnlineUsers(online),
                    endpoints,
                    NullLogger<SetUdpIpPortHandler>.Instance)
                .HandleAsync(
                    new NetworkDispatchContext(connection),
                    new SetUdpIpPortRequest(GameMessageMetadata.Empty, Endpoint(), []),
                    CancellationToken.None);

            Assert.Equal(Endpoint(), endpoints.Find(online.SessionIdentity));
            Assert.Null(online.CurrentCharacterId);
        });
    }

    [Fact]
    public async Task Set_udp_advertises_the_observed_tcp_address_instead_of_a_reported_adapter()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var reported = Endpoint();
            var connection = new NetworkConnectionContext(
                Inviter.ConnectionId,
                NetworkEndpointKind.Game,
                Inviter.ListenerPort,
                System.Net.IPAddress.Loopback);
            var online = AuthenticatedSession(connection);
            var endpoints = new RecordingSessionEndpoints();

            await DriveSetUdpIpPort(
                    new FixedOnlineUsers(online),
                    endpoints,
                    NullLogger<SetUdpIpPortHandler>.Instance)
                .HandleAsync(
                    new NetworkDispatchContext(connection),
                    new SetUdpIpPortRequest(GameMessageMetadata.Empty, reported, []),
                    CancellationToken.None);

            var observed = Assert.IsType<SessionEndpoint>(endpoints.Find(online.SessionIdentity));
            Assert.Equal(new SessionIpv4Address(127, 0, 0, 1), observed.InnerAddress);
            Assert.Equal(new SessionIpv4Address(127, 0, 0, 1), observed.OuterAddress);
            Assert.Equal(reported.Port, observed.Port);
            Assert.Equal(0, observed.NatType);
            Assert.Equal(1500U, observed.Mtu);
        });
    }

    [Theory]
    [InlineData("203.0.113.9", 203, 0, 113, 9)]
    [InlineData("::ffff:198.51.100.7", 198, 51, 100, 7)]
    public void Endpoint_observation_canonicalizes_supported_tcp_remote_addresses(
        string remoteAddress,
        byte a,
        byte b,
        byte c,
        byte d)
    {
        var reported = Endpoint();

        var observed = SessionEndpointObservation.FromConnection(
            reported,
            System.Net.IPAddress.Parse(remoteAddress));

        var expectedAddress = new SessionIpv4Address(a, b, c, d);
        Assert.Equal(expectedAddress, observed.InnerAddress);
        Assert.Equal(expectedAddress, observed.OuterAddress);
        Assert.Equal(reported.Port, observed.Port);
        Assert.Equal(0, observed.NatType);
        Assert.Equal(1500U, observed.Mtu);
    }

    [Fact]
    public async Task Non_party_peer_types_never_write_domain_state()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = new SessionDirectory(Inviter, Invitee);
            var commands = new RecordingCommandBus();

            await DriveRequestPeer(sessions, Presence, commands, Notifications,
                    NullLogger<RequestPeerHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new RequestPeerRequest(
                    GameMessageMetadata.Empty, Invitee.WireUserId, 1, 90667, []),
                    CancellationToken.None);
            await DriveResponsePeer(sessions, Presence, commands, Notifications,
                    NullLogger<ResponsePeerHandler>.Instance)
                .HandleAsync(Dispatch(Invitee), new ResponsePeerRequest(
                    GameMessageMetadata.Empty, Inviter.WireUserId, 2, 64, []),
                    CancellationToken.None);

            Assert.Empty(commands.Domain.Writes);
        });
    }

    [Fact]
    public async Task A_connection_without_a_current_selected_session_cannot_write_party_state()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var commands = new RecordingCommandBus();
            await DriveLeaveParty(
                    new SessionDirectory(),
                    commands,
                    Notifications,
                    NullLogger<LeavePartyHandler>.Instance)
                .HandleAsync(Dispatch(Invitee), new LeavePartyRequest(GameMessageMetadata.Empty),
                    CancellationToken.None);

            Assert.IsType<GameLeavePartyCommand>(Assert.Single(commands.Commands));
            Assert.Empty(commands.Domain.Writes);
        });
    }

    [Fact]
    public async Task A_stale_leave_frame_cannot_bind_to_the_current_party()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var commands = new RecordingCommandBus();
            var stale = new GameMessageMetadata(1, 0x000F, 0, 9)
            {
                InboundOrdinal = 2,
                SequenceIsFresh = false,
            };

            await DriveLeaveParty(
                    new SessionDirectory(Invitee),
                    commands,
                    Notifications,
                    NullLogger<LeavePartyHandler>.Instance)
                .HandleAsync(Dispatch(Invitee), new LeavePartyRequest(stale),
                    CancellationToken.None);

            Assert.IsType<GameLeavePartyCommand>(Assert.Single(commands.Commands));
            Assert.Empty(commands.Domain.Writes);
        });
    }

    [Fact]
    public async Task Stale_targeted_party_frames_stop_before_roster_or_session_capture()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var stale = new GameMessageMetadata(1, 0x000F, 0, 9)
            {
                InboundOrdinal = 2,
                SequenceIsFresh = false,
            };
            var sessions = new SessionDirectory(Inviter, Invitee);
            var parties = new RecordingParties();
            var commands = new RecordingCommandBus();
            var sender = new RecordingSender();

            await DriveChangePartyLeader(
                    sessions, parties, commands, Notifications,
                    NullLogger<ChangePartyLeaderHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new ChangePartyLeaderRequest(stale, 1),
                    CancellationToken.None);
            await DriveRemovePartyMember(
                    sessions, parties, commands, Notifications, sender,
                    NullLogger<RemovePartyMemberHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new RemovePartyMemberRequest(stale, 1),
                    CancellationToken.None);

            Assert.Empty(parties.Writes);
            Assert.Collection(commands.Commands,
                command => Assert.IsType<GameChangePartyLeaderCommand>(command),
                command => Assert.IsType<GameRemovePartyMemberCommand>(command));
            Assert.Empty(sender.Sent);
        });
    }

    [Fact]
    public async Task A_target_that_was_missing_on_the_first_lookup_cannot_appear_behind_the_gate()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = new SessionDirectory(Inviter);
            var commands = new RecordingCommandBus();

            await DriveRequestPeer(
                    sessions,
                    Presence,
                    commands,
                    Notifications,
                    NullLogger<RequestPeerHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new RequestPeerRequest(
                    GameMessageMetadata.Empty, Invitee.WireUserId, 0, 64, []),
                    CancellationToken.None);

            sessions.Select(Invitee);
            Assert.Empty(commands.Domain.Writes);
        });
    }

    [Fact]
    public async Task Successful_command_notifications_are_delivered_once()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = new SessionDirectory(Inviter, Invitee);
            var published = new CountingNotificationBus();
            var delivery = new TestProjection(
                published,
                new PartyProjectionStreamRegistry(),
                sessions,
                Presence);
            var commands = new RecordingCommandBus(PartyOperationResult.Success(
                new PartyNotificationPlan(
                    [],
                    Invitation: new PartyInviteNotification(
                        Invitee.CharacterId,
                        Inviter.WireUserId,
                        new PartyInviteDisplayState(64, 188, 0, 0)))));

            await DriveRequestPeer(
                    sessions,
                    Presence,
                    commands,
                    delivery,
                    NullLogger<RequestPeerHandler>.Instance)
                .HandleAsync(
                    Dispatch(Inviter),
                    new RequestPeerRequest(
                        GameMessageMetadata.Empty, Invitee.WireUserId, 0, 64, []),
                    CancellationToken.None)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(1, published.Count);
        });
    }

    [Fact]
    public async Task Set_party_info_sends_one_command_and_delivers_its_result_as_three_full_state_phases()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = new SessionDirectory(Inviter, Invitee);
            var published = new CountingNotificationBus();
            var snapshot = Snapshot() with { Revision = 4 };
            var commands = new RecordingCommandBus(PartyOperationResult.Success(
                new PartyNotificationPlan(
                    [],
                    snapshot,
                    SettingsChange: PartySettingsChangeKind.Updated)));

            await DriveSetPartyInfo(
                    sessions,
                    Presence,
                    commands,
                    new TestProjection(
                        published,
                        new PartyProjectionStreamRegistry(),
                        sessions,
                        Presence),
                    NullLogger<SetPartyInfoHandler>.Instance)
                .HandleAsync(
                    Dispatch(Inviter),
                    new SetPartyInfoRequest(
                        GameMessageMetadata.Empty, snapshot.Settings, snapshot.Title),
                    CancellationToken.None)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2));

            Assert.IsType<GameSetPartyInfoCommand>(Assert.Single(commands.Commands));
            // This isolated delivery has not observed a full baseline for the generation, so the
            // update is deliberately promoted to the three full-state phases.
            Assert.Equal(3, published.Count);
        });
    }

    [Fact]
    public async Task Missing_selected_session_never_writes_or_projects_from_any_new_party_cmd()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = new SessionDirectory();
            var fixture = new RecordingCommandBus();
            var events = new CountingNotificationBus();
            var projection = new TestProjection(events);
            await DriveRequestPeer(sessions, Presence, fixture, projection, NullLogger<RequestPeerHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new RequestPeerRequest(GameMessageMetadata.Empty, Invitee.WireUserId, 0, 64, []), default);
            await DriveResponsePeer(sessions, Presence, fixture, projection, NullLogger<ResponsePeerHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new ResponsePeerRequest(GameMessageMetadata.Empty, Invitee.WireUserId, 0, 0, []), default);
            await DriveSetPartyInfo(sessions, Presence, fixture, projection, NullLogger<SetPartyInfoHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new SetPartyInfoRequest(GameMessageMetadata.Empty, Settings(), PartyTitle.Empty), default);
            await DriveCallPartyMemberRealtimeInfo(sessions, fixture, projection, NullLogger<CallPartyMemberRealtimeInfoHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new CallPartyMemberRealtimeInfoRequest(GameMessageMetadata.Empty), default);
            Assert.Equal(4, fixture.Commands.Count);
            Assert.Empty(fixture.Domain.Writes);
            Assert.Equal(0, events.Count);
        });
    }

    [Fact]
    public async Task New_cmd_and_legacy_consumers_share_full_state_watermark_and_disband_tombstone()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = new SessionDirectory(Inviter, Invitee);
            var published = new CountingNotificationBus();
            var projection = new TestProjection(published, new PartyProjectionStreamRegistry(), sessions, Presence);
            var first = Snapshot() with { Revision = 4 };
            var fixture = new RecordingCommandBus(PartyOperationResult.Success(new PartyNotificationPlan(
                [], first, SettingsChange: PartySettingsChangeKind.Created)));
            await DriveSetPartyInfo(sessions, Presence, fixture, projection, NullLogger<SetPartyInfoHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new SetPartyInfoRequest(GameMessageMetadata.Empty, first.Settings, first.Title), default);
            Assert.Equal(3, published.Count);
            var stream = ((IPartyProjectionStateDomainService)projection.Streams).StreamOf(first.Generation);
            Assert.Same(projection.Streams.GetOrAdd(first.Generation), stream);
            Assert.Equal(2, stream.FullStateMembers.Count);

            PartyNotificationDelivery legacy = projection;
            await legacy.DeliverAsync(new PartyNotificationPlan([], first with { Revision = 5 },
                SettingsChange: PartySettingsChangeKind.Updated), default);
            Assert.Equal(4, published.Count); // full baseline from new CMD avoids promotion in old consumer
            Assert.Equal(5, stream.LatestRevision);
            await DriveCallPartyMemberRealtimeInfo(sessions, new RecordingCommandBus(snapshot: first), projection,
                    NullLogger<CallPartyMemberRealtimeInfoHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new CallPartyMemberRealtimeInfoRequest(GameMessageMetadata.Empty), default);
            Assert.Equal(4, published.Count); // new query sees the old consumer's later watermark

            await legacy.DeliverAsync(new PartyNotificationPlan([Inviter.CharacterId, Invitee.CharacterId],
                ClearPartyId: first.PartyId, ClearPartyGeneration: first.Generation, ClearPartyRevision: 6), default);
            Assert.Equal(6, stream.LatestRevision);
            Assert.Empty(stream.CurrentMembers);
            Assert.Empty(stream.FullStateMembers);
            Assert.Equal(5, published.Count);
            await DriveSetPartyInfo(sessions, Presence, fixture, projection, NullLogger<SetPartyInfoHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new SetPartyInfoRequest(GameMessageMetadata.Empty, first.Settings, first.Title), default);
            Assert.Equal(5, published.Count); // old disband tombstone suppresses a delayed new CMD projection
            Assert.Equal(6, stream.LatestRevision);
        });
    }

    private sealed class TestProjection(IEventService events, PartyProjectionStreamRegistry? streams = null,
        ISelectedSessionDomainService? sessions = null, ITownAreaDomainService? town = null)
    {
        internal IEventService Events { get; } = events;
        internal PartyProjectionStreamRegistry Streams { get; } = streams ?? new();
        private ISelectedSessionDomainService? Sessions { get; } = sessions;
        private ITownAreaDomainService? Town { get; } = town;
        public static implicit operator PartyNotificationDelivery(TestProjection value) =>
            global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<PartyNotificationDelivery>(
                value.Events, value.Streams, value.Sessions, value.Town);
    }

    private sealed class DirectBus<T>(ICommandHandler<T, bool> handler, RecordingCommandBus? recording = null) : ICommandBus
        where T : class, IProtocolCommand<bool>
    {
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

        public async ValueTask<R> SendAsync<R>(ICommand<R> value, CancellationToken ct = default) where R : notnull
        {
            var command = Assert.IsType<T>(value);
            recording?.Commands.Add(command);
            var request = new CommandRequestContext.ProtocolNonEffect(command.Dispatch, command.Message, command.Frame, () => true);
            return (R)(object)await handler.HandleAsync(new(Guid.NewGuid(), typeof(T), handler.GetType())
                { Request = request }, command, ct);
        }
    }

    private static LeavePartyHandler DriveLeaveParty(ISelectedSessionDomainService sessions,
        RecordingCommandBus commands, TestProjection projection, ILogger<LeavePartyHandler> _, ITownAreaDomainService? townAreas = null) =>
        new(PartyMutationTestDriver.Bus(PartyMutationTestDriver.Handler(sessions, commands.Domain, townAreas ?? Presence,
            projection.Events, projection.Streams), commands.Commands.Add));
    private static ChangePartyLeaderHandler DriveChangePartyLeader(ISelectedSessionDomainService sessions, IPartyService parties,
        RecordingCommandBus commands, TestProjection projection, ILogger<ChangePartyLeaderHandler> _, ITownAreaDomainService? townAreas = null) =>
        new(PartyMutationTestDriver.Bus(PartyMutationTestDriver.Handler(sessions, parties, townAreas ?? Presence,
            projection.Events, projection.Streams), commands.Commands.Add));
    private static RemovePartyMemberHandler DriveRemovePartyMember(ISelectedSessionDomainService sessions, IPartyService parties,
        RecordingCommandBus commands, TestProjection projection, IGameMessageSender sender, ILogger<RemovePartyMemberHandler> _, ITownAreaDomainService? townAreas = null) =>
        new(PartyMutationTestDriver.Bus(PartyMutationTestDriver.Handler(sessions, parties, townAreas ?? Presence,
            projection.Events, projection.Streams, sender), commands.Commands.Add));

    private static SetUdpIpPortHandler DriveSetUdpIpPort(IOnlineUserQueryDomainService online,
        IGameSessionEndpointDomainService endpoints, ILogger<SetUdpIpPortHandler> _) =>
        new(new DirectBus<GameSetUdpIpPortCommand>(new GameSetUdpIpPortCommandHandler(online,endpoints,NullLogger<GameSetUdpIpPortCommandHandler>.Instance)));

    private static RequestPeerHandler DriveRequestPeer(ISelectedSessionDomainService sessions,
        ITownAreaDomainService town, RecordingCommandBus commands, TestProjection projection, ILogger<RequestPeerHandler> _) =>
        new(new DirectBus<GameRequestPeerCommand>(new GameRequestPeerCommandHandler(sessions,            town,commands.Domain,new FixedPartyMemberStatus(),new global::Dfo.Server.Testing.TestCommandProjections(projection.Events),projection.Streams,            ProtocolAlgorithmFixture.Default<IOutboundOrdering>(),TestParties.Runs(),NullLogger<GameRequestPeerCommandHandler>.Instance), commands));

    private static ResponsePeerHandler DriveResponsePeer(ISelectedSessionDomainService sessions,
        ITownAreaDomainService town, RecordingCommandBus commands, TestProjection projection, ILogger<ResponsePeerHandler> _) =>
        new(new DirectBus<GameResponsePeerCommand>(new GameResponsePeerCommandHandler(sessions,            town,commands.Domain,new global::Dfo.Server.Testing.TestCommandProjections(projection.Events),projection.Streams,            ProtocolAlgorithmFixture.Default<IOutboundOrdering>(),TestParties.Runs(),NullLogger<GameResponsePeerCommandHandler>.Instance), commands));

    private static SetPartyInfoHandler DriveSetPartyInfo(ISelectedSessionDomainService sessions,
        ITownAreaDomainService town, RecordingCommandBus commands, TestProjection projection, ILogger<SetPartyInfoHandler> _) =>
        new(new DirectBus<GameSetPartyInfoCommand>(new GameSetPartyInfoCommandHandler(sessions,            town,commands.Domain,new global::Dfo.Server.Testing.TestCommandProjections(projection.Events),projection.Streams,            ProtocolAlgorithmFixture.Default<IOutboundOrdering>(),NullLogger<GameSetPartyInfoCommandHandler>.Instance), commands));

    private static CallPartyMemberRealtimeInfoHandler DriveCallPartyMemberRealtimeInfo(ISelectedSessionDomainService sessions,
        RecordingCommandBus commands, TestProjection projection, ILogger<CallPartyMemberRealtimeInfoHandler> _) =>
        new(new DirectBus<GameCallPartyMemberRealtimeInfoCommand>(new GameCallPartyMemberRealtimeInfoCommandHandler(sessions,            commands.Domain,new global::Dfo.Server.Testing.TestCommandProjections(projection.Events),projection.Streams,            NullLogger<GameCallPartyMemberRealtimeInfoCommandHandler>.Instance), commands));

    private static SessionEndpoint Endpoint() => new(
        5,
        new(172, 17, 208, 1),
        new(172, 17, 208, 1),
        30976,
        1472);

    private static OnlineUserSnapshot AuthenticatedSession(NetworkConnectionContext connection) =>
        new(
            new OnlineUserId(Guid.NewGuid()),
            connection,
            new AccountId(900),
            CurrentCharacterId: null,
            OnlineUserState.Authenticated,
            Generation: 2);

    private static PartySnapshot Snapshot() => new(
        0x1234,
        7,
        3,
        Inviter.WireUserId,
        new PartySettings(
            [0, 0, 4, 0, 0, 0, 0, 5, 0, (byte)PartyCategory.Normal, 0xFF, 0xFF]),
        PartyTitle.Empty,
        [
            new PartyMemberSnapshot(Character(Inviter), 0, Endpoint(), 73),
            new PartyMemberSnapshot(Character(Invitee), 1, null, 45),
        ]);

    private static PartyCharacterSnapshot Character(SelectedSessionIdentity session) =>
        new(
            session.AccountId,
            session.CharacterId,
            session.WireUserId,
            new PartyDirectoryScope(11, 1, 1));

    private static PartySettings Settings() => new(
        [0, 0, 4, 0, 0, 0, 0, 5, 0, (byte)PartyCategory.Normal, 0xFF, 0xFF]);

    private static NetworkDispatchContext Dispatch(SelectedSessionIdentity session) =>
        new(new NetworkConnectionContext(
            session.ConnectionId, NetworkEndpointKind.Game, session.ListenerPort));

    private static SelectedSessionIdentity Inviter { get; } = SelectedSessionIdentity.Create(
        new AccountId(1), new CharacterId(1), new NetworkConnectionId("inviter"), 1, 10011);

    private static SelectedSessionIdentity Invitee { get; } = SelectedSessionIdentity.Create(
        new AccountId(2), new CharacterId(2), new NetworkConnectionId("invitee"), 1, 10011);

    private static TestProjection Notifications { get; } =
        new(new NoopNotificationBus());

    private static ISessionPresenceDomainService Presence { get; } =
        new FixedPresence(Inviter, Invitee);

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
        public IReadOnlyList<SessionPresence> Query(SessionPresenceScope scope) => values.Values.ToArray();
    }

    private sealed class NoopNotificationBus : Dfo.Server.Testing.ProjectionEventService
    {
        public override ValueTask<NotificationDelivery> ProjectAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new NotificationDelivery([]));
    }

    private sealed class FixedOnlineUsers(OnlineUserSnapshot session)
        : IOnlineUserQueryDomainService
    {
        public OnlineUserSnapshot GetRequired(OnlineUserId onlineUserId) =>
            onlineUserId == session.OnlineUserId
                ? session
                : throw new KeyNotFoundException();

        public bool TryGetByConnection(
            NetworkConnectionId connectionId,
            out OnlineUserSnapshot? onlineUser)
        {
            onlineUser = connectionId == session.Connection.ConnectionId ? session : null;
            return onlineUser is not null;
        }
    }

    private sealed class RecordingSessionEndpoints : IGameSessionEndpointDomainService
    {
        private readonly Dictionary<GameSessionIdentity, SessionEndpoint> values = [];

        public bool TryObserve(GameSessionIdentity session, SessionEndpoint endpoint)
        {
            values[session] = endpoint;
            return true;
        }

        public SessionEndpoint? Find(GameSessionIdentity session) => values.GetValueOrDefault(session);
    }

    /// <summary>
    /// What a real registration adapter registered, read from its outbox (an explicit delivery with its direct messages
    /// only, as before); every call goes to the real protocol sender over it (TASK-25).
    /// </summary>
    private sealed class RecordingSender : IGameMessageSender
    {
        private readonly RecordedOutbound outbound = new();

        public List<IGameServerMessage> Sent => [.. outbound.CallMessages.Cast<IGameServerMessage>()];

        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendCmdAsync(connectionId, message, cancellationToken);
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendNotiAsync(connectionId, message, cancellationToken);
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken) =>
            outbound.Game.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);
    }

    private sealed class RecordingCommandBus(
        PartyOperationResult? result = null, PartySnapshot? snapshot = null, RecordingParties? domain = null) : ICommandBus

    {
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

        public RecordingParties Domain { get; } = domain ?? new RecordingParties(result, snapshot);
        public List<ICommand> Commands { get; } = [];

        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            Commands.Add(command);
            return ValueTask.FromResult(
                (TResult)(object)(result ?? PartyOperationResult.Success()));
        }
    }

    private sealed record Publication(NotificationAudience Audience, object Notification);

    private sealed class RecordingNotificationBus : Dfo.Server.Testing.ProjectionEventService
    {
        public List<Publication> Publications { get; } = [];

        public override ValueTask<NotificationDelivery> ProjectAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
        {
            Publications.Add(new Publication(audience, notification));
            return ValueTask.FromResult(new NotificationDelivery([]));
        }
    }

    private sealed class CountingNotificationBus : Dfo.Server.Testing.ProjectionEventService
    {
        public int Count { get; private set; }

        public override ValueTask<NotificationDelivery> ProjectAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
        {
            Count++;
            return ValueTask.FromResult(new NotificationDelivery([]));
        }
    }

    private sealed class SessionDirectory(params SelectedSessionIdentity[] initial)
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

        private readonly Dictionary<AccountId, SelectedSessionIdentity> sessions =
            initial.ToDictionary(value => value.AccountId);

        public TaskCompletionSource WireLookupObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource TwoWireLookupsObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ConnectionLookupObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int wireLookups;

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

        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId)
        {
            WireLookupObserved.TrySetResult();
            if (Interlocked.Increment(ref wireLookups) >= 2)
                TwoWireLookupsObserved.TrySetResult();
            return sessions.Values.SingleOrDefault(value => value.WireUserId == wireUserId);
        }

        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId)
        {
            ConnectionLookupObserved.TrySetResult();
            return sessions.Values.SingleOrDefault(value => value.ConnectionId == connectionId);
        }
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public static NoopAsyncDisposable Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingParties(PartyOperationResult? result = null, PartySnapshot? snapshot = null) : IPartyService
    {
        public PartyMembershipSnapshot? FindByKey(PartyKey key) => null;
        public PartyKey KeyOf(CharacterId characterId) => throw new NotSupportedException();
        public List<string> Writes { get; } = [];

        public void UpdateDirectoryPresence(CharacterId characterId, PartyDirectoryScope? scope) { }

        public IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query) => [Snapshot()];

        public PartyOperationResult CreateOrUpdateSettings(
            PartyCharacterSnapshot character,
            PartySettings settings,
            PartyTitle title)
        {
            Writes.Add(
                $"settings:{character.WireUserId}:{(byte)settings.Category}:{Convert.ToHexString(title.ToBytes())}");
            return result ?? PartyOperationResult.Success();
        }

        public PartyOperationResult RequestInvite(
            PartyCharacterSnapshot inviter,
            PartyCharacterSnapshot target,
            PartyInviteDisplayState display)
        {
            Writes.Add($"invite:{inviter.WireUserId}->{target.WireUserId}:{display.ReviveCoinCount}");
            return result ?? PartyOperationResult.Success();
        }

        public PartyOperationResult AcceptInvite(
            PartyCharacterSnapshot invitee,
            PartyCharacterSnapshot inviter)
        {
            Writes.Add($"accept:{invitee.WireUserId}<-{inviter.WireUserId}");
            return result ?? PartyOperationResult.Success();
        }

        public PartyOperationResult TransferLeadership(
            CharacterId currentLeaderCharacterId,
            CharacterId nextLeaderCharacterId)
        {
            Writes.Add($"lead:{currentLeaderCharacterId.Value}->{nextLeaderCharacterId.Value}");
            return result ?? PartyOperationResult.Success();
        }

        public PartyOperationResult RemoveMember(
            CharacterId currentLeaderCharacterId,
            CharacterId targetCharacterId)
        {
            Writes.Add($"remove:{currentLeaderCharacterId.Value}->{targetCharacterId.Value}");
            return result ?? PartyOperationResult.Success();
        }

        public PartyOperationResult LeaveParty(CharacterId characterId)
        {
            Writes.Add($"leave:{characterId.Value}");
            return result ?? PartyOperationResult.Success();
        }

        public PartyOperationResult CleanupCharacter(CharacterId characterId) =>
            throw new NotSupportedException();

        public PartySnapshot FindParty(PartyCharacterSnapshot character)
        {
            Writes.Add($"query:{character.CharacterId.Value}");
            return snapshot ?? Snapshot();
        }

    }

    private sealed class NoPartyEndpoints : IPartyMemberSessionEndpointSource
    {
        public SessionEndpoint? FindThroughCurrentSession(CharacterId characterId) => null;
    }

    private sealed class FixedPartyMemberStatus : IPartyMemberStatusDomainService
    {
        public ValueTask<PartyInviteDisplayState> InvitationOfAsync(
            PartyCharacterSnapshot character,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PartyInviteDisplayState(64, 188, 0, 0));

        public byte CurrentHpPercentOf(CharacterId characterId) => 73;
    }
}
