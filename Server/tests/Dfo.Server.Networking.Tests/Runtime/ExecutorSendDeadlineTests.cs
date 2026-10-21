using System.Diagnostics;
using System.Net;
using System.Reflection;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Tests.Runtime;

/// <summary>
/// Transitional rule 4: a direct send awaited on the executor gives up after a deadline instead of
/// waiting for the transport (plan-review-r2 R-5). "On the executor" always comes from a real job.
/// </summary>
public sealed class ExecutorSendDeadlineTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly NetworkConnectionId Connection = new("Game:deadline");

    [Fact]
    public async Task On_the_executor_a_send_that_never_completes_times_out_and_closes_once()
    {
        var send = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closes = 0;

        var (error, elapsed) = await OnExecutorAsync(async () =>
        {
            var watch = Stopwatch.StartNew();
            var thrown = await Record(() => ExecutorSendDeadline.AwaitAsync(
                send.Task, Deadline, Connection, () => Interlocked.Increment(ref closes), default));
            return (thrown, watch.Elapsed);
        });

        var timeout = Assert.IsType<TimeoutException>(error);
        Assert.Contains(Connection.ToString(), timeout.Message, StringComparison.Ordinal);
        Assert.Contains(Deadline.ToString(), timeout.Message, StringComparison.Ordinal);
        Assert.InRange(elapsed, Deadline - TimeSpan.FromMilliseconds(20), Deadline + Slack);
        Assert.Equal(1, Volatile.Read(ref closes));
        send.SetResult();
    }

    [Fact]
    public async Task Off_the_executor_the_send_is_awaited_without_a_deadline()
    {
        var send = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closes = 0;

        var waiting = ExecutorSendDeadline.AwaitAsync(
            send.Task, Deadline, Connection, () => Interlocked.Increment(ref closes), default);
        await Task.Delay(Deadline * 2);
        Assert.False(waiting.IsCompleted);

        send.SetResult();
        await waiting.WaitAsync(Patience);
        Assert.Equal(0, Volatile.Read(ref closes));
    }

    [Fact]
    public async Task On_the_executor_a_send_that_completes_in_time_passes()
    {
        var closes = 0;

        var error = await OnExecutorAsync(() => Record(() => ExecutorSendDeadline.AwaitAsync(
            Task.Delay(TimeSpan.FromMilliseconds(20)), TimeSpan.FromSeconds(5), Connection,
            () => Interlocked.Increment(ref closes), default)));

        Assert.Null(error);
        Assert.Equal(0, Volatile.Read(ref closes));
    }

    [Fact]
    public async Task On_the_executor_the_sends_own_timeout_is_passed_through_and_does_not_close()
    {
        var own = new TimeoutException("the transport's own timeout");
        var closes = 0;

        var error = await OnExecutorAsync(() => Record(() => ExecutorSendDeadline.AwaitAsync(
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20));
                throw own;
            }), TimeSpan.FromSeconds(5), Connection, () => Interlocked.Increment(ref closes), default)));

        Assert.Same(own, error);
        Assert.Equal(0, Volatile.Read(ref closes));
    }

    [Fact]
    public async Task A_failing_timeout_log_neither_hides_the_timeout_nor_stops_the_close()
    {
        var registry = new TouchSocketConnectionRegistry();
        var session = DispatchProxy.Create<IStalledSession, StalledSession>();
        var context = registry.Add(session, NetworkEndpointKind.Game);
        var services = new ServiceCollection();
        services.AddServerMessageEncoder<Probe, ProbeEncoder>(NetworkEndpointKind.Game);
        services.AddSingleton<ServerMessageEncoderRegistry>();
        await using var provider = services.BuildServiceProvider();
        var sender = new TouchSocketNetworkMessageSender(
            registry, provider.GetRequiredService<ServerMessageEncoderRegistry>(), new ThrowingLog())
        {
            DirectSendDeadline = Deadline,
        };

        var error = await OnExecutorAsync(() => Record(async () =>
            await sender.SendAsync(context.ConnectionId, new Probe(), default)));

        Assert.IsType<TimeoutException>(error);
        await ((StalledSession)(object)session).Closed.Task.WaitAsync(Patience);
    }

    [Fact]
    public async Task On_the_executor_cancellation_is_a_cancellation_and_does_not_close()
    {
        var send = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var closes = 0;

        var error = await OnExecutorAsync(() =>
        {
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));
            return Record(() => ExecutorSendDeadline.AwaitAsync(
                send.Task, TimeSpan.FromSeconds(5), Connection, () => Interlocked.Increment(ref closes),
                cancellation.Token));
        });

        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(0, Volatile.Read(ref closes));
        send.SetResult();
    }

    [Fact]
    public async Task An_abandoned_send_that_fails_later_leaves_no_unobserved_exception()
    {
        var marker = "abandoned send " + Guid.NewGuid().ToString("N");
        var unobserved = 0;
        void Watch(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            if (args.Exception.Flatten().InnerExceptions.Any(e => e.Message == marker))
                Interlocked.Increment(ref unobserved);
        }

        TaskScheduler.UnobservedTaskException += Watch;
        try
        {
            await AbandonAndFailAsync(marker);
            for (var round = 0; round < 3; round++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(20);
            }
            Assert.Equal(0, Volatile.Read(ref unobserved));
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Watch;
        }
    }

    [Fact]
    public async Task A_detached_close_started_in_a_job_runs_off_the_executor_while_the_job_still_runs()
    {
        var observed = new TaskCompletionSource<(bool OnExecutor, bool JobEnded)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var jobEnded = false;

        var seen = await OnExecutorAsync(async () =>
        {
            ExecutorSendDeadline.StartDetachedClose(() =>
            {
                observed.TrySetResult((GameplayJob.IsOnExecutor, Volatile.Read(ref jobEnded)));
                return Task.CompletedTask;
            }, NullLogger.Instance);
            var result = await observed.Task.WaitAsync(Patience);
            Assert.True(GameplayJob.IsOnExecutor);
            Volatile.Write(ref jobEnded, true);
            return result;
        });

        Assert.False(seen.OnExecutor);
        Assert.False(seen.JobEnded);
    }

    [Fact]
    public async Task A_failing_detached_close_is_logged_as_a_warning()
    {
        var log = new WarningLog();

        ExecutorSendDeadline.StartDetachedClose(() => throw new IOException("close failed"), log);

        var (level, error) = await log.First.Task.WaitAsync(Patience);
        Assert.Equal(LogLevel.Warning, level);
        Assert.IsType<IOException>(error);
    }

    // Kept out of the test method so the abandoned task is unreachable when the GC runs.
    private static async Task AbandonAndFailAsync(string marker)
    {
        var send = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = await OnExecutorAsync(() => Record(() => ExecutorSendDeadline.AwaitAsync(
            send.Task, Deadline, Connection, () => { }, default)));
        Assert.IsType<TimeoutException>(error);
        send.SetException(new IOException(marker));
    }

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

    private sealed record Probe : IServerNetworkMessage
    {
        public ServerMessageType MessageType => default;
    }

    private sealed class ProbeEncoder : ServerMessageEncoder<Probe>
    {
        protected override IReadOnlyList<byte[]> EncodeMessage(Probe message) => [[0, 0, 0, 1]];
    }

    private sealed class ThrowingLog : ILogger<TouchSocketNetworkMessageSender>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => throw new InvalidOperationException("log provider failed");
    }

    public interface IStalledSession : ITcpSession, ITcpSessionClient;

    /// <summary>A session whose writes never complete and whose close is recorded.</summary>
    public class StalledSession : DispatchProxy
    {
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "get_Id":
                    return "stalled";
                case "get_LocalEndPoint" or "get_RemoteEndPoint":
                    return new IPEndPoint(IPAddress.Loopback, 10011);
                case "SendAsync":
                    return new TaskCompletionSource().Task;
                case "CloseAsync":
                    Closed.TrySetResult();
                    return Completed(method.ReturnType);
                default:
                    throw new NotSupportedException(method.Name);
            }
        }

        private static object Completed(Type type)
        {
            if (!type.IsGenericType) return Task.CompletedTask;
            var value = type.GenericTypeArguments[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(value)
                .Invoke(null, [value.IsValueType ? Activator.CreateInstance(value) : null])!;
        }
    }

    private sealed class WarningLog : ILogger
    {
        public TaskCompletionSource<(LogLevel Level, Exception? Error)> First { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => First.TrySetResult((logLevel, exception));
    }

    private static async Task<T> OnExecutorAsync<T>(Func<Task<T>> body)
    {
        await using var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        return await executor.RunAsync(new GameplayWork<T>(
            GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection, Connection),
            "test:send-deadline",
            async _ =>
            {
                Assert.True(GameplayJob.IsOnExecutor);
                return await body();
            }), CancellationToken.None).AsTask().WaitAsync(Patience);
    }
}
