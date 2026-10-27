using System.Collections.Concurrent;
using System.Net;
using System.IO.Pipelines;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Outbound;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TouchSocket.Core;
using TouchSocket.Sockets;
using Xunit.Abstractions;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using TcpClient = System.Net.Sockets.TcpClient;

namespace Dfo.Server.Networking.Tests.Outbound;

/// <summary>
/// The sender over a real, minimal TouchSocket service. The first case is the direct regression of
/// TASK-01's root cause: a close must not overtake frames that were already handed to the transport.
/// </summary>
public sealed class OutboundRealSocketTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    /// <summary>
    /// RQ-003, the network half, and its counterexample: a job that writes to a resolved connection waits
    /// on the network. That is a program error that fails the job, is logged with its call chain, and is
    /// reported as evidence; nothing reaches the client.
    /// </summary>
    [Fact]
    public async Task A_job_that_writes_to_a_connection_directly_fails_as_a_program_error()
    {
        await using var server = await RealOutboundServer.StartAsync(confirmFlush: true);
        var (client, context) = await server.ConnectAsync();
        using (client)
        {
            var log = new Dfo.Server.Networking.Tests.Integration.LifecycleLog();
            using var factory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddProvider(log));
            await using var executor = new GameplayExecutor(factory.CreateLogger<GameplayExecutor>());
            var connection = server.Transport().Resolve(context)!;
            var seen = Violations.Attach();

            var error = await Assert.ThrowsAsync<GameplaySelfWaitException>(() => executor.RunAsync(new GameplayWork(
                GameplayShardKey.World,
                GameplayCallChain.NewRoot(GameplayCallSource.Connection, context.ConnectionId),
                "test:direct-write",
                async ct =>
                {
                    Violations.Attach(seen);
                    try
                    {
                        await connection.SendAsync(Frame(1, 16), ct);
                    }
                    catch (GameplaySelfWaitException)
                    {
                        // Swallowed on purpose: the error sticks to the job.
                    }
                }), CancellationToken.None).AsTask());

            Assert.Contains("Waiting for the network (OutboundConnection.SendAsync)", error.Message, StringComparison.Ordinal);
            var violation = Assert.Single(seen);
            Assert.Equal(GameplayNetworkWaitAssertion.NetworkWaitKind, violation.Kind);
            Assert.Contains(log.Entries, entry => entry.Message.Contains("Gameplay test:direct-write failed", StringComparison.Ordinal)
                && entry.Message.Contains("chain", StringComparison.Ordinal));
            Assert.Equal(0, client.Available);
        }
    }

    [Fact]
    public async Task Two_frames_then_a_close_arrive_in_full_before_the_fin()
    {
        await using var server = await RealOutboundServer.StartAsync(confirmFlush: true);
        await using var sender = server.Sender(OutboundSendOptions.Default);
        var first = Frame(1, 1024);
        // Below the default 64 KiB pause threshold, so without a flush-confirming pipe the send returns
        // with all of it still in the pipe; that variant lost the frames in 5 of 5 idle runs.
        var second = Frame(2, 48 * 1024);

        for (var round = 0; round < 10; round++)
        {
            var (client, context) = await server.ConnectAsync();
            using (client)
            {
                var reading = ReadToEndAsync(client);
                OutboundEntry[] entries = [Batch(context, first), Batch(context, second), OutboundEntry.Close(context)];
                sender.Submit(entries);

                var (bytes, endedByFin) = await reading.WaitAsync(Patience);
                await server.Observer.WaitForAsync(3 * (round + 1), Patience);
                var outcomes = string.Join(",", entries.Select(server.Observer.OutcomeOf));
                Assert.True(first.Length + second.Length == bytes.Length,
                    $"round {round}: read {bytes.Length} bytes, ended by {(endedByFin ? "FIN" : "reset")}, outcomes {outcomes}");
                Assert.Equal([.. first, .. second], bytes);
                Assert.True(endedByFin, $"round {round}: the connection ended by reset, not by FIN");
            }
        }

        await server.Observer.WaitForAsync(30, Patience);
        Assert.All(server.Observer.Reports, report => Assert.Contains(
            report.Outcome, new[] { OutboundOutcome.Delivered, OutboundOutcome.Closed }));
        output.WriteLine($"rounds 10, reports {server.Observer.Reports.Count}");
    }

    [Fact]
    public async Task A_client_that_reads_only_after_the_close_still_gets_everything_and_the_fin()
    {
        await using var server = await RealOutboundServer.StartAsync(confirmFlush: true);
        await using var sender = server.Sender(OutboundSendOptions.Default);
        var first = Frame(1, 1024);
        var second = Frame(2, 48 * 1024);
        for (var round = 0; round < 5; round++)
        {
            var (client, context) = await server.ConnectAsync();
            using (client)
            {
                OutboundEntry[] entries = [Batch(context, first), Batch(context, second), OutboundEntry.Close(context)];
                sender.Submit(entries);
                await server.Observer.WaitForAsync(3 * (round + 1), Patience);
                Assert.Equal(OutboundOutcome.Closed, server.Observer.OutcomeOf(entries[2]));
                await Task.Delay(300);

                var (bytes, endedByFin) = await ReadToEndAsync(client).WaitAsync(Patience);
                Assert.Equal([.. first, .. second], bytes);
                Assert.True(endedByFin, $"round {round}: the connection ended by reset, not by FIN");
            }
        }
    }

    /// <summary>
    /// The batch is larger than the socket buffers, so part of it is still in the kernel when the close
    /// runs. Ordered close must preserve that kernel queue and FIN, including on Linux where
    /// TouchSocket's Close(0) otherwise resets the connection and discards the tail.
    /// </summary>
    [Fact]
    public async Task A_slow_reader_with_a_small_window_gets_a_large_batch_in_full_before_the_fin()
    {
        await using var server = await RealOutboundServer.StartAsync(confirmFlush: true);
        await using var sender = server.Sender(OutboundSendOptions.Default);
        var large = Frame(5, 900 * 1024);
        var (client, context) = await server.ConnectAsync(receiveBufferSize: 4096);
        using (client)
        {
            OutboundEntry[] entries = [Batch(context, large), OutboundEntry.Close(context)];
            sender.Submit(entries);
            await Task.Delay(300);

            var (bytes, endedByFin) = await ReadSlowlyToEndAsync(client).WaitAsync(Patience);
            await server.Observer.WaitForAsync(2, Patience);
            Assert.True(large.Length == bytes.Length,
                $"read {bytes.Length} bytes, outcomes {string.Join(",", entries.Select(server.Observer.OutcomeOf))}");
            Assert.Equal(large, bytes);
            Assert.True(endedByFin, "the connection ended by reset, not by FIN");
            Assert.Equal(OutboundOutcome.Closed, server.Observer.OutcomeOf(entries[1]));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordered_close_sends_fin_without_waiting_for_a_blocked_receive_callback_or_peer_fin(bool unreadInput)
    {
        await using var server = await RealOutboundServer.StartAsync(confirmFlush: true, pauseReceive: true);
        await using var sender = server.Sender(OutboundSendOptions.Default);
        var (client, context) = await server.ConnectAsync(receiveBufferSize: unreadInput ? 4096 : null);
        using (client)
        {
            var gate = server.BlockReceive(context);
            await client.GetStream().WriteAsync(new byte[] { 1 });
            await gate.Entered.Task.WaitAsync(Patience);
            try
            {
                if (unreadInput)
                {
                    var physical = server.PhysicalSocket(context);
                    physical.ReceiveBufferSize = 4096;
                    await client.GetStream().WriteAsync(new byte[64 * 1024]).AsTask().WaitAsync(Patience);
                    await WaitUntilAsync(() => physical.Available > 0);
                    Assert.True(physical.Available > 0, "The regression requires unread bytes in the kernel, not only in TouchSocket's pipe.");
                }
                var frame = Frame(7, unreadInput ? 900 * 1024 : 48 * 1024);
                var close = OutboundEntry.Close(context);
                sender.Submit([Batch(context, frame), close]);
                var (bytes, endedByFin) = await ReadToEndAsync(client).WaitAsync(Patience);
                Assert.True(frame.Length == bytes.Length,
                    $"unreadInput={unreadInput}: read {bytes.Length}/{frame.Length} bytes, FIN={endedByFin}");
                Assert.Equal(frame, bytes);
                Assert.True(endedByFin, $"unreadInput={unreadInput}: a complete {bytes.Length}-byte batch was followed by reset instead of FIN");
                // The peer is still open, and the callback cannot finish until the finally below.
                await server.Observer.WaitForAsync(2, OutboundLimits.DeliveryTimeout + TimeSpan.FromSeconds(5));
                Assert.Equal(OutboundOutcome.Closed, server.Observer.OutcomeOf(close));
                Assert.False(gate.Exited.Task.IsCompleted);
            }
            finally
            {
                gate.Release.TrySetResult();
                await gate.Exited.Task.WaitAsync(Patience);
            }
        }
    }

    [Fact]
    public async Task A_send_pipe_that_does_not_confirm_flushes_is_refused()
    {
        await using var server = await RealOutboundServer.StartAsync(confirmFlush: false);
        await using var sender = server.Sender(OutboundSendOptions.Default);
        var (client, context) = await server.ConnectAsync();
        using (client)
        {
            var entry = Batch(context, Frame(1, 16));
            sender.Submit([entry]);

            await server.Observer.WaitForAsync(1, Patience);
            var report = Assert.Single(server.Observer.Reports);
            Assert.Equal(OutboundOutcome.Failed, report.Outcome);
            Assert.Contains("confirm flushes", report.Error!.Message, StringComparison.Ordinal);
            var (bytes, _) = await ReadToEndAsync(client).WaitAsync(Patience);
            Assert.Empty(bytes);
        }
    }

    [Fact]
    public async Task An_entry_for_an_equal_but_not_identical_context_is_stale()
    {
        await using var server = await RealOutboundServer.StartAsync(confirmFlush: true);
        await using var sender = server.Sender(OutboundSendOptions.Default);
        var (client, context) = await server.ConnectAsync();
        using (client)
        {
            var lookalike = context with { };
            Assert.Equal(context, lookalike);
            var stale = Batch(lookalike, Frame(3, 16));
            var current = Batch(context, Frame(4, 16));

            sender.Submit([stale]);
            sender.Submit([current]);

            await server.Observer.WaitForAsync(2, Patience);
            Assert.Equal(OutboundOutcome.Stale, server.Observer.OutcomeOf(stale));
            Assert.Equal(OutboundOutcome.Delivered, server.Observer.OutcomeOf(current));
            Assert.Equal(Frame(4, 16), await ReadExactlyAsync(client, 16).WaitAsync(Patience));
            Assert.Empty(server.Observer.Failures);
        }
    }

    [Fact]
    public async Task A_client_that_stops_reading_times_out_and_is_closed_while_another_keeps_receiving()
    {
        await using var server = await RealOutboundServer.StartAsync(confirmFlush: true);
        await using var sender = server.Sender(OutboundSendOptions.Default with
        {
            QueueByteLimit = 256L * 1024 * 1024,
            DeliveryTimeout = TimeSpan.FromSeconds(1),
        });
        var (stalled, stalledContext) = await server.ConnectAsync(receiveBufferSize: 4096);
        var (healthy, healthyContext) = await server.ConnectAsync();
        using (stalled)
        using (healthy)
        {
            var healthyReading = ReadExactlyAsync(healthy, 8 * 1024);
            var large = Frame(7, 512 * 1024);
            var stalledEntries = Enumerable.Range(0, 32).Select(_ => Batch(stalledContext, large)).ToArray();
            for (var i = 0; i < stalledEntries.Length; i++)
                sender.Submit(i < 8 ? [stalledEntries[i], Batch(healthyContext, Frame((byte)i, 1024))] : [stalledEntries[i]]);

            Assert.Equal(8 * 1024, (await healthyReading.WaitAsync(Patience)).Length);
            await WaitUntilAsync(() => server.Observer.Failures.Any(failure => failure.Connection == stalledContext));
            Assert.Equal((stalledContext, OutboundOutcome.TimedOut),
                Assert.Single(server.Observer.Failures));
            Assert.Contains(server.Observer.Reports, report => report.Outcome == OutboundOutcome.TimedOut);
            await ReadToEndAsync(stalled).WaitAsync(Patience);
        }
    }

    [Fact]
    public async Task A_client_whose_queue_overflows_is_closed_while_another_keeps_receiving()
    {
        await using var server = await RealOutboundServer.StartAsync(confirmFlush: true);
        await using var sender = server.Sender(OutboundSendOptions.Default with { DeliveryTimeout = TimeSpan.FromSeconds(30) });
        var (stalled, stalledContext) = await server.ConnectAsync(receiveBufferSize: 4096);
        var (healthy, healthyContext) = await server.ConnectAsync();
        using (stalled)
        using (healthy)
        {
            var healthyReading = ReadExactlyAsync(healthy, 3 * 1024);
            var large = Frame(9, 600 * 1024);
            for (var i = 0; i < 8; i++)
                sender.Submit([Batch(stalledContext, large)]);
            for (var i = 0; i < 3; i++)
                sender.Submit([Batch(healthyContext, Frame((byte)i, 1024))]);

            Assert.Equal(3 * 1024, (await healthyReading.WaitAsync(Patience)).Length);
            await WaitUntilAsync(() => server.Observer.Failures.Any(failure => failure.Connection == stalledContext));
            Assert.Equal((stalledContext, OutboundOutcome.OverLimit), Assert.Single(server.Observer.Failures));
            await ReadToEndAsync(stalled).WaitAsync(Patience);
        }
    }

    // The blob's encoding is the frame itself (BlobEncoder); the entry carries it encoded, as registered.
    private static OutboundEntry Batch(NetworkConnectionContext context, byte[] frame) =>
        OutboundEntry.Batch(context, [new BlobMessage(frame)], frame);

    private static byte[] Frame(byte seed, int length)
    {
        var frame = new byte[length];
        for (var i = 0; i < length; i++)
            frame[i] = (byte)(seed + i * 31);
        return frame;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition did not hold in time.");
            await Task.Delay(10);
        }
    }

    private static Task<(byte[] Bytes, bool EndedByFin)> ReadToEndAsync(TcpClient client) => Task.Run(async () =>
    {
        var stream = client.GetStream();
        var received = new MemoryStream();
        var buffer = new byte[64 * 1024];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                    return (received.ToArray(), true);
                received.Write(buffer, 0, read);
            }
        }
        catch (IOException)
        {
            return (received.ToArray(), false);
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_real_completed_send_side_is_still_failed_but_logged_as_information(bool closeTransport)
    {
        await using var server = await RealOutboundServer.StartAsync(confirmFlush: true);
        var (client, context) = await server.ConnectAsync();
        var (healthyClient, healthy) = await server.ConnectAsync();
        using (client)
        using (healthyClient)
        {
            var gate = server.BlockReceive(context);
            var transport = new CapturedTransport(server.Transport());
            var session = server.Session(context);
            try
            {
                await client.GetStream().WriteAsync(new byte[] { 1 });
                await gate.Entered.Task.WaitAsync(Patience);
                var actualTransport = RealOutboundServer.PhysicalTransport(session);
                if (closeTransport)
                    await actualTransport.CloseAsync("test transport close", CancellationToken.None);
                else
                    await ((ITransportWriter)actualTransport).Writer.CompleteAsync();
                Assert.Equal(closeTransport, session.ClosedToken.IsCancellationRequested);
                Assert.True(session.Online);
                Assert.NotNull(transport.Resolve(context));
                var log = new SendLog();
                await using var sender = new OutboundSender(transport, [server.Observer], log, OutboundSendOptions.Default);
                var failed = Batch(context, Frame(1, 16));
                var discarded = Batch(context, Frame(2, 16));
                var sent = Batch(healthy, Frame(3, 16));
                var reading = ReadExactlyAsync(healthyClient, 16);

                sender.Submit([failed, discarded, sent]);
                await server.Observer.WaitForAsync(3, Patience);
                await transport.CloseStarted.Task.WaitAsync(Patience);
                Assert.Equal(OutboundOutcome.Failed, server.Observer.OutcomeOf(failed));
                Assert.Equal(OutboundOutcome.Discarded, server.Observer.OutcomeOf(discarded));
                Assert.Equal(OutboundOutcome.Delivered, server.Observer.OutcomeOf(sent));
                Assert.Equal(Frame(3, 16), await reading.WaitAsync(Patience));
                Assert.Equal((context, OutboundOutcome.Failed), Assert.Single(server.Observer.Failures));
                Assert.Equal(1, transport.CloseCalls);
                var report = Assert.Single(server.Observer.Reports, x => ReferenceEquals(x.Entry, failed));
                Assert.Same(transport.SendError, report.Error);
                Assert.IsType<InvalidOperationException>(report.Error);
                output.WriteLine($"closeTransport={closeTransport}; Online before send=true; ClosedToken before send={closeTransport}; error={report.Error}");
                var entry = Assert.Single(log.Entries, x => x.Message.StartsWith($"Outbound entry for {context.ConnectionId}: Failed", StringComparison.Ordinal));
                Assert.Same(report.Error, entry.Error);
                Assert.Equal(LogLevel.Information, entry.Level);
                Assert.EndsWith("; the connection was already gone", entry.Message, StringComparison.Ordinal);
                var closed = Assert.Single(log.Entries, x => x.Message.StartsWith($"Outbound connection {context.ConnectionId} closed: Failed", StringComparison.Ordinal));
                Assert.Equal(LogLevel.Information, closed.Level);
                Assert.EndsWith("; the connection was already gone", closed.Message, StringComparison.Ordinal);
                Assert.DoesNotContain(log.Entries, x => x.Level >= LogLevel.Warning);
            }
            finally
            {
                gate.Release.TrySetResult();
                if (gate.Entered.Task.IsCompleted)
                    await gate.Exited.Task.WaitAsync(Patience);
                if (transport.CloseStarted.Task.IsCompleted)
                {
                    await transport.Closed.Task.WaitAsync(Patience);
                    output.WriteLine($"ClosedToken after session close: {session.ClosedToken.IsCancellationRequested}");
                }
            }
        }
    }

    [Fact]
    public async Task The_writer_error_text_from_another_throw_site_does_not_mark_a_healthy_connection_gone()
    {
        await using var server = await RealOutboundServer.StartAsync(confirmFlush: true);
        var (client, context) = await server.ConnectAsync();
        using (client)
        {
            var session = server.Session(context);
            Assert.False(session.ClosedToken.IsCancellationRequested);
            var connection = server.Transport().Resolve(context)!;
            var unrelated = Assert.Throws<InvalidOperationException>((Action)(() =>
                throw new InvalidOperationException("Writing is not allowed after writer was completed.")));
            Assert.False(connection.HasClosedSendSide(unrelated));
            var frame = Frame(1, 16);
            var reading = ReadExactlyAsync(client, frame.Length);
            await connection.SendAsync(frame, CancellationToken.None);
            Assert.Equal(frame, await reading.WaitAsync(Patience));
        }
    }

    private sealed class SendLog : ILogger<OutboundSender>
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Error)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format) =>
            Entries.Enqueue((level, format(state, error), error));
    }

    // Transparent instrumentation: all sends/closes still reach the real TouchSocket connection.
    private sealed class CapturedTransport(IOutboundTransport inner) : IOutboundTransport
    {
        private int closeCalls;
        public int CloseCalls => Volatile.Read(ref closeCalls);
        public Exception? SendError { get; private set; }
        public TaskCompletionSource CloseStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IOutboundConnection? Resolve(NetworkConnectionContext expected) =>
            inner.Resolve(expected) is { } connection ? new CapturedConnection(this, connection) : null;
        private sealed class CapturedConnection(CapturedTransport owner, IOutboundConnection inner) : IOutboundConnection
        {
            public async ValueTask SendAsync(ReadOnlyMemory<byte> frames, CancellationToken cancellationToken)
            {
                try { await inner.SendAsync(frames, cancellationToken); }
                catch (Exception error) { owner.SendError = error; throw; }
            }
            public bool HasClosedSendSide(Exception error) => inner.HasClosedSendSide(error);

            public async ValueTask CloseAsync()
            {
                Interlocked.Increment(ref owner.closeCalls);
                owner.CloseStarted.TrySetResult();
                try { await inner.CloseAsync(); }
                finally { owner.Closed.TrySetResult(); }
            }
        }
    }

    private static Task<(byte[] Bytes, bool EndedByFin)> ReadSlowlyToEndAsync(TcpClient client) => Task.Run(async () =>
    {
        var stream = client.GetStream();
        var received = new MemoryStream();
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                    return (received.ToArray(), true);
                received.Write(buffer, 0, read);
                if (received.Length % (64 * 1024) < read)
                    await Task.Delay(1);
            }
        }
        catch (IOException)
        {
            return (received.ToArray(), false);
        }
    });

    private static Task<byte[]> ReadExactlyAsync(TcpClient client, int length) => Task.Run(async () =>
    {
        var buffer = new byte[length];
        await client.GetStream().ReadExactlyAsync(buffer);
        return buffer;
    });

    private sealed record BlobMessage(byte[] Frame) : IServerNetworkMessage
    {
        public ServerMessageType MessageType => default;
    }

    private sealed class BlobEncoder : ServerMessageEncoder<BlobMessage>
    {
        protected override IReadOnlyList<byte[]> EncodeMessage(BlobMessage message) => One(message.Frame);
    }

    private sealed class RealOutboundServer : IAsyncDisposable
    {
        private readonly GameTcpService service;
        private readonly TouchSocketConnectionRegistry registry;
        private readonly ServiceProvider provider;
        private readonly ConcurrentQueue<TaskCompletionSource<NetworkConnectionContext>> waiting = new();
        private readonly int port;
        private NetworkConnectionContext? blocked;
        private ReceiveGate? receiveGate;

        public sealed class ReceiveGate
        {
            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public ReceiveGate BlockReceive(NetworkConnectionContext context)
        {
            receiveGate = new ReceiveGate();
            blocked = context;
            return receiveGate;
        }

        public ITcpSessionClient Session(NetworkConnectionContext context) => registry.Get(context.ConnectionId).Session;

        public Socket PhysicalSocket(NetworkConnectionContext context) =>
            (Socket)PhysicalTransport(Session(context)).GetType().GetField("m_socket",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(PhysicalTransport(Session(context)))!;

        public static ITransport PhysicalTransport(ITcpSessionClient session) =>
            (ITransport)typeof(TcpSessionClientBase).GetProperty("Transport",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(session)!;

        private RealOutboundServer(GameTcpService service, TouchSocketConnectionRegistry registry, ServiceProvider provider, int port)
        {
            this.service = service;
            this.registry = registry;
            this.provider = provider;
            this.port = port;
        }

        public RecordingObserver Observer { get; } = new();

        public static async Task<RealOutboundServer> StartAsync(bool confirmFlush, bool pauseReceive = false)
        {
            var services = new ServiceCollection();
            services.AddServerMessageEncoder<BlobMessage, BlobEncoder>(NetworkEndpointKind.Game);
            var provider = services.BuildServiceProvider();
            var port = GetAvailablePort();
            var registry = new TouchSocketConnectionRegistry();
            // The production service type: a plain TcpService recycles its TcpCore on fast reconnects
            // (see DrainingTcpService), which lost or reset a round in 2 of 60 loaded runs.
            var service = new GameTcpService();
            var server = new RealOutboundServer(service, registry, provider, port);
            service.Connected = (client, _) =>
            {
                var context = registry.Add(client, NetworkEndpointKind.Game);
                if (server.waiting.TryDequeue(out var pending))
                    pending.TrySetResult(context);
                return EasyTask.CompletedTask;
            };
            service.Received = async (client, _) =>
            {
                if (server.receiveGate is { } gate && server.blocked?.ConnectionId ==
                    TouchSocketConnectionRegistry.CreateConnectionId(client, NetworkEndpointKind.Game))
                {
                    gate.Entered.TrySetResult();
                    try { await gate.Release.Task; }
                    finally { gate.Exited.TrySetResult(); }
                }
            };
            service.Closed = (client, closed) =>
            {
                registry.TryRemove(TouchSocketConnectionRegistry.CreateConnectionId(client, NetworkEndpointKind.Game), out _);
                return EasyTask.CompletedTask;
            };
            var config = new TouchSocketConfig().SetListenIPHosts(new IPHost(IPAddress.Loopback, port));
            if (confirmFlush || pauseReceive)
                config.SetTransportOption(option =>
                {
                    if (confirmFlush) option.SendPipeOptions = OutboundTransportOptions.CreateSendPipeOptions();
                    if (pauseReceive) option.ReceivePipeOptions = new PipeOptions(
                        pauseWriterThreshold: 1, resumeWriterThreshold: 1, useSynchronizationContext: false);
                });
            await service.SetupAsync(config);
            await service.StartAsync();
            return server;
        }

        public TouchSocketOutboundTransport Transport() => new(registry);

        public OutboundSender Sender(OutboundSendOptions options) => new(
            new TouchSocketOutboundTransport(registry),
            [Observer],
            NullLogger<OutboundSender>.Instance,
            options);

        public async Task<(TcpClient Client, NetworkConnectionContext Context)> ConnectAsync(int? receiveBufferSize = null)
        {
            var pending = new TaskCompletionSource<NetworkConnectionContext>(TaskCreationOptions.RunContinuationsAsynchronously);
            waiting.Enqueue(pending);
            var client = new TcpClient();
            if (receiveBufferSize is { } size)
                client.ReceiveBufferSize = size;
            await client.ConnectAsync(IPAddress.Loopback, port);
            return (client, await pending.Task.WaitAsync(Patience));
        }

        public async ValueTask DisposeAsync()
        {
            await service.StopAsync();
            service.Dispose();
            await provider.DisposeAsync();
        }

        private static int GetAvailablePort() => TestTcpPorts.Allocate();
    }
}
