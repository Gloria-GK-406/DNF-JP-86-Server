using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Infrastructure.Configuration;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Channel.Connect;
using Dfo.Server.Protocol.Game.Exit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Networking.Tests.Integration;

/// <summary>
/// The real inbound path (TouchSocket, the production plugins and the real dispatcher) runs every
/// non-telemetry message as a connection-rooted job on the executor, one connection at a time
/// (AC-003 first two items, spec appendix A row 1, appendix B telemetry row).
/// </summary>
/// <remarks>
/// GameExit and ChannelConnect are rerouted to a probe handler, GamePi to a telemetry probe. A
/// telemetry handler may only inject logging, so the recorder is static; the tests of this class run
/// one at a time and each resets it.
/// </remarks>
public sealed class InboundExecutorWireTests
{
    // GAME_EXIT (empty body) and PI (a telemetry report), as registered by ProtocolModule.
    private const ushort ExitType = 0x0003;
    private const ushort PiType = 0x011B;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    /// <summary>How long the second frame is given to (wrongly) start while the first one is held.</summary>
    private static readonly TimeSpan ObservationWindow = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task A_game_and_a_channel_message_each_run_as_an_inbound_job_rooted_at_their_connection()
    {
        Recorder.Reset();
        await using var server = await Server.StartAsync();
        using var game = await server.ConnectGameAsync();
        using var channel = await server.ConnectChannelAsync();

        await game.GetStream().WriteAsync(GameFrame(ExitType, 1, []), server.Timeout);
        await channel.GetStream().WriteAsync(ChannelFrameEncoder.Encode(0x7C, 0x0B, 0, 1, ReadOnlySpan<byte>.Empty), server.Timeout);
        await WaitUntilAsync(() => Recorder.Events.Count(x => x.Kind == "returned") == 2, server.Timeout);

        var entered = Recorder.Events.Where(x => x.Kind == "enter").ToArray();
        Assert.Equal(2, entered.Length);
        var onGame = Assert.Single(entered, x => x.Connection.Value.StartsWith("Game:", StringComparison.Ordinal));
        var onChannel = Assert.Single(entered, x => x.Connection.Value.StartsWith("Channel:", StringComparison.Ordinal));
        AssertInboundJob(onGame, "inbound:GameExit");
        AssertInboundJob(onChannel, "inbound:ChannelConnect");
    }

    [Fact]
    public async Task The_next_frame_of_a_connection_starts_only_after_the_previous_job_completes()
    {
        Recorder.Reset();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Recorder.Hold = number => number == 1 ? release.Task : Task.CompletedTask;
        await using var server = await Server.StartAsync();
        using var client = await server.ConnectGameAsync();

        await client.GetStream().WriteAsync(GameFrame(ExitType, 1, []), server.Timeout);
        await client.GetStream().WriteAsync(GameFrame(ExitType, 2, []), server.Timeout);
        await WaitUntilAsync(() => Recorder.Events.Any(x => x.Kind == "enter" && x.Number == 1), server.Timeout);
        await Task.Delay(ObservationWindow, server.Timeout);
        Assert.DoesNotContain(Recorder.Events, x => x.Kind == "enter" && x.Number == 2);

        release.SetResult();
        await WaitUntilAsync(() => Recorder.Events.Count(x => x.Kind == "returned") == 2, server.Timeout);
        Assert.Equal(["enter 1", "exit 1", "enter 2", "exit 2"], Recorder.Events
            .Where(x => x.Kind is "enter" or "exit").Select(x => $"{x.Kind} {x.Number}"));
    }

    [Fact]
    public async Task Two_connections_sending_alternately_keep_their_own_order_and_never_overlap()
    {
        const int perConnection = 20;
        Recorder.Reset();
        await using var server = await Server.StartAsync();
        using var first = await server.ConnectGameAsync();
        using var second = await server.ConnectGameAsync();

        for (ushort number = 1; number <= perConnection; number++)
        {
            await first.GetStream().WriteAsync(GameFrame(ExitType, number, []), server.Timeout);
            await second.GetStream().WriteAsync(GameFrame(ExitType, number, []), server.Timeout);
        }
        await WaitUntilAsync(() => Recorder.Events.Count(x => x.Kind == "returned") == 2 * perConnection, server.Timeout);

        var entered = Recorder.Events.Where(x => x.Kind == "enter").ToArray();
        Assert.Equal(2 * perConnection, entered.Length);
        var byConnection = entered.GroupBy(x => x.Connection).ToArray();
        Assert.Equal(2, byConnection.Length);
        Assert.All(byConnection, group =>
            Assert.Equal(Enumerable.Range(1, perConnection), group.Select(x => x.Number)));
        Assert.All(entered, x => AssertInboundJob(x, "inbound:GameExit"));
        Assert.Equal(0, Recorder.Overlaps);
    }

    [Fact]
    public async Task A_telemetry_frame_is_handled_off_the_executor_after_the_previous_job_completed()
    {
        Recorder.Reset();
        await using var server = await Server.StartAsync();
        using var client = await server.ConnectGameAsync();

        await client.GetStream().WriteAsync(GameFrame(ExitType, 1, []), server.Timeout);
        await client.GetStream().WriteAsync(GameFrame(PiType, 2, [0x01, 0x02]), server.Timeout);
        await WaitUntilAsync(() => Recorder.Events.Count(x => x.Kind == "returned") == 2, server.Timeout);

        AssertInboundJob(Assert.Single(Recorder.Events, x => x.Kind == "enter"), "inbound:GameExit");
        var telemetry = Assert.Single(Recorder.Events, x => x.Kind == "telemetry");
        Assert.False(telemetry.OnExecutor);
        Assert.Null(telemetry.Chain);
        Assert.Equal(1, telemetry.Number);
        Assert.Equal(["enter", "exit", "returned", "telemetry", "returned"], Recorder.Events.Select(x => x.Kind));
    }

    private static void AssertInboundJob(Event seen, string commandType)
    {
        Assert.True(seen.OnExecutor);
        var chain = Assert.NotNull(seen.Chain);
        Assert.Equal(GameplayCallSource.Connection, chain.Source);
        Assert.Equal(seen.Connection, chain.SourceConnection);
        Assert.Equal(commandType, chain.CommandType);
        Assert.Equal(0, chain.Depth);
    }

    private sealed record Event(string Kind, NetworkConnectionId Connection, int Number, bool OnExecutor, GameplayCallChain? Chain);

    private static class Recorder
    {
        private static int active;

        public static ConcurrentQueue<Event> Events { get; private set; } = new();

        public static Func<int, Task> Hold { get; set; } = _ => Task.CompletedTask;

        public static int Overlaps;

        public static int Active => Volatile.Read(ref active);

        public static void Reset()
        {
            Events = new();
            Hold = _ => Task.CompletedTask;
            Overlaps = 0;
            active = 0;
        }

        public static void Enter(NetworkConnectionId connection, int number)
        {
            if (Interlocked.Increment(ref active) != 1) Interlocked.Increment(ref Overlaps);
            Events.Enqueue(new("enter", connection, number, GameplayJob.IsOnExecutor, GameplayJob.Current));
        }

        public static void Exit(NetworkConnectionId connection, int number)
        {
            Events.Enqueue(new("exit", connection, number, GameplayJob.IsOnExecutor, GameplayJob.Current));
            Interlocked.Decrement(ref active);
        }
    }

    private sealed class ProbeHandler : IClientMessageHandler
    {
        public bool CanHandle(IClientNetworkMessage message) => message is GameExitRequest or ChannelConnectRequest;

        public async ValueTask HandleAsync(NetworkDispatchContext context, IClientNetworkMessage message, CancellationToken cancellationToken)
        {
            var number = message is GameExitRequest exit ? exit.Metadata.Sequence : 0;
            Recorder.Enter(context.Connection.ConnectionId, number);
            // Gives a concurrently running second job the chance to show up as an overlap.
            await Task.Delay(1, cancellationToken);
            await Recorder.Hold(number);
            Recorder.Exit(context.Connection.ConnectionId, number);
        }
    }

    private sealed class TelemetryProbe(ILogger<TelemetryProbe> logger) : IClientMessageHandler
    {
        public bool CanHandle(IClientNetworkMessage message) => message.MessageType == ClientMessageType.GamePi;

        public ValueTask HandleAsync(NetworkDispatchContext context, IClientNetworkMessage message, CancellationToken cancellationToken)
        {
            logger.LogDebug("telemetry probe {ConnectionId}", context.Connection.ConnectionId);
            var returned = Recorder.Events.Count(x => x.Kind == "returned" && x.Connection == context.Connection.ConnectionId);
            Recorder.Events.Enqueue(new("telemetry", context.Connection.ConnectionId, returned, GameplayJob.IsOnExecutor, GameplayJob.Current));
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Records when the plugin's await on the real dispatcher has returned.</summary>
    private sealed class ReturnRecordingDispatcher(INetworkMessageDispatcher inner) : INetworkMessageDispatcher
    {
        public async ValueTask DispatchAsync(NetworkDispatchContext context, IClientNetworkMessage message, CancellationToken cancellationToken)
        {
            await inner.DispatchAsync(context, message, cancellationToken);
            Recorder.Events.Enqueue(new("returned", context.Connection.ConnectionId, 0, GameplayJob.IsOnExecutor, GameplayJob.Current));
        }
    }

    private sealed class Server : IAsyncDisposable
    {
        private readonly TestInfrastructure host;
        private readonly CancellationTokenSource timeout = new(Patience);
        private readonly int channelPort;
        private readonly int gamePort;

        private Server(TestInfrastructure host, int channelPort, int gamePort)
        {
            this.host = host;
            this.channelPort = channelPort;
            this.gamePort = gamePort;
        }

        public CancellationToken Timeout => timeout.Token;

        public static async Task<Server> StartAsync()
        {
            var channel = Port(); var game = Port(); while (game == channel) game = Port();
            var builder = Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Networking:Enabled"] = "true", ["Networking:ChannelPort"] = channel.ToString(),
                ["Networking:GamePorts:0"] = game.ToString(), ["Game:EndpointHost"] = "127.0.0.1",
            });
            builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
            builder.Services.AddSingleton<IServerConfigService>(new ServerConfigService(builder.Configuration));
            builder.Services.AddSingleton<IChannelCatalog>(StaticChannelCatalog.ForGamePort(game));
            builder.Services.AddSingleton<IChannelScriptSource>(new NoChannelScript());
            builder.Services.AddSingleton(ChannelEntryOptions.Legacy);
            new ApplicationModule().ConfigureServices(builder.Services, builder.Configuration);
            new NetworkingModule().ConfigureServices(builder.Services, builder.Configuration);
            new ProtocolModule().ConfigureServices(builder.Services, builder.Configuration);
            Reroute(builder.Services, ClientMessageType.GameExit, ClientMessageType.ChannelConnect, ClientMessageType.GamePi);
            builder.Services.RegisterMessageHandler<ProbeHandler>(ClientMessageType.GameExit);
            builder.Services.RegisterMessageHandler<ProbeHandler>(ClientMessageType.ChannelConnect);
            builder.Services.RegisterTelemetryMessageHandler<TelemetryProbe>(ClientMessageType.GamePi);
            var original = builder.Services.Last(x => x.ServiceType == typeof(INetworkMessageDispatcher));
            builder.Services.Remove(original);
            builder.Services.AddSingleton<INetworkMessageDispatcher>(sp => new ReturnRecordingDispatcher(
                (INetworkMessageDispatcher)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)));
            var host = TestInfrastructure.Build(builder);
            var server = new Server(host, channel, game);
            try
            {
                await host.StartAsync(server.Timeout);
            }
            catch
            {
                await server.DisposeAsync();
                throw;
            }
            return server;
        }

        public async Task<TcpClient> ConnectGameAsync()
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, gamePort, Timeout);
            // The initial connection notice: the connection is registered once it has been sent.
            var header = new byte[GameServerFrameEncoder.HeaderLength];
            await client.GetStream().ReadExactlyAsync(header, Timeout);
            var rest = new byte[checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(3))) - header.Length];
            await client.GetStream().ReadExactlyAsync(rest, Timeout);
            return client;
        }

        public async Task<TcpClient> ConnectChannelAsync()
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, channelPort, Timeout);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            try { await host.StopAsync(CancellationToken.None); }
            finally
            {
                host.Dispose();
                timeout.Dispose();
            }
        }

        private static void Reroute(IServiceCollection services, params ClientMessageType[] types)
        {
            foreach (var type in types)
                services.Remove(services.Single(x => x.ServiceType == typeof(MessageHandlerRegistration)
                    && ((MessageHandlerRegistration)x.ImplementationInstance!).MessageType == type));
        }
    }

    private static byte[] GameFrame(ushort type, ushort sequence, ReadOnlySpan<byte> body)
    {
        var frame = new byte[GameClientFrameRequestInfo.HeaderLength + body.Length]; frame[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), type);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), (uint)frame.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(12), sequence);
        body.CopyTo(frame.AsSpan(GameClientFrameRequestInfo.HeaderLength)); return frame;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition()) await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
    }

    private static int Port()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
