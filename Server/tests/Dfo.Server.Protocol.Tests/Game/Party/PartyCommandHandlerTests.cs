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

public sealed class PartyCommandHandlerTests
{
    [Fact]
    public async Task Leaving_party_in_a_run_detaches_only_the_departing_character()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var parties = new PartyService(
                new FixedPartyMemberStatus(), new PartyModeRuleService(),
                TimeProvider.System, new NoPartyEndpoints());
            var scope = new PartyDirectoryScope(11, 1, 1);
            var first = Character(Inviter) with { DirectoryScope = scope };
            var second = Character(Invitee) with { DirectoryScope = scope };
            Assert.True(parties.RequestInvite(first, second,
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(second, first).Succeeded);
            var runs = new DungeonRunDomainService(parties);
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
            var context = new CommandExecutionContext(
                Guid.NewGuid(), typeof(object), typeof(object), CommandExecutionKind.Direct);

            var result = await new LeavePartyCommandHandler(parties, runs)
                .HandleAsync(context, new LeavePartyCommand(Inviter.CharacterId), CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Null(runs.Find(Inviter.CharacterId));
            Assert.Equal(run.RunId, runs.Find(Invitee.CharacterId)!.RunId);
            Assert.Equal([Inviter, Invitee], deliveries.Find(run.RunId));
        });
    }

    [Fact]
    public async Task Removing_a_member_in_a_run_detaches_only_the_removed_character()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var parties = new PartyService(
                new FixedPartyMemberStatus(), new PartyModeRuleService(),
                TimeProvider.System, new NoPartyEndpoints());
            var scope = new PartyDirectoryScope(11, 1, 1);
            var leader = Character(Inviter) with { DirectoryScope = scope };
            var target = Character(Invitee) with { DirectoryScope = scope };
            Assert.True(parties.RequestInvite(leader, target,
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(target, leader).Succeeded);
            var runs = new DungeonRunDomainService(parties);
            var layout = new DungeonLayout(
                10000, 0, 1, 1, new MazeCell(0, 0), new MazeCell(0, 0),
                61000, 1, new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(61000)});
            var run = runs.PreparePartyEntry(
                [new DungeonRunParticipant(Inviter.AccountId, Inviter.CharacterId, 0, true),
                 new DungeonRunParticipant(Invitee.AccountId, Invitee.CharacterId, 1, false)],
                layout, 0, DungeonCellSeed.CompleteEmpty(layout.StartCell, layout.StartRoomMapId),
                DungeonRunId.New(), DateTimeOffset.UnixEpoch).Run;
            var context = new CommandExecutionContext(
                Guid.NewGuid(), typeof(object), typeof(object), CommandExecutionKind.Direct);

            var result = await new RemovePartyMemberCommandHandler(parties, runs)
                .HandleAsync(context, new RemovePartyMemberCommand(
                    Inviter.CharacterId, Invitee.CharacterId), CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(run.RunId, runs.Find(Inviter.CharacterId)!.RunId);
            Assert.Null(runs.Find(Invitee.CharacterId));
        });
    }

    [Fact]
    public async Task Commands_are_the_only_route_from_protocol_to_party_writes()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var domain = new RecordingParties();
            var context = new CommandExecutionContext(
                Guid.NewGuid(), typeof(object), typeof(object), CommandExecutionKind.Direct);

            await new RequestPartyInviteCommandHandler(domain, new FixedPartyMemberStatus()).HandleAsync(
                context, new(Character(Inviter), Character(Invitee)), CancellationToken.None);
            await new AcceptPartyInviteCommandHandler(domain).HandleAsync(
                context, new(Character(Invitee), Character(Inviter)), CancellationToken.None);
            await new LeavePartyCommandHandler(domain).HandleAsync(
                context, new(Invitee.CharacterId), CancellationToken.None);
            await new TransferPartyLeadershipCommandHandler(domain).HandleAsync(
                context, new(Inviter.CharacterId, Invitee.CharacterId), CancellationToken.None);
            await new RemovePartyMemberCommandHandler(domain).HandleAsync(
                context, new(Inviter.CharacterId, Invitee.CharacterId), CancellationToken.None);
            await new QueryPartyRealtimeCommandHandler(domain).HandleAsync(
                context, new(Character(Inviter)), CancellationToken.None);
            var settings = Settings();
            var title = new PartyTitle([0x41]);
            await new SetPartyInfoCommandHandler(domain).HandleAsync(
                context, new(Character(Inviter), settings, title), CancellationToken.None);

            Assert.Equal(
                [
                    "invite:1->2:64",
                    "accept:2<-1",
                    "leave:2",
                    "lead:1->2",
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
            var commands = new RealtimeCommandBus(snapshot);
            var published = new RecordingNotificationBus();

            await new CallPartyMemberRealtimeInfoHandler(
                    sessions,
                    commands,
                    new PartyNotificationDelivery(published),
                    NullLogger<CallPartyMemberRealtimeInfoHandler>.Instance)
                .HandleAsync(
                    Dispatch(Inviter),
                    new CallPartyMemberRealtimeInfoRequest(GameMessageMetadata.Empty),
                    CancellationToken.None);

            Assert.IsType<QueryPartyRealtimeCommand>(Assert.Single(commands.Commands));
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

            await new RequestPeerHandler(sessions, Presence, commands, Notifications,
                    NullLogger<RequestPeerHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new RequestPeerRequest(
                    GameMessageMetadata.Empty, Invitee.WireUserId, 0, 64, []),
                    CancellationToken.None);
            await new ResponsePeerHandler(sessions, Presence, commands, Notifications,
                    NullLogger<ResponsePeerHandler>.Instance)
                .HandleAsync(Dispatch(Invitee), new ResponsePeerRequest(
                    GameMessageMetadata.Empty, Inviter.WireUserId, 0, 0, []),
                    CancellationToken.None);
            await new LeavePartyHandler(sessions, commands, Notifications,
                    NullLogger<LeavePartyHandler>.Instance)
                .HandleAsync(Dispatch(Invitee), new LeavePartyRequest(GameMessageMetadata.Empty),
                    CancellationToken.None);
            await new ChangePartyLeaderHandler(
                    sessions, new RecordingParties(), commands, Notifications,
                    NullLogger<ChangePartyLeaderHandler>.Instance)
                .HandleAsync(Dispatch(Inviter),
                    new ChangePartyLeaderRequest(GameMessageMetadata.Empty, 1),
                    CancellationToken.None);
            var sender = new RecordingSender();
            await new RemovePartyMemberHandler(
                    sessions, new RecordingParties(), commands, Notifications, sender,
                    NullLogger<RemovePartyMemberHandler>.Instance)
                .HandleAsync(Dispatch(Inviter),
                    new RemovePartyMemberRequest(GameMessageMetadata.Empty, 1),
                    CancellationToken.None);
            await new SetPartyInfoHandler(sessions, Presence, commands, Notifications,
                    NullLogger<SetPartyInfoHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new SetPartyInfoRequest(
                    GameMessageMetadata.Empty, Settings(), new PartyTitle([0x41])),
                    CancellationToken.None);

            Assert.Collection(
                commands.Commands,
                command => Assert.IsType<RequestPartyInviteCommand>(command),
                command => Assert.IsType<AcceptPartyInviteCommand>(command),
                command => Assert.IsType<LeavePartyCommand>(command),
                command => Assert.IsType<TransferPartyLeadershipCommand>(command),
                command => Assert.IsType<RemovePartyMemberCommand>(command),
                command =>
                {
                    var set = Assert.IsType<SetPartyInfoCommand>(command);
                    Assert.Equal(Settings(), set.Settings);
                    Assert.Equal(new PartyTitle([0x41]), set.Title);
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

            await new RemovePartyMemberHandler(
                    sessions,
                    new RecordingParties(),
                    commands,
                    Notifications,
                    sender,
                    NullLogger<RemovePartyMemberHandler>.Instance)
                .HandleAsync(
                    Dispatch(Inviter),
                    new RemovePartyMemberRequest(GameMessageMetadata.Empty, 7),
                    CancellationToken.None);

            Assert.Empty(commands.Commands);
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

            await new SetUdpIpPortHandler(
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

            await new SetUdpIpPortHandler(
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
    public async Task Non_party_peer_types_never_dispatch_a_command()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = new SessionDirectory(Inviter, Invitee);
            var commands = new RecordingCommandBus();

            await new RequestPeerHandler(sessions, Presence, commands, Notifications,
                    NullLogger<RequestPeerHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new RequestPeerRequest(
                    GameMessageMetadata.Empty, Invitee.WireUserId, 1, 90667, []),
                    CancellationToken.None);
            await new ResponsePeerHandler(sessions, Presence, commands, Notifications,
                    NullLogger<ResponsePeerHandler>.Instance)
                .HandleAsync(Dispatch(Invitee), new ResponsePeerRequest(
                    GameMessageMetadata.Empty, Inviter.WireUserId, 2, 64, []),
                    CancellationToken.None);

            Assert.Empty(commands.Commands);
        });
    }

    [Fact]
    public async Task A_connection_without_a_current_selected_session_cannot_write_party_state()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var commands = new RecordingCommandBus();
            await new LeavePartyHandler(
                    new SessionDirectory(),
                    commands,
                    Notifications,
                    NullLogger<LeavePartyHandler>.Instance)
                .HandleAsync(Dispatch(Invitee), new LeavePartyRequest(GameMessageMetadata.Empty),
                    CancellationToken.None);

            Assert.Empty(commands.Commands);
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

            await new LeavePartyHandler(
                    new SessionDirectory(Invitee),
                    commands,
                    Notifications,
                    NullLogger<LeavePartyHandler>.Instance)
                .HandleAsync(Dispatch(Invitee), new LeavePartyRequest(stale),
                    CancellationToken.None);

            Assert.Empty(commands.Commands);
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

            await new ChangePartyLeaderHandler(
                    sessions, parties, commands, Notifications,
                    NullLogger<ChangePartyLeaderHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new ChangePartyLeaderRequest(stale, 1),
                    CancellationToken.None);
            await new RemovePartyMemberHandler(
                    sessions, parties, commands, Notifications, sender,
                    NullLogger<RemovePartyMemberHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new RemovePartyMemberRequest(stale, 1),
                    CancellationToken.None);

            Assert.Empty(parties.Writes);
            Assert.Empty(commands.Commands);
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

            await new RequestPeerHandler(
                    sessions,
                    Presence,
                    commands,
                    Notifications,
                    NullLogger<RequestPeerHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new RequestPeerRequest(
                    GameMessageMetadata.Empty, Invitee.WireUserId, 0, 64, []),
                    CancellationToken.None);

            sessions.Select(Invitee);
            Assert.Empty(commands.Commands);
        });
    }

    [Fact]
    public async Task Successful_command_notifications_are_delivered_after_both_transition_gates_release()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = new SessionDirectory(Inviter, Invitee);
            var published = new CountingNotificationBus();
            var delivery = new PartyNotificationDelivery(
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

            await new RequestPeerHandler(
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
    public async Task Set_party_info_delivers_its_immutable_result_after_the_transition_gate_releases()
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

            await new SetPartyInfoHandler(
                    sessions,
                    Presence,
                    commands,
                    new PartyNotificationDelivery(
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

            Assert.IsType<SetPartyInfoCommand>(Assert.Single(commands.Commands));
            // This isolated delivery has not observed a full baseline for the generation, so the
            // update is deliberately promoted to the three full-state phases.
            Assert.Equal(3, published.Count);
        });
    }

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

    private static PartyNotificationDelivery Notifications { get; } =
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
        PartyOperationResult? result = null) : ICommandBus
    {
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

    private sealed class RealtimeCommandBus(PartySnapshot snapshot) : ICommandBus
    {
        public List<ICommand> Commands { get; } = [];

        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            Commands.Add(command);
            return ValueTask.FromResult(
                (TResult)(object)new PartyRealtimeQueryResult(snapshot));
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

    private sealed class RecordingParties : IPartyService
    {
        public PartyMembershipSnapshot? FindByKey(PartyKey key) => null;
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
            return PartyOperationResult.Success();
        }

        public PartyOperationResult RequestInvite(
            PartyCharacterSnapshot inviter,
            PartyCharacterSnapshot target,
            PartyInviteDisplayState display)
        {
            Writes.Add($"invite:{inviter.WireUserId}->{target.WireUserId}:{display.ReviveCoinCount}");
            return PartyOperationResult.Success();
        }

        public PartyOperationResult AcceptInvite(
            PartyCharacterSnapshot invitee,
            PartyCharacterSnapshot inviter)
        {
            Writes.Add($"accept:{invitee.WireUserId}<-{inviter.WireUserId}");
            return PartyOperationResult.Success();
        }

        public PartyOperationResult TransferLeadership(
            CharacterId currentLeaderCharacterId,
            CharacterId nextLeaderCharacterId)
        {
            Writes.Add($"lead:{currentLeaderCharacterId.Value}->{nextLeaderCharacterId.Value}");
            return PartyOperationResult.Success();
        }

        public PartyOperationResult RemoveMember(
            CharacterId currentLeaderCharacterId,
            CharacterId targetCharacterId)
        {
            Writes.Add($"remove:{currentLeaderCharacterId.Value}->{targetCharacterId.Value}");
            return PartyOperationResult.Success();
        }

        public PartyOperationResult LeaveParty(CharacterId characterId)
        {
            Writes.Add($"leave:{characterId.Value}");
            return PartyOperationResult.Success();
        }

        public PartyOperationResult CleanupCharacter(CharacterId characterId) =>
            throw new NotSupportedException();

        public PartySnapshot FindParty(PartyCharacterSnapshot character)
        {
            Writes.Add($"query:{character.CharacterId.Value}");
            return Snapshot();
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
