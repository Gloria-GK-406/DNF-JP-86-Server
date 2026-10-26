using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Joust;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.Joust;

public sealed class JoustQueryHandlerTests
{
    [Theory]
    [InlineData(JoustPhase.Betting)]
    [InlineData(JoustPhase.Final)]
    [InlineData(JoustPhase.Result)]
    public async Task Bet_updates_always_publish_silent_data(JoustPhase phase)
    {
        var state = new JoustState(true, DateTimeOffset.UnixEpoch, phase, "round",
            null, null, null, [], [1,2,3,4,1,3,1], 0, 1000, 17);
        var messages = await new JoustDetailsChangedHandler(new Projection(state)).ComposeAsync(new(new(1), new("joust")),
            new(state.ServerTime), default);
        Assert.DoesNotContain(messages, m => m is JoustStateMessage or JoustResultsMessage);
        Assert.Single(messages.OfType<JoustBetsMessage>());
        Assert.Single(messages.OfType<JoustInfoMessage>());
    }

    [Theory]
    [InlineData(JoustPhase.Betting)]
    [InlineData(JoustPhase.Result)]
    public async Task Repeated_info_queries_return_details_without_reannouncing_the_phase(JoustPhase phase)
    {
        var state = new JoustState(true, DateTimeOffset.UnixEpoch, phase, "round", null, null, null,
            [], phase == JoustPhase.Result ? [1, 2, 3, 4, 1, 3, 1] : [], 0, 1000, 17);
        var replies = await QueryTwice(state);
        foreach (var messages in replies)
        {
            Assert.DoesNotContain(messages, m => m is JoustStateMessage or JoustResultsMessage);
            var ack=Assert.IsType<JoustResponse>(messages[0]);
            Assert.Equal((ushort)0x04E6,ack.Type);
            Assert.Equal(0u,ack.Result);
            Assert.Null(ack.History);
            Assert.IsType<JoustBetsMessage>(messages[1]);
            Assert.IsType<JoustInfoMessage>(messages[2]);
            Assert.Equal(3, messages.Count);
        }
    }

    [Fact]
    public async Task Closed_info_query_returns_only_the_refusal_without_empty_phase_notifications()
    {
        var state = new JoustState(false, DateTimeOffset.UnixEpoch, JoustPhase.Inactive, null,
            null, null, null, [], [], 0, 0, 0);
        foreach (var messages in await QueryTwice(state))
            Assert.Equal(6u, Assert.IsType<JoustResponse>(Assert.Single(messages)).Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_history_queries_return_only_history_without_mail_or_phase_notifications(bool hasHistory)
    {
        var state = new JoustState(true, DateTimeOffset.UnixEpoch, JoustPhase.Result, "round",
            null, null, null, [], [], 0, 1000, 17);
        JoustHistoryEntry[] history = hasHistory
            ? [new("saved", DateTimeOffset.UnixEpoch, 3, 2.5m) { RoundNo = 7 }]
            : [];
        foreach (var messages in await QueryTwice(state, 0x04E8, history))
        {
            var response = Assert.IsType<JoustResponse>(Assert.Single(messages));
            Assert.Equal((ushort)0x04E8, response.Type);
            Assert.Equal(0u, response.Result);
            Assert.Equal(history, response.History);
        }
    }

    [Theory]
    [InlineData(JoustPhase.Betting)]
    [InlineData(JoustPhase.Result)]
    public async Task General_event_refresh_does_not_reinitialize_joust_or_reannounce_results(JoustPhase phase)
    {
        var state=new JoustState(true,DateTimeOffset.UnixEpoch,phase,"round",null,null,null,
            [],[1,2,3,4,1,3,1],0,1000,17);
        var session=SelectedSessionIdentity.Create(new(1),new(1),new("joust-test"),1,10010);
        var handler=new Dfo.Server.Protocol.Game.WeeklyAttendance.EventsChangedHandler(new Sessions(session),new EventProjection(state));
        for(var i=0;i<2;i++)
        {
            var messages=await handler.ComposeAsync(new(new(1),session.ConnectionId),new EventsChanged(),default);
            Assert.DoesNotContain(messages,m=>m is JoustStateMessage or JoustResultsMessage);
            Assert.Single(messages.OfType<JoustBetsMessage>());
            Assert.Single(messages.OfType<JoustInfoMessage>());
            Assert.Equal(4,messages.Count);
        }
    }
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task General_event_refresh_sends_scheduled_dungeon_open_and_close(bool open, byte value)
    {
        var state = new JoustState(false,DateTimeOffset.UnixEpoch,JoustPhase.Betting,"round",null,null,null,[],[],0,0,0);
        var session = SelectedSessionIdentity.Create(new(1),new(1),new("schedule-test"),1,10010);
        var handler = new Dfo.Server.Protocol.Game.WeeklyAttendance.EventsChangedHandler(new Sessions(session),
            new EventProjection(state,[new(3700,open)]));
        var messages = await handler.ComposeAsync(new(new(1),session.ConnectionId),new EventsChanged(),default);
        var schedule = Assert.Single(messages.OfType<Dfo.Server.Protocol.Game.WeeklyAttendance.ScheduledDungeonStateMessage>());
        Assert.Equal((ushort)0x033F,schedule.Type);
        Assert.Equal(new byte[] { 0,0,0,0,value,0,0,0,0x74,0x0E,0,0,0,0,0,0 },schedule.EncodeBody());
    }
    private sealed class EventProjection(JoustState state,Dfo.Server.Data.Events.ScheduledDungeonState[]? scheduled=null):IEventPlayerProjectionDomainService
    {
        public ValueTask<Dfo.Server.Data.Events.EventPlayerSnapshot> ReadNowAsync(SelectedSessionIdentity session,CancellationToken ct)
            =>ValueTask.FromResult(new Dfo.Server.Data.Events.EventPlayerSnapshot([],Dfo.Server.Data.Events.WeeklyAttendanceState.Empty,state)
                { ScheduledDungeons = scheduled ?? [] });
    }

    private static async Task<IReadOnlyList<IGameServerMessage>[]> QueryTwice(JoustState state,
        ushort type = 0x04E6, JoustHistoryEntry[]? history = null)
    {
        var connection = new NetworkConnectionContext(new("joust-test"), NetworkEndpointKind.Game, 10010);
        var session = SelectedSessionIdentity.Create(new(1), new(1), connection.ConnectionId, 1, 10010);
        var sessions = new Sessions(session);
        var sender = new Sender();
        using var services = new ServiceCollection()
            .AddSingleton<ISelectedSessionDomainService>(sessions)
            .AddSingleton<ISelectedSessionSnapshotQuery>(sessions)
            .AddSingleton<IOnlineUserQueryDomainService>(new Users(new(OnlineUserId.New(), connection,
                session.AccountId, session.CharacterId, OnlineUserState.CharacterActive, 1)))
            .AddSingleton<ICommandBus>(sp => new Commands(sp, state, history ?? []))
            .AddSingleton<IGameMessageSender>(sender)
            .AddSingleton<IEventService>(new Notices())
            .AddSingleton(sp => new SelectedSessionSafety(sp, NullLogger<SelectedSessionSafety>.Instance))
            .BuildServiceProvider();
        var replies = await TestGameplay.RunAsync(services, async () =>
        {
            var handler = ActivatorUtilities.CreateInstance<JoustQueryHandler>(services);
            var replies = new List<IReadOnlyList<IGameServerMessage>>();
            for (var i = 0; i < 2; i++)
            {
                sender.Sent.Clear();
                await handler.HandleAsync(new(connection), new JoustQueryRequest(new(1, type, 0, 1)), default);
                replies.Add(sender.Sent.ToArray());
            }
            return replies;
        });
        return [.. replies];
    }

    private sealed class Commands(IServiceProvider services, JoustState state, JoustHistoryEntry[] history) : ICommandBus
    {
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

        public async ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            var query = Assert.IsType<GameJoustQueryCommand>(command);
            var handler = new GameJoustQueryCommandHandler(services.GetRequiredService<ISelectedSessionDomainService>(),
                services.GetRequiredService<IOnlineUserQueryDomainService>(), null!, TimeProvider.System,
                new Units(state, history), (IGameCommandOutbox)services.GetRequiredService<IGameMessageSender>());
            var execution = DomainCommandFixture.Context(Guid.NewGuid(), query.GetType(), handler.GetType());
            return (TResult)(object)await handler.HandleAsync(execution, query, cancellationToken);
        }
    }
    private sealed class Units(JoustState state, JoustHistoryEntry[] history) : ICommandWorkUnits
    {
        public ValueTask<TResult> ReadOnlyAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
            Func<CancellationToken, ValueTask<TResult>> action, CancellationToken ct = default) where TResult : notnull =>
            ValueTask.FromResult((TResult)(object)(typeof(TResult) == typeof(JoustState) ? (object)state : history));
        public ValueTask<TResult> WriteAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
            Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action, CancellationToken ct = default) where TResult : notnull => throw new InvalidOperationException("A query must be read-only.");
        public ValueTask<CommandWriteResult<TResult>> WriteResultAsync<TResult>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
            Func<CommandWorkUnit, CancellationToken, ValueTask<TResult>> action, CancellationToken ct = default) where TResult : notnull => throw new InvalidOperationException("A query must be read-only.");
    }
    private sealed class Sessions(SelectedSessionIdentity session) : ISelectedSessionDomainService, ISelectedSessionSnapshotQuery
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

        public SelectedSessionIdentity? Select(SelectedSessionIdentity value) => throw new NotSupportedException();
        public bool ReleaseExact(SelectedSessionIdentity value) => throw new NotSupportedException();
        public SelectedSessionIdentity? Find(AccountId id) => id == session.AccountId ? session : null;
        public SelectedSessionIdentity? FindByCharacter(CharacterId id) => id == session.CharacterId ? session : null;
        public SelectedSessionIdentity? FindByWireUserId(ushort id) => id == session.WireUserId ? session : null;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId id) => id == session.ConnectionId ? session : null;
        public IReadOnlyList<SelectedSessionIdentity> Snapshot() => [session];
    }
    private sealed class Projection(JoustState state) : IJoustProjectionDomainService
    {
        public ValueTask<JoustState> ReadAsync(CharacterId character, DateTimeOffset now, CancellationToken ct)
            => ValueTask.FromResult(state);
    }
    private sealed class Users(OnlineUserSnapshot user) : IOnlineUserQueryDomainService
    {
        public OnlineUserSnapshot GetRequired(OnlineUserId id) => user;
        public bool TryGetByConnection(NetworkConnectionId id, out OnlineUserSnapshot? found)
        { found = id == user.Connection.ConnectionId ? user : null; return found is not null; }
    }
    private sealed class Notices : Dfo.Server.Testing.ProjectionEventService
    {
        public override ValueTask<NotificationDelivery> ProjectAsync<T>(NotificationAudience audience, T notice, CancellationToken ct) => throw new InvalidOperationException("A query must not publish notifications.");
    }
    /// <summary>
    /// What the handler registered, read from the outbox of a real registration adapter (inside the job, what is still
    /// in it); every call goes to the real protocol sender over it (TASK-25). An explicit delivery fails where it is
    /// registered, as the old double refused one.
    /// </summary>
    private sealed class Sender : IGameMessageSender, IGameCommandOutbox
    {
        public ValueTask RegisterCmdAsync(NetworkConnectionId id, IGameCmdMessage message, CancellationToken ct) => SendCmdAsync(id, message, ct);
        public ValueTask RegisterNotiAsync(NetworkConnectionId id, IGameNotiMessage message, CancellationToken ct) => SendNotiAsync(id, message, ct);
        public ValueTask RegisterBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IGameServerMessage> messages, CancellationToken ct) => SendBatchAsync(connection, messages, ct);
        public ValueTask RegisterExplicitAsync(NetworkConnectionId id, GameExplicitMessageDelivery delivery, CancellationToken ct) => SendExplicitAsync(id, delivery, ct);
        private readonly RecordedOutbound outbound = new();
        public Sender() => outbound.RefuseExplicit();
        public RecordedMessages<IGameServerMessage> Sent => outbound.MessagesAs<IGameServerMessage>();
        public ValueTask SendBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IGameServerMessage> messages, CancellationToken ct) =>
            outbound.Game.SendBatchAsync(connection, messages, ct);
        public ValueTask SendCmdAsync(NetworkConnectionId id, IGameCmdMessage message, CancellationToken ct) =>
            outbound.Game.SendCmdAsync(id, message, ct);
        public ValueTask SendNotiAsync(NetworkConnectionId id, IGameNotiMessage message, CancellationToken ct) =>
            outbound.Game.SendNotiAsync(id, message, ct);
        public ValueTask SendExplicitAsync(NetworkConnectionId id, GameExplicitMessageDelivery delivery, CancellationToken ct) =>
            outbound.Game.SendExplicitAsync(id, delivery, ct);
    }
}
