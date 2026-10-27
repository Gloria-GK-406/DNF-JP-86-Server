using System.Collections.Concurrent;
using System.Text;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Outbound;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Networking.Tests.Outbound;

internal sealed record TextMessage(string Text) : IServerNetworkMessage
{
    public ServerMessageType MessageType => default;
}

internal sealed class FakeTransport : IOutboundTransport
{
    private readonly ConcurrentDictionary<NetworkConnectionContext, FakeConnection> live =
        new(ReferenceEqualityComparer.Instance);

    public ConcurrentQueue<string> Events { get; } = new();

    public FakeConnection Add(NetworkConnectionContext context)
    {
        var connection = new FakeConnection(context, Events);
        live[context] = connection;
        return connection;
    }

    public void Remove(NetworkConnectionContext context) => live.TryRemove(context, out _);

    public IOutboundConnection? Resolve(NetworkConnectionContext expected) =>
        live.TryGetValue(expected, out var connection) && !connection.IsClosed ? connection : null;
}

internal sealed class FakeConnection(NetworkConnectionContext context, ConcurrentQueue<string> events) : IOutboundConnection
{
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Name => context.ConnectionId.Value;

    /// <summary>Awaited by every send before it completes; the token is the send's own.</summary>
    public Func<CancellationToken, Task>? Gate { get; set; }

    public Exception? FailWith { get; set; }

    public bool FailBeforeStarting { get; set; }

    public bool ClosedSendSide { get; set; }

    public bool HasClosedSendSide(Exception error) => ClosedSendSide;

    public ConcurrentQueue<string> Sent { get; } = new();

    public ConcurrentQueue<bool> SentOnExecutor { get; } = new();

    public bool IsClosed => closed.Task.IsCompleted;

    public Task Closed => closed.Task;

    public ValueTask SendAsync(ReadOnlyMemory<byte> frames, CancellationToken cancellationToken)
    {
        if (FailBeforeStarting && FailWith is { } failure)
            throw failure;
        return SendCoreAsync(frames, cancellationToken);
    }

    private async ValueTask SendCoreAsync(ReadOnlyMemory<byte> frames, CancellationToken cancellationToken)
    {
        SentOnExecutor.Enqueue(GameplayJob.IsOnExecutor);
        var text = Encoding.UTF8.GetString(frames.Span);
        events.Enqueue($"{Name}:start {text}");
        if (Gate is { } gate)
            await gate(cancellationToken);
        if (FailWith is { } failure)
            throw failure;
        Sent.Enqueue(text);
        events.Enqueue($"{Name}:sent {text}");
    }

    /// <summary>Thrown by <see cref="CloseAsync"/> after the connection is marked closed.</summary>
    public Exception? CloseFailWith { get; set; }

    public ValueTask CloseAsync()
    {
        events.Enqueue($"{Name}:close");
        closed.TrySetResult();
        return CloseFailWith is { } failure ? ValueTask.FromException(failure) : ValueTask.CompletedTask;
    }
}

internal sealed class RecordingObserver : IOutboundDeliveryObserver
{
    private readonly object sync = new();
    private readonly List<OutboundDeliveryReport> reports = [];
    private readonly List<(NetworkConnectionContext Connection, OutboundOutcome Reason)> failures = [];

    public IReadOnlyList<OutboundDeliveryReport> Reports
    {
        get { lock (sync) return [.. reports]; }
    }

    public IReadOnlyList<(NetworkConnectionContext Connection, OutboundOutcome Reason)> Failures
    {
        get { lock (sync) return [.. failures]; }
    }

    public void OnEntryCompleted(OutboundDeliveryReport report)
    {
        lock (sync) reports.Add(report);
    }

    public void OnConnectionFailed(NetworkConnectionContext connection, OutboundOutcome reason)
    {
        lock (sync) failures.Add((connection, reason));
    }

    public OutboundOutcome OutcomeOf(OutboundEntry entry) =>
        Reports.Single(report => ReferenceEquals(report.Entry, entry)).Outcome;

    public async Task WaitForAsync(int count, TimeSpan? patience = null)
    {
        var deadline = DateTime.UtcNow + (patience ?? TimeSpan.FromSeconds(10));
        while (Reports.Count < count)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Expected {count} reports, saw {Reports.Count}.");
            await Task.Delay(5);
        }
    }
}

internal static class Outbound
{
    public static NetworkConnectionContext Context(string name) =>
        new(new NetworkConnectionId(name), NetworkEndpointKind.Game, 10011);

    public static OutboundEntry Batch(
        NetworkConnectionContext connection,
        string text,
        OutboundDelivery delivery = OutboundDelivery.Required,
        OutboundOrder? order = null) =>
        OutboundEntry.Batch(connection, [new TextMessage(text)], Encoding.UTF8.GetBytes(text), delivery, order);

    public static OutboundSender Sender(
        FakeTransport transport,
        RecordingObserver observer,
        OutboundSendOptions? options = null,
        ILogger<OutboundSender>? logger = null) =>
        new(transport, [observer], logger ?? NullLogger<OutboundSender>.Instance,
            options ?? OutboundSendOptions.Default);

    public static async Task<bool> NeverAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return true;
    }
}
