using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Outbound;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TouchSocket.Core;
using TouchSocket.Sockets;
using Xunit.Abstractions;
using TcpClient = System.Net.Sockets.TcpClient;

namespace Dfo.Server.Networking.Tests.Outbound;

/// <summary>
/// The sender over a real, minimal TouchSocket service. The first case is the direct regression of
/// TASK-01's root cause: a close must not overtake frames that were already handed to the transport.
/// </summary>
public sealed class OutboundRealSocketTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

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
    /// runs. TouchSocket closes with Shutdown(Both) then Close(0); this pins that the tail still arrives
    /// (measured on macOS; Linux and Windows are for T-006 to confirm).
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

    private static OutboundEntry Batch(NetworkConnectionContext context, byte[] frame) =>
        OutboundEntry.Batch(context, [new BlobMessage(frame)]);

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

        private RealOutboundServer(GameTcpService service, TouchSocketConnectionRegistry registry, ServiceProvider provider, int port)
        {
            this.service = service;
            this.registry = registry;
            this.provider = provider;
            this.port = port;
        }

        public RecordingObserver Observer { get; } = new();

        public static async Task<RealOutboundServer> StartAsync(bool confirmFlush)
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
            service.Closed = (client, closed) =>
            {
                registry.TryRemove(TouchSocketConnectionRegistry.CreateConnectionId(client, NetworkEndpointKind.Game), out _);
                return EasyTask.CompletedTask;
            };
            var config = new TouchSocketConfig().SetListenIPHosts(new IPHost(IPAddress.Loopback, port));
            if (confirmFlush)
                OutboundTransportOptions.ConfigureSendPipe(config);
            await service.SetupAsync(config);
            await service.StartAsync();
            return server;
        }

        public OutboundSender Sender(OutboundSendOptions options) => new(
            new TouchSocketOutboundTransport(registry),
            new RegistryOutboundEncoder(
                new ServerMessageEncoderRegistry(provider.GetServices<ServerMessageEncoderRegistration>(), provider),
                NullLogger<RegistryOutboundEncoder>.Instance),
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

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
