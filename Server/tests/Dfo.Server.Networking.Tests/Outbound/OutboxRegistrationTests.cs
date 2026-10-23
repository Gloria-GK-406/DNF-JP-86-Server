using System.Collections.Concurrent;
using System.Diagnostics;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Networking.Tests.Outbound;

/// <summary>
/// The registration adapters (spec §5 table): what each sender and closer call registers in the job's
/// outbox, what it drops, and that off the executor every one of them is the program error RQ-061 makes
/// it, with its evidence.
/// </summary>
public sealed class OutboxRegistrationTests
{
    [Fact]
    public async Task A_send_by_id_registers_one_required_batch_for_the_connection_registered_under_it()
    {
        var connections = new FixedConnections();
        var a = connections.Add("a");
        await using var host = new OutboxRegistrationHost(connections, new TextEncoder());

        await host.RunAsync(() => host.Sender.SendAsync(a.ConnectionId, new Text("one"), CancellationToken.None).AsTask());

        var entry = Assert.Single(host.Entries);
        Assert.Same(a, entry.Connection);
        Assert.Equal(OutboundDelivery.Required, entry.Delivery);
        Assert.Equal("one", System.Text.Encoding.UTF8.GetString(entry.Frames.Span));
    }

    [Fact]
    public async Task A_prepared_batch_is_registered_for_exactly_the_context_given_even_one_no_longer_registered()
    {
        var connections = new FixedConnections();
        var captured = connections.Add("a");
        connections.Remove(captured.ConnectionId);
        var replacement = connections.Add("a");
        await using var host = new OutboxRegistrationHost(connections, new TextEncoder());

        await host.RunAsync(() => host.Sender.SendBatchAsync(captured, [new Text("one"), new Text("two")], CancellationToken.None).AsTask());

        var entry = Assert.Single(host.Entries);
        Assert.Same(captured, entry.Connection);
        Assert.NotSame(replacement, entry.Connection);
        Assert.Equal(2, entry.Messages.Count);
    }

    [Fact]
    public async Task An_exact_close_of_a_replaced_context_is_not_registered_and_one_of_the_current_context_is()
    {
        var connections = new FixedConnections();
        var old = connections.Add("a");
        connections.Remove(old.ConnectionId);
        var current = connections.Add("a");
        await using var host = new OutboxRegistrationHost(connections, new TextEncoder());

        await host.RunAsync(async () =>
        {
            await host.Closer.RequestCloseExactAsync(old, CancellationToken.None);
            await host.Closer.RequestCloseExactAsync(current, CancellationToken.None);
        });

        var close = Assert.Single(host.Entries);
        Assert.True(close.IsClose);
        Assert.Same(current, close.Connection);
    }

    [Fact]
    public async Task A_close_by_id_closes_the_connection_registered_under_it()
    {
        var connections = new FixedConnections();
        var a = connections.Add("a");
        await using var host = new OutboxRegistrationHost(connections, new TextEncoder());

        await host.RunAsync(() => host.Closer.RequestCloseAsync(a.ConnectionId, CancellationToken.None).AsTask());

        var close = Assert.Single(host.Entries);
        Assert.True(close.IsClose);
        Assert.Same(a, close.Connection);
    }

    /// <summary>F-83: the connection went away before the CMD answered it. Nothing is registered and the CMD goes on.</summary>
    [Fact]
    public async Task A_send_or_close_for_a_connection_no_longer_registered_is_dropped_and_the_job_succeeds()
    {
        var connections = new FixedConnections();
        var live = connections.Add("live");
        var gone = new NetworkConnectionId("gone");
        await using var host = new OutboxRegistrationHost(connections, new TextEncoder());

        await host.RunAsync(async () =>
        {
            await host.Sender.SendAsync(gone, new Text("lost"), CancellationToken.None);
            await host.Sender.SendExplicitAsync(gone, new ExplicitMessageDelivery([new Text("lost")], []), CancellationToken.None);
            await host.Closer.RequestCloseAsync(gone, CancellationToken.None);
            await host.Sender.SendAsync(live.ConnectionId, new Text("kept"), CancellationToken.None);
        });

        var entry = Assert.Single(host.Entries);
        Assert.Same(live, entry.Connection);
    }

    /// <summary>All of a batch or none of it, at the call that registers it (TASK-23 ruling 1).</summary>
    [Fact]
    public async Task A_batch_whose_last_message_cannot_be_encoded_throws_at_the_call_and_registers_none_of_it()
    {
        var connections = new FixedConnections();
        var a = connections.Add("a");
        await using var host = new OutboxRegistrationHost(connections, new TextEncoder());

        await host.RunAsync(async () =>
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => host.Sender.SendBatchAsync(a,
                [new Text("one"), new Text("unencodable")], CancellationToken.None).AsTask());
            await Assert.ThrowsAsync<InvalidDataException>(() => host.Sender.SendExplicitAsync(a.ConnectionId,
                new ExplicitMessageDelivery([new Text("one"), new Text("unencodable")], []), CancellationToken.None).AsTask());
        });

        Assert.Empty(host.Entries);
    }

    /// <summary>RQ-061, the outbox half, and its counterexample (RQ-072): off the executor nothing is registered.</summary>
    [Fact]
    public async Task A_send_off_the_executor_is_a_program_error_reported_as_evidence()
    {
        var connections = new FixedConnections();
        var a = connections.Add("a");
        await using var host = new OutboxRegistrationHost(connections, new TextEncoder());
        var seen = Violations.Attach();

        var error = await Assert.ThrowsAsync<GameplayOffExecutorException>(
            () => host.Sender.SendAsync(a.ConnectionId, new Text("one"), CancellationToken.None).AsTask());

        Assert.Contains("GameplayOutbox.Register", error.Message, StringComparison.Ordinal);
        var violation = Assert.Single(seen);
        Assert.Equal(GameplayWriteAssertion.OffExecutorKind, violation.Kind);
        Assert.Empty(host.Entries);
    }

    /// <summary>The closer's counterexample (RQ-072).</summary>
    [Fact]
    public async Task A_close_off_the_executor_is_a_program_error_reported_as_evidence()
    {
        var connections = new FixedConnections();
        var a = connections.Add("a");
        await using var host = new OutboxRegistrationHost(connections, new TextEncoder());
        var seen = Violations.Attach();

        await Assert.ThrowsAsync<GameplayOffExecutorException>(
            () => host.Closer.RequestCloseAsync(a.ConnectionId, CancellationToken.None).AsTask());

        var violation = Assert.Single(seen);
        Assert.Equal(GameplayWriteAssertion.OffExecutorKind, violation.Kind);
        Assert.Empty(host.Entries);
    }

    /// <summary>An ordered scope off the executor groups nothing and is not itself an error.</summary>
    [Fact]
    public async Task An_ordered_scope_off_the_executor_does_nothing()
    {
        await using var host = new OutboxRegistrationHost(new FixedConnections(), new TextEncoder());
        var seen = Violations.Attach();

        using (var scope = host.Ordering.Begin("off the executor", OutboundDelivery.Required))
            scope.Delivery = OutboundDelivery.BestEffort;

        Assert.Empty(seen);
    }

    private sealed record Text(string Value) : IServerNetworkMessage
    {
        public ServerMessageType MessageType => default;
    }

    private sealed class TextEncoder : IOutboundMessageEncoder
    {
        public byte[] EncodeMessage(NetworkConnectionContext connection, IServerNetworkMessage message) =>
            ((Text)message).Value == "unencodable"
                ? throw new InvalidDataException("cannot encode")
                : System.Text.Encoding.UTF8.GetBytes(((Text)message).Value);

        public byte[] EncodeBatch(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages) =>
            [.. messages.SelectMany(message => EncodeMessage(connection, message))];
    }
}

/// <summary>Collects the gameplay assertion violations reported on the current flow.</summary>
internal static class Violations
{
    private static readonly AsyncLocal<ConcurrentQueue<GameplayAssertionViolation>?> Current = new();
    private static readonly IDisposable Subscription = DiagnosticListener.AllListeners.Subscribe(new Sources());

    /// <summary>A job does not inherit the test's flow, so a job body attaches the same queue again.</summary>
    public static ConcurrentQueue<GameplayAssertionViolation> Attach(ConcurrentQueue<GameplayAssertionViolation>? into = null)
    {
        _ = Subscription;
        into ??= new ConcurrentQueue<GameplayAssertionViolation>();
        Current.Value = into;
        return into;
    }

    private sealed class Sources : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == GameplayGateAssertion.DiagnosticSourceName) value.Subscribe(new Events());
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    private sealed class Events : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is GameplayAssertionViolation violation) Current.Value?.Enqueue(violation);
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}
