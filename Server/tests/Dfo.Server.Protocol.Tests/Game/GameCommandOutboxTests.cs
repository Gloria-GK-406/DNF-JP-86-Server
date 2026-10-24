using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;

namespace Dfo.Server.Protocol.Tests.Game;

public sealed class GameCommandOutboxTests
{
    [Fact]
    public async Task Explicit_registration_preserves_each_messages_category_and_the_exact_target_order()
    {
        var recorder = new Registration();
        var outbox = new GameCommandOutbox(recorder);
        var cmd = new Cmd(); var noti = new Noti();
        await outbox.RegisterCmdAsync(new("direct"), cmd, default);
        await outbox.RegisterNotiAsync(new("direct"), noti, default);
        Assert.Equal(new IServerNetworkMessage[] { cmd, noti }, recorder.Messages);
        await outbox.RegisterExplicitAsync(new("direct"), new([cmd, noti], [new(new("peer"), [noti, cmd])]), default);
        Assert.Equal(new IServerNetworkMessage[] { cmd, noti }, recorder.Explicit!.RequiredDirectMessages);
        var target = Assert.Single(recorder.Explicit.BestEffortTargets);
        Assert.Equal(new NetworkConnectionId("peer"), target.ConnectionId);
        Assert.Equal(new IServerNetworkMessage[] { noti, cmd }, target.OrderedMessages);
        var attempt = new Token();
        await outbox.RegisterExplicitAsync(new("direct"), new([cmd, noti], []), attempt, default);
        Assert.Same(attempt, recorder.Attempt);
        Assert.Equal(new IServerNetworkMessage[] { cmd, noti }, recorder.Explicit!.RequiredDirectMessages);
    }

    [Fact]
    public async Task Prepared_registration_preserves_the_exact_physical_connection()
    {
        var recorder = new Registration(); var outbox = new GameCommandOutbox(recorder);
        var connection = new NetworkConnectionContext(new("physical"), NetworkEndpointKind.Game, 10011);
        var cmd = new Cmd(); var noti = new Noti();
        await outbox.RegisterBatchAsync(connection, [cmd, noti], default);
        Assert.Same(connection, recorder.Connection);
        Assert.Equal(new IServerNetworkMessage[] { cmd, noti }, recorder.Messages);
        recorder.Messages.Clear();
        var attempt = new Token();
        await outbox.RegisterBatchAsync(connection, [cmd, noti], attempt, default);
        Assert.Same(connection, recorder.Connection);
        Assert.Same(attempt, recorder.Attempt);
        Assert.Equal(new IServerNetworkMessage[] { cmd, noti }, recorder.Messages);
    }

    private sealed record Cmd : IGameCmdMessage { public ServerMessageType MessageType => ServerMessageType.GameLogin; }
    private sealed record Noti : IGameNotiMessage { public ServerMessageType MessageType => ServerMessageType.GameUserState; }
    private sealed class Token : ICommandRegistrationAttempt;
    private sealed class Registration : ICommandMessageOutbox
    {
        public List<IServerNetworkMessage> Messages { get; } = [];
        public ICommandRegistrationAttempt? Attempt { get; private set; }
        public NetworkConnectionContext? Connection { get; private set; }
        public ExplicitMessageDelivery? Explicit { get; private set; }
        public ValueTask RegisterAsync(NetworkConnectionId connection, IServerNetworkMessage message, CancellationToken ct)
        { Messages.Add(message); return ValueTask.CompletedTask; }
        public ValueTask RegisterBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages, CancellationToken ct)
        { Connection = connection; Messages.AddRange(messages); return ValueTask.CompletedTask; }
        public ValueTask RegisterExplicitAsync(NetworkConnectionId connection, ExplicitMessageDelivery delivery, CancellationToken ct)
        { Explicit = delivery; return ValueTask.CompletedTask; }
        public ValueTask RegisterBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages, ICommandRegistrationAttempt attempt, CancellationToken ct)
        { Attempt = attempt; return RegisterBatchAsync(connection, messages, ct); }
        public ValueTask RegisterExplicitAsync(NetworkConnectionId connection, ExplicitMessageDelivery delivery, ICommandRegistrationAttempt attempt, CancellationToken ct)
        { Attempt = attempt; return RegisterExplicitAsync(connection, delivery, ct); }

    }
}
