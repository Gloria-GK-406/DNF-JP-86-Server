using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.Party;

public sealed class PartyCommandHandlerTests
{
    [Fact]
    public async Task Commands_are_the_only_route_from_protocol_to_party_writes()
    {
        var domain = new RecordingParties();
        var endpoint = Endpoint();
        var context = new CommandExecutionContext(
            Guid.NewGuid(), typeof(object), typeof(object), CommandExecutionKind.Direct);

        await new UpdatePartyEndpointCommandHandler(domain).HandleAsync(
            context, new(Inviter, endpoint), CancellationToken.None);
        await new RequestPartyInviteCommandHandler(domain, new FixedPartyMemberStatus()).HandleAsync(
            context, new(Inviter, Invitee.WireUserId), CancellationToken.None);
        await new AcceptPartyInviteCommandHandler(domain).HandleAsync(
            context, new(Invitee, Inviter.WireUserId), CancellationToken.None);
        await new LeavePartyCommandHandler(domain).HandleAsync(
            context, new(Invitee), CancellationToken.None);
        await new QueryPartyRealtimeCommandHandler(domain).HandleAsync(
            context, new(Inviter), CancellationToken.None);
        var settings = Settings();
        var title = new PartyTitle([0x41]);
        await new SetPartyInfoCommandHandler(domain).HandleAsync(
            context, new(Inviter, settings, title), CancellationToken.None);

        Assert.Equal(
            [
                "endpoint:1",
                "invite:1->2:64",
                "accept:2<-1",
                "leave:2",
                "query:1",
                "settings:1:2:41",
            ],
            domain.Writes);
    }

    [Fact]
    public async Task Party_realtime_query_dispatches_a_command_and_notifies_only_the_requester()
    {
        var sessions = new SessionDirectory(Inviter, Invitee);
        var snapshot = Snapshot();
        var commands = new RealtimeCommandBus(snapshot);
        var published = new RecordingNotificationBus();

        await new CallPartyMemberRealtimeInfoHandler(
                sessions,
                new SelectedSessionTransitionGate(),
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
        var audience = Assert.IsType<NotificationAudience.TheseCharacters>(publication.Audience);
        Assert.Equal([Inviter.CharacterId], audience.CharacterIds);
    }

    [Fact]
    public async Task Party_handlers_dispatch_the_five_party_write_commands()
    {
        var sessions = new SessionDirectory(Inviter, Invitee);
        var commands = new RecordingCommandBus();
        var gate = new SelectedSessionTransitionGate();

        await new SetUdpIpPortHandler(sessions, gate, commands, Notifications,
                NullLogger<SetUdpIpPortHandler>.Instance)
            .HandleAsync(Dispatch(Inviter), new SetUdpIpPortRequest(
                GameMessageMetadata.Empty, Endpoint(), []), CancellationToken.None);
        await new RequestPeerHandler(sessions, gate, commands, Notifications,
                NullLogger<RequestPeerHandler>.Instance)
            .HandleAsync(Dispatch(Inviter), new RequestPeerRequest(
                GameMessageMetadata.Empty, Invitee.WireUserId, 0, 64, []),
                CancellationToken.None);
        await new ResponsePeerHandler(sessions, gate, commands, Notifications,
                NullLogger<ResponsePeerHandler>.Instance)
            .HandleAsync(Dispatch(Invitee), new ResponsePeerRequest(
                GameMessageMetadata.Empty, Inviter.WireUserId, 0, 0, []),
                CancellationToken.None);
        await new LeavePartyHandler(sessions, gate, commands, Notifications,
                NullLogger<LeavePartyHandler>.Instance)
            .HandleAsync(Dispatch(Invitee), new LeavePartyRequest(GameMessageMetadata.Empty),
                CancellationToken.None);
        await new SetPartyInfoHandler(sessions, gate, commands, Notifications,
                NullLogger<SetPartyInfoHandler>.Instance)
            .HandleAsync(Dispatch(Inviter), new SetPartyInfoRequest(
                GameMessageMetadata.Empty, Settings(), new PartyTitle([0x41])),
                CancellationToken.None);

        Assert.Collection(
            commands.Commands,
            command => Assert.IsType<UpdatePartyEndpointCommand>(command),
            command => Assert.IsType<RequestPartyInviteCommand>(command),
            command => Assert.IsType<AcceptPartyInviteCommand>(command),
            command => Assert.IsType<LeavePartyCommand>(command),
            command =>
            {
                var set = Assert.IsType<SetPartyInfoCommand>(command);
                Assert.Equal(Settings(), set.Settings);
                Assert.Equal(new PartyTitle([0x41]), set.Title);
            });
    }

    [Fact]
    public async Task Set_party_info_rechecks_the_exact_session_after_acquiring_its_gate()
    {
        var sessions = new SessionDirectory(Inviter);
        var commands = new RecordingCommandBus();
        var gate = new SelectedSessionTransitionGate();
        var blocker = await gate.AcquireAsync(
            Inviter.AccountId, UserAccessMode.Exclusive, CancellationToken.None);
        try
        {
            var pending = new SetPartyInfoHandler(
                    sessions,
                    gate,
                    commands,
                    Notifications,
                    NullLogger<SetPartyInfoHandler>.Instance)
                .HandleAsync(
                    Dispatch(Inviter),
                    new SetPartyInfoRequest(
                        GameMessageMetadata.Empty, Settings(), PartyTitle.Empty),
                    CancellationToken.None)
                .AsTask();

            await sessions.ConnectionLookupObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            sessions.Select(Inviter with
            {
                ConnectionId = new NetworkConnectionId("set-info-replacement"),
                ConnectionGeneration = 2,
            });
            await blocker.DisposeAsync();
            blocker = NoopAsyncDisposable.Instance;

            await pending.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            await blocker.DisposeAsync();
        }

        Assert.Empty(commands.Commands);
    }

    [Fact]
    public async Task Non_party_peer_types_never_dispatch_a_command()
    {
        var sessions = new SessionDirectory(Inviter, Invitee);
        var commands = new RecordingCommandBus();
        var gate = new SelectedSessionTransitionGate();

        await new RequestPeerHandler(sessions, gate, commands, Notifications,
                NullLogger<RequestPeerHandler>.Instance)
            .HandleAsync(Dispatch(Inviter), new RequestPeerRequest(
                GameMessageMetadata.Empty, Invitee.WireUserId, 1, 90667, []),
                CancellationToken.None);
        await new ResponsePeerHandler(sessions, gate, commands, Notifications,
                NullLogger<ResponsePeerHandler>.Instance)
            .HandleAsync(Dispatch(Invitee), new ResponsePeerRequest(
                GameMessageMetadata.Empty, Inviter.WireUserId, 2, 64, []),
                CancellationToken.None);

        Assert.Empty(commands.Commands);
    }

    [Fact]
    public async Task A_connection_without_a_current_selected_session_cannot_write_party_state()
    {
        var commands = new RecordingCommandBus();
        await new LeavePartyHandler(
                new SessionDirectory(),
                new SelectedSessionTransitionGate(),
                commands,
                Notifications,
                NullLogger<LeavePartyHandler>.Instance)
            .HandleAsync(Dispatch(Invitee), new LeavePartyRequest(GameMessageMetadata.Empty),
                CancellationToken.None);

        Assert.Empty(commands.Commands);
    }

    [Fact]
    public async Task A_target_that_was_missing_on_the_first_lookup_cannot_appear_behind_the_gate()
    {
        var sessions = new SessionDirectory(Inviter);
        var commands = new RecordingCommandBus();

        await new RequestPeerHandler(
                sessions,
                new SelectedSessionTransitionGate(),
                commands,
                Notifications,
                NullLogger<RequestPeerHandler>.Instance)
            .HandleAsync(Dispatch(Inviter), new RequestPeerRequest(
                GameMessageMetadata.Empty, Invitee.WireUserId, 0, 64, []),
                CancellationToken.None);

        sessions.Select(Invitee);
        Assert.Empty(commands.Commands);
    }

    [Fact]
    public async Task A_target_replaced_before_its_gate_is_acquired_is_not_dispatched()
    {
        var sessions = new SessionDirectory(Inviter, Invitee);
        var commands = new RecordingCommandBus();
        var gate = new SelectedSessionTransitionGate();
        var blocker = await gate.AcquireAsync(
            Invitee.AccountId, UserAccessMode.Exclusive, CancellationToken.None);
        try
        {
            var pending = new RequestPeerHandler(
                    sessions,
                    gate,
                    commands,
                    Notifications,
                    NullLogger<RequestPeerHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new RequestPeerRequest(
                    GameMessageMetadata.Empty, Invitee.WireUserId, 0, 64, []),
                    CancellationToken.None)
                .AsTask();

            await sessions.WireLookupObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            sessions.Select(Invitee with
            {
                ConnectionId = new NetworkConnectionId("replacement"),
                ConnectionGeneration = 2,
            });
            await blocker.DisposeAsync();
            blocker = NoopAsyncDisposable.Instance;

            await pending.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            await blocker.DisposeAsync();
        }

        Assert.Empty(commands.Commands);
    }

    [Fact]
    public async Task A_higher_account_inviter_replaced_while_waiting_for_the_lower_gate_is_not_dispatched()
    {
        var sessions = new SessionDirectory(Inviter, Invitee);
        var commands = new RecordingCommandBus();
        var gate = new SelectedSessionTransitionGate();
        var lowerBlocker = await gate.AcquireAsync(
            Inviter.AccountId, UserAccessMode.Exclusive, CancellationToken.None);
        var inviterBlocker = await gate.AcquireAsync(
            Invitee.AccountId, UserAccessMode.Exclusive, CancellationToken.None);
        try
        {
            var pending = new RequestPeerHandler(
                    sessions,
                    gate,
                    commands,
                    Notifications,
                    NullLogger<RequestPeerHandler>.Instance)
                .HandleAsync(Dispatch(Invitee), new RequestPeerRequest(
                    GameMessageMetadata.Empty, Inviter.WireUserId, 0, 64, []),
                    CancellationToken.None)
                .AsTask();

            await sessions.WireLookupObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            sessions.Select(Invitee with
            {
                ConnectionId = new NetworkConnectionId("higher-account-replacement"),
                ConnectionGeneration = 2,
            });
            await inviterBlocker.DisposeAsync();
            inviterBlocker = NoopAsyncDisposable.Instance;
            await lowerBlocker.DisposeAsync();
            lowerBlocker = NoopAsyncDisposable.Instance;

            await pending.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            await inviterBlocker.DisposeAsync();
            await lowerBlocker.DisposeAsync();
        }

        Assert.Empty(commands.Commands);
    }

    [Fact]
    public async Task Reverse_direction_pair_requests_complete_under_one_stable_gate_order()
    {
        var sessions = new SessionDirectory(Inviter, Invitee);
        var commands = new RecordingCommandBus();
        var gate = new SelectedSessionTransitionGate();
        var lowerBlocker = await gate.AcquireAsync(
            Inviter.AccountId, UserAccessMode.Exclusive, CancellationToken.None);
        try
        {
            var lowToHigh = new RequestPeerHandler(
                    sessions,
                    gate,
                    commands,
                    Notifications,
                    NullLogger<RequestPeerHandler>.Instance)
                .HandleAsync(Dispatch(Inviter), new RequestPeerRequest(
                    GameMessageMetadata.Empty, Invitee.WireUserId, 0, 64, []),
                    CancellationToken.None)
                .AsTask();
            var highToLow = new RequestPeerHandler(
                    sessions,
                    gate,
                    commands,
                    Notifications,
                    NullLogger<RequestPeerHandler>.Instance)
                .HandleAsync(Dispatch(Invitee), new RequestPeerRequest(
                    GameMessageMetadata.Empty, Inviter.WireUserId, 0, 64, []),
                    CancellationToken.None)
                .AsTask();

            await sessions.TwoWireLookupsObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await lowerBlocker.DisposeAsync();
            lowerBlocker = NoopAsyncDisposable.Instance;

            await Task.WhenAll(lowToHigh, highToLow).WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            await lowerBlocker.DisposeAsync();
        }

        Assert.Equal(2, commands.Commands.OfType<RequestPartyInviteCommand>().Count());
    }

    [Fact]
    public async Task Successful_command_notifications_are_delivered_after_both_transition_gates_release()
    {
        var sessions = new SessionDirectory(Inviter, Invitee);
        var gate = new SelectedSessionTransitionGate();
        var published = new GateReentryNotificationBus(gate, Inviter.AccountId);
        var delivery = new PartyNotificationDelivery(published);
        var commands = new RecordingCommandBus(PartyOperationResult.Success(
            new PartyNotificationPlan(
                [],
                Invitation: new PartyInviteNotification(
                    Invitee, Inviter, new PartyInviteDisplayState(64, 188, 0, 0)))));

        await new RequestPeerHandler(
                sessions,
                gate,
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
    }

    [Fact]
    public async Task Set_party_info_delivers_its_immutable_result_after_the_transition_gate_releases()
    {
        var sessions = new SessionDirectory(Inviter, Invitee);
        var gate = new SelectedSessionTransitionGate();
        var published = new GateReentryNotificationBus(gate, Inviter.AccountId);
        var snapshot = Snapshot() with { Revision = 4 };
        var commands = new RecordingCommandBus(PartyOperationResult.Success(
            new PartyNotificationPlan(
                [],
                snapshot,
                SettingsChange: PartySettingsChangeKind.Updated)));

        await new SetPartyInfoHandler(
                sessions,
                gate,
                commands,
                new PartyNotificationDelivery(published),
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
        // update is deliberately promoted to the three full-state phases. Every phase re-enters
        // the account gate and therefore proves transport starts only after the handler releases it.
        Assert.Equal(3, published.Count);
    }

    private static PartyEndpoint Endpoint() => new(
        5,
        new(172, 17, 208, 1),
        new(172, 17, 208, 1),
        30976,
        1472);

    private static PartySnapshot Snapshot() => new(
        0x1234,
        7,
        3,
        Inviter.WireUserId,
        new PartySettings(
            [0, 0, 4, 0, 0, 0, 0, 5, 0, (byte)PartyCategory.Normal, 0xFF, 0xFF]),
        PartyTitle.Empty,
        [
            new PartyMemberSnapshot(Inviter, 0, Endpoint(), 73),
            new PartyMemberSnapshot(Invitee, 1, null, 45),
        ]);

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

    private sealed class NoopNotificationBus : INotificationBus
    {
        public ValueTask<NotificationDelivery> PublishAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
            where TNotification : class, INotification<TNotification> =>
            ValueTask.FromResult(new NotificationDelivery([]));
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

    private sealed class RecordingNotificationBus : INotificationBus
    {
        public List<Publication> Publications { get; } = [];

        public ValueTask<NotificationDelivery> PublishAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
            where TNotification : class, INotification<TNotification>
        {
            Publications.Add(new Publication(audience, notification));
            return ValueTask.FromResult(new NotificationDelivery([]));
        }
    }

    private sealed class GateReentryNotificationBus(
        SelectedSessionTransitionGate gate,
        AccountId accountId) : INotificationBus
    {
        public int Count { get; private set; }

        public async ValueTask<NotificationDelivery> PublishAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
            where TNotification : class, INotification<TNotification>
        {
            await using var lease = await gate.AcquireAsync(
                accountId, UserAccessMode.Exclusive, cancellationToken);
            Count++;
            return new NotificationDelivery([]);
        }
    }

    private sealed class SessionDirectory(params SelectedSessionIdentity[] initial)
        : ISelectedSessionDomainService
    {
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

    private sealed class RecordingParties : IPartyDomainService
    {
        public List<string> Writes { get; } = [];

        public PartyOperationResult CreateOrUpdateSettingsExact(
            SelectedSessionIdentity session,
            PartySettings settings,
            PartyTitle title)
        {
            Writes.Add(
                $"settings:{session.WireUserId}:{(byte)settings.Category}:{Convert.ToHexString(title.ToBytes())}");
            return PartyOperationResult.Success();
        }

        public PartyOperationResult UpdateEndpoint(
            SelectedSessionIdentity session,
            PartyEndpoint endpoint)
        {
            Writes.Add($"endpoint:{session.WireUserId}");
            return PartyOperationResult.Success();
        }

        public PartyOperationResult RequestInvite(
            SelectedSessionIdentity inviter,
            ushort targetWireUserId,
            PartyInviteDisplayState display)
        {
            Writes.Add($"invite:{inviter.WireUserId}->{targetWireUserId}:{display.ReviveCoinCount}");
            return PartyOperationResult.Success();
        }

        public PartyOperationResult AcceptInvite(
            SelectedSessionIdentity invitee,
            ushort inviterWireUserId)
        {
            Writes.Add($"accept:{invitee.WireUserId}<-{inviterWireUserId}");
            return PartyOperationResult.Success();
        }

        public PartyOperationResult LeavePartyExact(SelectedSessionIdentity session)
        {
            Writes.Add($"leave:{session.WireUserId}");
            return PartyOperationResult.Success();
        }

        public PartyOperationResult CleanupSessionExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException();

        public PartySnapshot? FindParty(SelectedSessionIdentity session)
        {
            Writes.Add($"query:{session.WireUserId}");
            return Snapshot();
        }

        public PartyEndpoint? FindEndpoint(SelectedSessionIdentity session) => null;
    }

    private sealed class FixedPartyMemberStatus : IPartyMemberStatusDomainService
    {
        public ValueTask<PartyInviteDisplayState> InvitationOfAsync(
            SelectedSessionIdentity session,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PartyInviteDisplayState(64, 188, 0, 0));

        public byte CurrentHpPercentOf(SelectedSessionIdentity session) => 73;
    }
}
