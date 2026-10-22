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
        // Transitional (baseline defect): the close can overtake a frame still in TouchSocket's send
        // pipe, so any prefix of what was sent precedes the FIN; T-006 makes the frame certain (AC-007).
        var received = await ReadUntilFinAsync(client, server.Timeout);
        output.WriteLine($"frames before FIN: {received.Count}");
        Assert.InRange(received.Count, 0, 1);
        Assert.All(received, frame =>
        {
            Assert.Equal(BlobType, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
            Assert.Equal(7, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(15)));
        });
        var untilFin = Stopwatch.GetElapsedTime(Volatile.Read(ref requestedAt));
        output.WriteLine($"FIN {untilFin.TotalMilliseconds:F0} ms after the close request");
        Assert.True(untilFin < TimeSpan.FromSeconds(2), $"FIN after {untilFin}");

        await WaitUntilAsync(() => server.Recorder.First("disconnect-exit", id) is not null, server.Timeout);
        var disconnect = server.Recorder.First("disconnect-enter", id)!;
        AssertLifecycleJob(disconnect, id, "lifecycle:disconnected");
        Assert.True(disconnect.Order > server.Recorder.First("inbound-exit", id)!.Order);
        Assert.Equal(0, server.Log.Count("did not complete within"));
        Assert.Equal(0, server.Log.Count("direct send deadline exceeded"));
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
        AssertLifecycleJob(server.Recorder.First("disconnect-enter", id)!, id, "lifecycle:disconnected");
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
        AssertLifecycleJob(disconnect, idleId, "lifecycle:disconnected");
        Assert.True(disconnect.Order > server.Recorder.First("inbound-exit", requesterId)!.Order);
        Assert.Null(server.Recorder.First("disconnect-enter", requesterId));
    }

    [Fact]
    public async Task A_superseded_connection_is_closed_by_maintenance_and_cleaned_up_on_the_executor()
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
        AssertLifecycleJob(server.Recorder.First("disconnect-enter", id)!, id, "lifecycle:disconnected");
    }

    /// <summary>
    /// R-5 (plan-review-r2): whether TouchSocket runs a close callback inline in a failing send. A job
    /// marks its execution context and keeps sending to a client that resets; the close callback
    /// shows whether it ran on that context. Measured on macOS only.
    /// </summary>
    [Fact]
    public async Task A_send_to_a_resetting_client_does_not_hold_the_executor_past_the_deadline()
    {
        const int attempts = 5;
        await using var server = await LifecycleWireHost.StartAsync();
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var (client, id) = await server.ConnectGameAsync(receiveBufferSize: 4096);
            var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var jobEnded = 0L;
            string? outcome = null;
            var job = server.RunAsync("test:send-until-reset", async ct =>
            {
                LifecycleRecorder.Marker.Value = "sending job";
                var sender = server.Host.Services.GetRequiredService<Dfo.Server.Abstractions.Networking.INetworkMessageSender>();
                try
                {
                    for (var index = 0; index < 4096; index++)
                    {
                        await sender.SendAsync(id, new LifecycleBlob(index, 64 * 1024), ct);
                        sending.TrySetResult();
                    }
                    outcome = "all sent";
                }
                catch (Exception error)
                {
                    outcome = error.GetType().Name;
                }
                finally
                {
                    sending.TrySetResult();
                    Volatile.Write(ref jobEnded, Stopwatch.GetTimestamp());
                }
            });
            await sending.Task.WaitAsync(server.Timeout);
            client.Client.LingerState = new System.Net.Sockets.LingerOption(true, 0);
            var resetAt = Stopwatch.GetTimestamp();
            client.Dispose();

            await job.WaitAsync(server.Timeout);
            var jobTook = Stopwatch.GetElapsedTime(resetAt, Volatile.Read(ref jobEnded));
            await WaitUntilAsync(() => server.Recorder.First("disconnect-exit", id) is not null, server.Timeout);
            var callback = server.Recorder.First("closed-callback", id)!;
            var disconnect = server.Recorder.First("disconnect-enter", id)!;
            var callbackDuringJob = callback.Ticks < Volatile.Read(ref jobEnded);
            output.WriteLine($"attempt {attempt}: job ended {jobTook.TotalMilliseconds:F0} ms after the reset with {outcome}; " +
                $"close callback inline={callback.Flag}, during the job={callbackDuringJob}; disconnect after job={disconnect.Ticks >= Volatile.Read(ref jobEnded)}");

            Assert.True(jobTook < TimeSpan.FromSeconds(2) + TimeSpan.FromSeconds(3), $"job held the executor {jobTook}");
            AssertLifecycleJob(disconnect, id, "lifecycle:disconnected");
            Assert.True(disconnect.Ticks >= Volatile.Read(ref jobEnded));
            output.WriteLine($"attempt {attempt}: next job ran in {(await server.NextJobLatencyAsync()).TotalMilliseconds:F0} ms");
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
