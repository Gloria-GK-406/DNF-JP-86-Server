using Dfo.Server.Testing;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Outbound;
using Dfo.Server.Networking.Tests.Outbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;
using static Dfo.Server.Networking.Tests.Integration.LifecycleWireHost;

namespace Dfo.Server.Networking.Tests.Integration;

/// <summary>
/// The outbox over a real host and real sockets (T-006): what a CMD registers reaches the client after the
/// job, in order, and a close of the source or of another connection comes after the frames registered
/// for it (AC-007 strict form, AC-011, RQ-005, RQ-031(c), F-83, VC-8).
/// </summary>
public sealed class OutboxWireTests(ITestOutputHelper output)
{
    /// <summary>Lets a client fall behind so that a close which overtook frames would cut them off.</summary>
    private static readonly TimeSpan ReaderDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Large enough (under the 1 MiB frame limit) that the socket buffers cannot take two of them, so a frame is still with the sender
    /// while the client is not reading; a close that overtook it would cut it off.
    /// </summary>
    private const int Large = 900 * 1024;

    /// <summary>RQ-005: when the dispatch completes, the reply is already with the sender; then it reaches the client.</summary>
    [Fact]
    public async Task An_inbound_cmds_reply_is_with_the_sender_when_the_dispatch_completes_and_reaches_the_client()
    {
        var recorder = new RecordingOutboundSender();
        var atCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await LifecycleWireHost.StartAsync(services => Observe(services, recorder,
            context => atCompletion.TrySetResult(Submitted(recorder, context.Connection, index: 11))));
        server.Recorder.OnInbound = (context, sender, _, ct) =>
            sender.SendAsync(context.Connection.ConnectionId, new LifecycleBlob(11, 16), ct).AsTask();
        var (client, _) = await server.ConnectGameAsync();

        await client.GetStream().WriteAsync(GameFrame(ExitType, 1, []), server.Timeout);

        var reply = await ReadFrameAsync(client, server.Timeout);
        Assert.Equal(11, BinaryPrimitives.ReadInt32LittleEndian(reply.AsSpan(15)));
        Assert.True(await atCompletion.Task.WaitAsync(server.Timeout), "the reply was not with the sender when the dispatch completed");
    }

    /// <summary>F-83: a CMD answering a connection that is already gone drops that answer and still succeeds.</summary>
    [Fact]
    public async Task A_reply_to_a_client_that_disconnected_first_is_dropped_and_the_cmd_succeeds()
    {
        await using var server = await LifecycleWireHost.StartAsync();
        var (requester, requesterId) = await server.ConnectGameAsync();
        var (gone, goneId) = await server.ConnectGameAsync();
        gone.Dispose();
        await WaitUntilAsync(() => server.Recorder.First("disconnect-exit", goneId) is not null, server.Timeout);
        server.Recorder.OnInbound = async (context, sender, _, ct) =>
        {
            await sender.SendAsync(goneId, new LifecycleBlob(1, 16), ct);
            await sender.SendAsync(context.Connection.ConnectionId, new LifecycleBlob(9, 16), ct);
        };

        await requester.GetStream().WriteAsync(GameFrame(ExitType, 1, []), server.Timeout);

        var reply = await ReadFrameAsync(requester, server.Timeout);
        Assert.Equal(9, Index(reply));
        await WaitUntilAsync(() => server.Recorder.First("inbound-exit", requesterId) is not null, server.Timeout);
        Assert.Equal(0, server.Log.Count(" failed: root"));
        Assert.Equal(0, server.Log.Count("Game handler failed"));
    }

    /// <summary>
    /// AC-011 item 2, RQ-031(c): a CMD that fails keeps what a committed unit and what no unit registered,
    /// drops what a rolled-back unit registered, and closes its source after the frames it kept.
    /// </summary>
    [Fact]
    public async Task A_failing_cmd_sends_what_it_kept_in_order_and_only_then_closes_its_source()
    {
        await using var server = await LifecycleWireHost.StartAsync(services =>
        {
            RoomForLargeFrames(services);
        });
        server.Recorder.OnInbound = async (context, sender, _, ct) =>
        {
            var id = context.Connection.ConnectionId;
            await SendInUnit(server.Host.Services,sender,id,1,false,ct);
            await sender.SendAsync(id, new LifecycleBlob(2, Large), ct);
            await SendInUnit(server.Host.Services,sender,id,3,true,ct);
            throw new InvalidOperationException("cmd failed after its sends");
        };
        var (client, id) = await server.ConnectGameAsync(receiveBufferSize: 4096);

        await client.GetStream().WriteAsync(GameFrame(ExitType, 1, []), server.Timeout);
        await Task.Delay(ReaderDelay, server.Timeout);

        var received = await ReadFramesUntilFinAsync(client, server.Timeout);
        Assert.Equal([1, 2], received.Select(Index));
        await WaitUntilAsync(() => server.Recorder.First("disconnect-exit", id) is not null, server.Timeout);
        Assert.Equal(1, server.Log.Count("Gameplay Inbound`2 failed", LogLevel.Error));
        Assert.Equal(0, server.Log.Count("Game handler failed", LogLevel.Error));
    }

    /// <summary>TASK-23 ruling 1: a message that cannot be encoded fails the CMD where it is sent; what came before still arrives.</summary>
    [Fact]
    public async Task A_cmd_whose_message_cannot_be_encoded_fails_and_its_earlier_frames_arrive_before_the_close()
    {
        await using var server = await LifecycleWireHost.StartAsync(RoomForLargeFrames);
        server.Recorder.OnInbound = async (context, sender, _, ct) =>
        {
            var id = context.Connection.ConnectionId;
            await sender.SendAsync(id, new LifecycleBlob(1, Large), ct);
            await sender.SendAsync(id, new Unencodable(), ct);
        };
        var (client, id) = await server.ConnectGameAsync(receiveBufferSize: 4096);

        await client.GetStream().WriteAsync(GameFrame(ExitType, 1, []), server.Timeout);
        await Task.Delay(ReaderDelay, server.Timeout);

        var received = await ReadFramesUntilFinAsync(client, server.Timeout);
        Assert.Equal([1], received.Select(Index));
        await WaitUntilAsync(() => server.Recorder.First("disconnect-exit", id) is not null, server.Timeout);
        Assert.Equal(1, server.Log.Count("Gameplay Inbound`2 failed", LogLevel.Error));
        Assert.Equal(0, server.Log.Count("Game handler failed", LogLevel.Error));
        Assert.Null(server.Recorder.First("inbound-exit", id));
    }

    /// <summary>AC-007 strict form: a CMD that closes another connection closes it after the frames it registered for it.</summary>
    [Fact]
    public async Task A_cmd_closing_another_connection_closes_it_after_the_frames_it_registered_for_it()
    {
        await using var server = await LifecycleWireHost.StartAsync(RoomForLargeFrames);
        var (requester, requesterId) = await server.ConnectGameAsync();
        var (other, otherId) = await server.ConnectGameAsync(receiveBufferSize: 4096);
        server.Recorder.OnInbound = async (context, sender, closer, ct) =>
        {
            if (context.Connection.ConnectionId != requesterId) return;
            await sender.SendAsync(otherId, new LifecycleBlob(5, Large), ct);
            await sender.SendAsync(otherId, new LifecycleBlob(6, 16), ct);
            await closer.RequestCloseAsync(otherId, ct);
        };

        await requester.GetStream().WriteAsync(GameFrame(ExitType, 1, []), server.Timeout);
        await Task.Delay(ReaderDelay, server.Timeout);

        var received = await ReadFramesUntilFinAsync(other, server.Timeout);
        Assert.Equal([5, 6], received.Select(Index));
        await WaitUntilAsync(() => server.Recorder.First("disconnect-exit", otherId) is not null, server.Timeout);
        Assert.Null(server.Recorder.First("disconnect-enter", requesterId));
    }

    /// <summary>
    /// VC-8 and AC-013: a client that stops reading is closed by the sender once its delivery runs out of
    /// time, and is torn down by its own lifecycle CMD; meanwhile another connection's CMD runs at once.
    /// </summary>
    [Fact]
    public async Task A_client_that_stops_reading_is_closed_by_the_sender_while_another_connections_cmd_is_answered_at_once()
    {
        await using var server = await LifecycleWireHost.StartAsync(services => services.AddSingleton(
            OutboundSendOptions.Default with { QueueByteLimit = 256L * 1024 * 1024, DeliveryTimeout = TimeSpan.FromSeconds(1) }));
        var (stalled, stalledId) = await server.ConnectGameAsync(receiveBufferSize: 4096);
        var (healthy, _) = await server.ConnectGameAsync();
        server.Recorder.OnInbound = (context, sender, _, ct) =>
            sender.SendAsync(context.Connection.ConnectionId, new LifecycleBlob(21, 16), ct).AsTask();

        await server.RunAsync("test:flood-the-stalled-client", async ct =>
        {
            var sender = server.Host.Services.GetRequiredService<INetworkMessageSender>();
            for (var index = 0; index < 32; index++)
                await sender.SendAsync(stalledId, new LifecycleBlob(index, 512 * 1024), ct);
        });
        var watch = Stopwatch.StartNew();
        await healthy.GetStream().WriteAsync(GameFrame(ExitType, 1, []), server.Timeout);
        var reply = await ReadFrameAsync(healthy, server.Timeout);
        var answered = watch.Elapsed;

        Assert.Equal(21, BinaryPrimitives.ReadInt32LittleEndian(reply.AsSpan(15)));
        output.WriteLine($"the healthy connection's CMD was answered in {answered.TotalMilliseconds:F0} ms");
        Assert.True(answered < TimeSpan.FromSeconds(1), $"answered after {answered}");
        await WaitUntilAsync(() => server.Recorder.First("disconnect-exit", stalledId) is not null, server.Timeout);
        Assert.True(server.Log.Count($"Outbound connection {stalledId} closed: TimedOut") == 1,
            string.Join("\n", server.Log.Entries.Select(entry => entry.Message)));
    }

    /// <summary>The host's sender, recorded; the dispatcher, observed as its dispatch completes.</summary>
    private static void Observe(IServiceCollection services, RecordingOutboundSender recorder, Action<NetworkDispatchContext> completed)
    {
        services.AddSingleton<OutboundSender>(provider => ActivatorUtilities.CreateInstance<OutboundSender>(provider));
        services.AddSingleton<IOutboundSender>(provider =>
        {
            recorder.Inner = provider.GetRequiredService<OutboundSender>();
            return recorder;
        });
        var original = services.Last(descriptor => descriptor.ServiceType == typeof(INetworkMessageDispatcher));
        services.Remove(original);
        services.AddSingleton<INetworkMessageDispatcher>(provider => new CompletionObserver(
            (INetworkMessageDispatcher)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!), completed));
    }

    private static bool Submitted(RecordingOutboundSender recorder, NetworkConnectionContext connection, int index) =>
        recorder.Submissions.SelectMany(batch => batch).Any(entry => ReferenceEquals(entry.Connection, connection)
            && entry.Messages.Any(message => message is LifecycleBlob blob && blob.Index == index));

    /// <summary>A queue limit that takes a few large frames; the default delivery time limit stays.</summary>
    private static void RoomForLargeFrames(IServiceCollection services) =>
        services.AddSingleton(OutboundSendOptions.Default with { QueueByteLimit = 64L * 1024 * 1024 });

    private static async ValueTask SendInUnit(IServiceProvider services,INetworkMessageSender sender,NetworkConnectionId id,int index,bool rollback,CancellationToken ct)
    {
        await using var unit=services.GetRequiredService<Dfo.Server.Application.Transactions.DomainTransactionScope>().Begin();
        await sender.SendAsync(id,new LifecycleBlob(index,Large),ct);
        if(rollback)unit.Transaction.RequireRollback();
        await unit.CommitAsync(ct);
    }

    private static int Index(byte[] frame) => BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(15));

    /// <summary>Whole frames read until a FIN; a reset or a FIN inside a frame fails.</summary>
    private static async Task<List<byte[]>> ReadFramesUntilFinAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var frames = new List<byte[]>();
        var header = new byte[Dfo.Server.Networking.Framing.GameServerFrameEncoder.HeaderLength];
        while (true)
        {
            var read = await client.GetStream().ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
            if (read == 0) return frames;
            Assert.Equal(header.Length, read);
            var frame = new byte[checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(3)))];
            header.CopyTo(frame, 0);
            await client.GetStream().ReadExactlyAsync(frame.AsMemory(header.Length), cancellationToken);
            frames.Add(frame);
        }
    }

    private sealed record Unencodable : IServerNetworkMessage
    {
        public ServerMessageType MessageType => default;
    }

    private sealed class CompletionObserver(INetworkMessageDispatcher inner, Action<NetworkDispatchContext> completed)
        : INetworkMessageDispatcher
    {
        public async ValueTask DispatchAsync(NetworkDispatchContext context, IClientNetworkMessage message, CancellationToken cancellationToken)
        {
            await inner.DispatchAsync(context, message, cancellationToken);
            completed(context);
        }
    }
}
