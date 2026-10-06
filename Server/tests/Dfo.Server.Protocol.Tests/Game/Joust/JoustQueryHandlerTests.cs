using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
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

namespace Dfo.Server.Protocol.Tests.Game.Joust;

public sealed class JoustQueryHandlerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Only_a_state_change_announces_the_phase_while_bet_changes_refresh_details(bool stateChanged)
    {
        var state = new JoustState(true, DateTimeOffset.UnixEpoch, JoustPhase.Betting, "round",
            null, null, null, [], [], 0, 1000, 17);
        var messages = await new JoustChangedHandler(new Projection(state)).ComposeAsync(new(new(1), new("joust")),
            new(state.ServerTime, stateChanged), default);
        Assert.Equal(stateChanged ? 1 : 0, messages.OfType<JoustStateMessage>().Count());
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
            Assert.DoesNotContain(messages, m => m is JoustStateMessage or JoustResponse);
            Assert.IsType<JoustBetsMessage>(messages[0]);
            Assert.IsType<JoustInfoMessage>(messages[1]);
            Assert.Equal(phase == JoustPhase.Result ? 3 : 2, messages.Count);
            if (phase == JoustPhase.Result) Assert.IsType<JoustResultsMessage>(messages[2]);
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

    private static async Task<IReadOnlyList<IGameServerMessage>[]> QueryTwice(JoustState state)
    {
        var connection = new NetworkConnectionContext(new("joust-test"), NetworkEndpointKind.Game, 10010);
        var session = SelectedSessionIdentity.Create(new(1), new(1), connection.ConnectionId, 1, 10010);
        var sessions = new Sessions(session);
        using var services = new ServiceCollection()
            .AddSingleton<ISelectedSessionDomainService>(sessions)
            .AddSingleton<IOnlineUserQueryDomainService>(new Users(new(OnlineUserId.New(), connection,
                session.AccountId, session.CharacterId, OnlineUserState.CharacterActive, 1)))
            .BuildServiceProvider();
        var transitions = new SelectedSessionTransitionGate();
        var sender = new Sender();
        var handler = new JoustQueryHandler(sessions, new Commands(state), sender, new Notices(), sessions,
            transitions, new SelectedSessionSafety(services, transitions, NullLogger<SelectedSessionSafety>.Instance));
        var replies = new List<IReadOnlyList<IGameServerMessage>>();
        for (var i = 0; i < 2; i++)
        {
            sender.Sent.Clear();
            await handler.HandleAsync(new(connection), new JoustQueryRequest(new(1, 0x04E6, 0, 1)), default);
            replies.Add(sender.Sent.ToArray());
        }
        return [.. replies];
    }

    private sealed class Commands(JoustState state) : ICommandBus
    {
        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            Assert.IsType<JoustStateCommand>(command);
            return ValueTask.FromResult((TResult)(object)state);
        }
    }
    private sealed class Sessions(SelectedSessionIdentity session) : ISelectedSessionDomainService, ISelectedSessionSnapshotQuery
    {
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
    private sealed class Notices : INotificationBus
    {
        public ValueTask<NotificationDelivery> PublishAsync<T>(NotificationAudience audience, T notice, CancellationToken ct)
            where T : class, INotification<T> => throw new InvalidOperationException("These queries do not settle rewards.");
    }
    private sealed class Sender : IGameMessageSender
    {
        public List<IGameServerMessage> Sent { get; } = [];
        public ValueTask SendBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IGameServerMessage> messages, CancellationToken ct)
        { Sent.AddRange(messages); return ValueTask.CompletedTask; }
        public ValueTask SendCmdAsync(NetworkConnectionId id, IGameCmdMessage message, CancellationToken ct)
        { Sent.Add(message); return ValueTask.CompletedTask; }
        public ValueTask SendNotiAsync(NetworkConnectionId id, IGameNotiMessage message, CancellationToken ct)
        { Sent.Add(message); return ValueTask.CompletedTask; }
        public ValueTask SendExplicitAsync(NetworkConnectionId id, GameExplicitMessageDelivery delivery, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
