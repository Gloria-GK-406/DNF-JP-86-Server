using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TouchSocket.Core;
using TouchSocket.Sockets;
using Xunit.Abstractions;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using TcpClient = System.Net.Sockets.TcpClient;

namespace Dfo.Server.Networking.Tests.Runtime;

/// <summary>
/// The premise of transitional rule 4 on a real TouchSocket service configured like production: with
/// both kernel buffers full, the write hands back a pending task, so the deadline gets control.
/// Measured on macOS only.
/// </summary>
public sealed class DirectSendDeadlineRealSocketTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);
    private const int FrameCount = 64;

    [Fact]
    public async Task A_job_sending_to_a_client_that_stops_reading_times_out_closes_it_and_frees_the_executor()
    {
        var deadline = TimeSpan.FromMilliseconds(500);
        var port = GetAvailablePort();
        var registry = new TouchSocketConnectionRegistry();
        var connected = new TaskCompletionSource<NetworkConnectionContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddServerMessageEncoder<BlobMessage, BlobEncoder>(NetworkEndpointKind.Game);
        services.AddSingleton<ServerMessageEncoderRegistry>();
        await using var provider = services.BuildServiceProvider();
        var log = new ListLogger<TouchSocketNetworkMessageSender>();
        var sender = new TouchSocketNetworkMessageSender(
            registry, provider.GetRequiredService<ServerMessageEncoderRegistry>(), log)
        {
            DirectSendDeadline = deadline,
        };

        using var service = new GameTcpService();
        service.Connected = (client, _) =>
        {
            connected.TrySetResult(registry.Add(client, NetworkEndpointKind.Game));
            return EasyTask.CompletedTask;
        };
        service.Closed = (client, closed) =>
        {
            registry.TryRemove(TouchSocketConnectionRegistry.CreateConnectionId(client, NetworkEndpointKind.Game), out _);
            return EasyTask.CompletedTask;
        };
        // Production's game listener: frame adapter, default send pipe thresholds.
        await service.SetupAsync(new TouchSocketConfig()
            .SetListenIPHosts(new IPHost(IPAddress.Loopback, port))
            .SetSingleStreamDataHandlingAdapter(() => new GameClientFrameDataHandlingAdapter()));
        await service.StartAsync();
        await using var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        try
        {
            using var client = new TcpClient { ReceiveBufferSize = 4096 };
            await client.ConnectAsync(IPAddress.Loopback, port);
            var context = await connected.Task.WaitAsync(Patience);
            var messages = Enumerable.Range(0, FrameCount).Select(i => (IServerNetworkMessage)new BlobMessage(i)).ToArray();

            var watch = Stopwatch.StartNew();
            var sending = executor.RunAsync(Work("test:stalled-send", async ct =>
            {
                await sender.SendBatchAsync(context, messages, ct);
                return true;
            }), CancellationToken.None).AsTask();
            var error = await Record(() => sending.WaitAsync(Patience));
            var elapsed = watch.Elapsed;
            output.WriteLine($"job ended after {elapsed.TotalMilliseconds:F0} ms with {error?.GetType().Name}");

            Assert.True(sending.IsCompleted, "the job is still waiting on the stalled send");
            Assert.IsType<TimeoutException>(error);
            Assert.InRange(elapsed, deadline, deadline + TimeSpan.FromSeconds(3));
            Assert.Contains(log.Entries, entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains(context.ConnectionId.ToString(), StringComparison.Ordinal));

            var (read, ending) = await ReadToEndAsync(client).WaitAsync(Patience);
            output.WriteLine($"client read {read} bytes before {ending}");
            Assert.True(read < (long)FrameCount * DnfFrameProtocol.MaximumFrameLength);

            var next = Stopwatch.StartNew();
            Assert.True(await executor.RunAsync(Work("test:after-stall", _ => ValueTask.FromResult(true)),
                CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(1)));
            output.WriteLine($"next job ran after {next.Elapsed.TotalMilliseconds:F0} ms");
        }
        finally
        {
            await service.StopAsync();
        }
    }

    [Fact]
    public void The_default_direct_send_deadline_is_two_seconds()
    {
        using var provider = new ServiceCollection().AddSingleton<ServerMessageEncoderRegistry>().BuildServiceProvider();
        var sender = new TouchSocketNetworkMessageSender(new TouchSocketConnectionRegistry(),
            provider.GetRequiredService<ServerMessageEncoderRegistry>(), NullLogger<TouchSocketNetworkMessageSender>.Instance);

        Assert.Equal(TimeSpan.FromSeconds(2), sender.DirectSendDeadline);
    }

    private static GameplayWork<T> Work<T>(string commandType, Func<CancellationToken, ValueTask<T>> body) =>
        new(GameplayShardKey.World, GameplayCallChain.NewRoot(GameplayCallSource.Connection), commandType, body);

    private static async Task<Exception?> Record(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    /// <summary>Reads until FIN or reset and reports how much arrived.</summary>
    private static Task<(long Read, string Ending)> ReadToEndAsync(TcpClient client) => Task.Run(async () =>
    {
        var stream = client.GetStream();
        var buffer = new byte[256 * 1024];
        long total = 0;
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0) return (total, "FIN");
                total += read;
            }
        }
        catch (IOException)
        {
            return (total, "reset");
        }
    });

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

    private sealed record BlobMessage(int Index) : IServerNetworkMessage
    {
        public ServerMessageType MessageType => default;
    }

    private sealed class BlobEncoder : ServerMessageEncoder<BlobMessage>
    {
        protected override IReadOnlyList<byte[]> EncodeMessage(BlobMessage message)
        {
            var frame = new byte[DnfFrameProtocol.MaximumFrameLength];
            frame[0] = (byte)message.Index;
            return [frame];
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
