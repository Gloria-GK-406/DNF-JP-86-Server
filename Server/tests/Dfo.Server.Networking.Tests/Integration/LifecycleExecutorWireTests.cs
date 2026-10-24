using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static Dfo.Server.Networking.Tests.Integration.LifecycleWireHost;

namespace Dfo.Server.Networking.Tests.Integration;

/// <summary>
/// Disconnect, a CMD's close and supersession each run their cleanup as a lifecycle CMD on the
/// executor (AC-007, spec appendix A rows 3 and 4), in order with the connection's inbound CMDs.
/// </summary>
public sealed class LifecycleExecutorWireTests(ITestOutputHelper output)
{
    /// <summary>How long a disconnect is given to (wrongly) start while the inbound job is held.</summary>
    private static readonly TimeSpan ObservationWindow = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task A_client_disconnect_runs_connect_and_disconnect_as_lifecycle_jobs_after_the_connections_inbound_job()
    {
        await using var server = await LifecycleWireHost.StartAsync();
        var (game, gameId) = await server.ConnectGameAsync();
        await game.GetStream().WriteAsync(GameFrame(ExitType, 1, []), server.Timeout);
        await WaitUntilAsync(() => server.Recorder.First("inbound-exit", gameId) is not null, server.Timeout);
        game.Dispose();
        await WaitUntilAsync(() => server.Recorder.First("disconnect-exit", gameId) is not null, server.Timeout);
        var (channel, channelId) = await server.ConnectChannelAsync();
        channel.Dispose();
        await WaitUntilAsync(() => server.Recorder.First("disconnect-exit", channelId) is not null, server.Timeout);

        foreach (var id in new[] { gameId, channelId })
        {
            AssertLifecycleJob(server.Recorder.First("connected", id)!, id, "lifecycle:connected");
            AssertLifecycleJob(server.Recorder.First("disconnect-enter", id)!, id, "lifecycle:disconnected");
        }
        Assert.Equal(["connected", "inbound-enter", "inbound-exit", "disconnect-enter"],
            server.Recorder.KindsOf(gameId, "connected", "inbound-enter", "inbound-exit", "disconnect-enter"));
    }

    [Fact]
    public async Task A_disconnect_during_an_inbound_job_starts_only_after_that_job_completes()
    {
        await using var server = await LifecycleWireHost.StartAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Recorder.OnInbound = (_, _, _, _) => release.Task;
        var (client, id) = await server.ConnectGameAsync();

        await client.GetStream().WriteAsync(GameFrame(ExitType, 1, []), server.Timeout);
        await WaitUntilAsync(() => server.Recorder.First("inbound-enter", id) is not null, server.Timeout);
        client.Dispose();
        await Task.Delay(ObservationWindow, server.Timeout);
        Assert.Null(server.Recorder.First("disconnect-enter", id));

        release.SetResult();
        await WaitUntilAsync(() => server.Recorder.First("disconnect-exit", id) is not null, server.Timeout);
        Assert.Equal(["inbound-enter", "inbound-exit", "disconnect-enter", "disconnect-exit"],
            server.Recorder.KindsOf(id, "inbound-enter", "inbound-exit", "disconnect-enter", "disconnect-exit"));
        AssertLifecycleJob(server.Recorder.First("disconnect-enter", id)!, id, "lifecycle:disconnected");
    }

    [Fact]
    public async Task A_cmd_closing_its_own_source_connection_does_not_wait_on_itself()
    {
        await using var server = await LifecycleWireHost.StartAsync();
        var requestedAt = 0L;
        server.Recorder.OnInbound = async (context, sender, closer, ct) =>
        {
            var self = context.Connection.ConnectionId;
            await sender.SendAsync(self, new LifecycleBlob(7, 16), ct);
            Volatile.Write(ref requestedAt, Stopwatch.GetTimestamp());
            await closer.RequestCloseAsync(self, ct);
            await Task.Delay(ObservationWindow, ct);
        };
        var (client, id) = await server.ConnectGameAsync();

        await client.GetStream().WriteAsync(GameFrame(ExitType, 1, []), server.Timeout);
        // The close is registered after the frame, so the frame always precedes the FIN (AC-007, F-20).
        var received = await ReadUntilFinAsync(client, server.Timeout);
        output.WriteLine($"frames before FIN: {received.Count}");
        var frame = Assert.Single(received);
        Assert.Equal(BlobType, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal(7, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(15)));
        var untilFin = Stopwatch.GetElapsedTime(Volatile.Read(ref requestedAt));
        output.WriteLine($"FIN {untilFin.TotalMilliseconds:F0} ms after the close request");
        Assert.True(untilFin < TimeSpan.FromSeconds(2), $"FIN after {untilFin}");

        await WaitUntilAsync(() => server.Recorder.First("disconnect-exit", id) is not null, server.Timeout);
        var disconnect = server.Recorder.First("disconnect-enter", id)!;
        AssertLifecycleChild(disconnect, id, id, new(GameplayCallSource.Connection, "inbound:GameExit"),
            server.Recorder.First("inbound-enter", id)!.Chain);
        Assert.True(disconnect.Order > server.Recorder.First("inbound-exit", id)!.Order);
        Assert.Equal(0, server.Log.Count("did not complete within"));
        Assert.Equal(0, server.Log.Count("Waiting for the network"));
        output.WriteLine($"next job ran in {(await server.NextJobLatencyAsync()).TotalMilliseconds:F0} ms");
    }

    [Fact]
    public async Task A_login_refused_by_the_real_handler_closes_the_connection_and_cleans_up_on_the_executor()
    {
        await using var server = await LifecycleWireHost.StartAsync();
        var (client, id) = await server.ConnectGameAsync();

        await client.GetStream().WriteAsync(
            GameFrame(0x0001, 1, DStrings("123456", "00000000000000000000000000000000")), server.Timeout);

        Assert.True(await ReadsFinAsync(client, server.Timeout));
        await WaitUntilAsync(() => server.Recorder.First("disconnect-exit", id) is not null, server.Timeout);
        AssertLifecycleChild(server.Recorder.First("disconnect-enter", id)!, id, id,
            new(GameplayCallSource.Connection, nameof(Dfo.Server.Protocol.Game.Login.GameLoginCommand)));
        output.WriteLine($"next job ran in {(await server.NextJobLatencyAsync()).TotalMilliseconds:F0} ms");
    }

    [Fact]
    public async Task A_cmd_closing_another_connection_has_its_cleanup_run_after_the_cmd()
    {
        await using var server = await LifecycleWireHost.StartAsync();
        var (requester, requesterId) = await server.ConnectGameAsync();
        var (idle, idleId) = await server.ConnectGameAsync();
        server.Recorder.OnInbound = async (context, _, closer, ct) =>
        {
            if (context.Connection.ConnectionId != requesterId) return;
            await closer.RequestCloseAsync(idleId, ct);
            await Task.Delay(ObservationWindow, ct);
        };

        await requester.GetStream().WriteAsync(GameFrame(ExitType, 1, []), server.Timeout);
        Assert.True(await ReadsFinAsync(idle, server.Timeout));
        await WaitUntilAsync(() => server.Recorder.First("disconnect-exit", idleId) is not null, server.Timeout);

        var disconnect = server.Recorder.First("disconnect-enter", idleId)!;
        AssertLifecycleChild(disconnect, idleId, requesterId, new(GameplayCallSource.Connection, "inbound:GameExit"),
            server.Recorder.First("inbound-enter", requesterId)!.Chain);
        Assert.True(disconnect.Order > server.Recorder.First("inbound-exit", requesterId)!.Order);
        Assert.Null(server.Recorder.First("disconnect-enter", requesterId));
    }

    [Fact]
    public async Task A_superseded_connection_is_closed_by_the_login_cmd_and_its_teardown_is_that_cmds_child()
    {
        await using var server = await LifecycleWireHost.StartAsync();
        var commands = server.Host.Services.GetRequiredService<ICommandBus>();
        await TestCredentials.RegisterAsync(server.Host.Services, "superseded", "integration password", server.Timeout);
        var first = await TestCredentials.LoginAsync(server.Host.Services, "superseded", "integration password", server.Timeout);
        var (client, id) = await server.ConnectGameAsync();
        await client.GetStream().WriteAsync(GameFrame(0x0001, 1, DStrings(first.MId!, first.Ticket!)), server.Timeout);
        var login = await ReadFrameAsync(client, server.Timeout);
        Assert.Equal((byte)1, login[0]);
        Assert.Equal(0x0001, BinaryPrimitives.ReadUInt16LittleEndian(login.AsSpan(1)));

        // A second login of the same account supersedes the bound connection.
        await TestCredentials.LoginAsync(server.Host.Services, "superseded", "integration password", server.Timeout);

        Assert.True(await DrainsToFinAsync(client, server.Timeout));
        await WaitUntilAsync(() => server.Recorder.First("disconnect-exit", id) is not null, server.Timeout);
        AssertLifecycleChild(server.Recorder.First("disconnect-enter", id)!, id, rootConnection: null,
            new(GameplayCallSource.Connection, "bus:" + nameof(AuthenticateAndIssueTicketCommand)));
    }

    /// <summary>
    /// R-5 (plan-review-r2), after T-006: a job's sends to a client that then resets are only registered, so
    /// the job never waits on that client at all; the sender closes it, its teardown is a lifecycle CMD that
    /// runs after the job, and the job enqueued next runs at once.
    /// </summary>
    [Fact]
    public async Task A_send_to_a_resetting_client_does_not_hold_the_executor()
    {
        const int attempts = 5;
        await using var server = await LifecycleWireHost.StartAsync();
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var (client, id) = await server.ConnectGameAsync(receiveBufferSize: 4096);
            var jobEnded = 0L;
            var job = server.RunAsync("test:send-until-reset", async ct =>
            {
                LifecycleRecorder.Marker.Value = "sending job";
                var sender = server.Host.Services.GetRequiredService<Dfo.Server.Abstractions.Networking.INetworkMessageSender>();
                // 15 x 64 KiB stays under the 1 MiB queue limit: the sender is still writing when the client
                // resets, instead of closing the connection as OverLimit before the reset.
                for (var index = 0; index < 15; index++)
                    await sender.SendAsync(id, new LifecycleBlob(index, 64 * 1024), ct);
                Volatile.Write(ref jobEnded, Stopwatch.GetTimestamp());
            });
            await job.WaitAsync(server.Timeout);
            var registeredAt = Volatile.Read(ref jobEnded);
            client.Client.LingerState = new System.Net.Sockets.LingerOption(true, 0);
            var resetAt = Stopwatch.GetTimestamp();
            client.Dispose();
            var latency = await server.NextJobLatencyAsync();

            await WaitUntilAsync(() => server.Recorder.First("disconnect-exit", id) is not null, server.Timeout);
            var callback = server.Recorder.First("closed-callback", id)!;
            var disconnect = server.Recorder.First("disconnect-enter", id)!;
            output.WriteLine($"attempt {attempt}: the job had ended {Stopwatch.GetElapsedTime(registeredAt, resetAt).TotalMilliseconds:F0} ms " +
                $"before the reset; close callback inline={callback.Flag}; next job ran in {latency.TotalMilliseconds:F0} ms");

            Assert.True(registeredAt < resetAt, "the sending job was still running when the client reset");
            Assert.True(latency < TimeSpan.FromSeconds(1), $"the next job waited {latency}");
            AssertLifecycleJob(disconnect, id, "lifecycle:disconnected");
            Assert.True(disconnect.Ticks >= registeredAt);
            // Recorded, not asserted (TASK-10 ruling 3): an inline close callback stops the unit.
            output.WriteLine($"K-INLINE={callback.Flag}");
        }
    }

    private static void AssertLifecycleJob(LifecycleEvent seen, NetworkConnectionId connection, string commandType)
    {
        Assert.True(seen.OnExecutor, $"{seen.Kind} of {connection} was off the executor");
        var chain = Assert.NotNull(seen.Chain);
        Assert.Equal(GameplayCallSource.Lifecycle, chain.Source);
        Assert.Equal(connection, chain.SourceConnection);
        Assert.Equal(commandType, chain.CommandType);
        Assert.Equal(0, chain.Depth);
    }

    /// <summary>
    /// The teardown of a connection a CMD asked to close is that CMD's child (RQ-050): one deeper, the CMD's root
    /// and its root's connection; <paramref name="parentHop"/> names the CMD.
    /// </summary>
    private static void AssertLifecycleChild(LifecycleEvent seen, NetworkConnectionId closed, NetworkConnectionId? rootConnection,
        GameplayCallHop parentHop, GameplayCallChain? parent = null)
    {
        Assert.True(seen.OnExecutor, $"{seen.Kind} of {closed} was off the executor");
        var chain = Assert.NotNull(seen.Chain);
        Assert.Equal(GameplayCallSource.Lifecycle, chain.Source);
        Assert.Equal("lifecycle:disconnected", chain.CommandType);
        Assert.Equal(1, chain.Depth);
        Assert.Equal(rootConnection, chain.SourceConnection);
        Assert.Equal(parentHop, Assert.Single(chain.Ancestors));
        if (parent is { } cmd)
        {
            Assert.Equal(cmd.Id, chain.ParentId);
            Assert.Equal(cmd.RootId, chain.RootId);
        }
    }

    /// <summary>Whole frames read until a FIN; a reset or a FIN inside a frame fails.</summary>
    private static async Task<List<byte[]>> ReadUntilFinAsync(TcpClient client, CancellationToken cancellationToken)
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

    private static async Task<bool> DrainsToFinAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try
        {
            while (await client.GetStream().ReadAsync(buffer, cancellationToken) > 0)
            {
            }
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static byte[] DStrings(params string[] values)
    {
        using var stream = new MemoryStream();
        foreach (var value in values)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            var length = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            stream.Write(length);
            stream.Write(bytes);
        }
        return stream.ToArray();
    }
}
