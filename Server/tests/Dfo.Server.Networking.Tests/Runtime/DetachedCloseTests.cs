using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TouchSocket.Core;
using TouchSocket.Sockets;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Dfo.Server.Networking.Tests.Runtime;

/// <summary>
/// The closer's detached close starts under suppressed flow (3e): it does not carry the requesting
/// job's token, so the close callbacks it may run are not on the executor.
/// </summary>
public sealed class DetachedCloseTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_close_requested_inside_a_job_runs_off_the_executor_while_the_job_still_runs()
    {
        var registry = new TouchSocketConnectionRegistry();
        var byId = Session("by-id");
        var exact = Session("exact");
        var byIdContext = registry.Add(byId, NetworkEndpointKind.Game);
        var exactContext = registry.Add(exact, NetworkEndpointKind.Game);
        var closer = new TouchSocketNetworkConnectionCloser(registry, NullLogger<TouchSocketNetworkConnectionCloser>.Instance);
        var jobEnded = false;
        Proxy(byId).OnClose = () => Volatile.Read(ref jobEnded);
        Proxy(exact).OnClose = () => Volatile.Read(ref jobEnded);

        await using var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        await executor.RunAsync(new GameplayWork(
            GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection, byIdContext.ConnectionId),
            "test:request-close",
            async ct =>
            {
                await closer.RequestCloseAsync(byIdContext.ConnectionId, ct);
                await closer.RequestCloseExactAsync(exactContext, ct);
                // The job waits until both closes have reported what they saw.
                await Task.WhenAll(Proxy(byId).Closed.Task, Proxy(exact).Closed.Task).WaitAsync(Patience, ct);
                Assert.True(GameplayJob.IsOnExecutor);
                Volatile.Write(ref jobEnded, true);
            }), CancellationToken.None).AsTask().WaitAsync(Patience);

        Assert.Equal((false, false), await Proxy(byId).Closed.Task);
        Assert.Equal((false, false), await Proxy(exact).Closed.Task);
    }

    [Fact]
    public async Task A_failing_close_is_logged_as_a_warning_and_not_thrown_to_the_requester()
    {
        var registry = new TouchSocketConnectionRegistry();
        var session = Session("failing");
        var context = registry.Add(session, NetworkEndpointKind.Game);
        Proxy(session).Failure = new IOException("close failed");
        var log = new WarningLog();
        var closer = new TouchSocketNetworkConnectionCloser(registry, log);

        await closer.RequestCloseAsync(context.ConnectionId, CancellationToken.None);

        var (message, error) = await log.First.Task.WaitAsync(Patience);
        Assert.Equal("Requested network connection close failed", message);
        Assert.IsType<IOException>(error);
    }

    public interface ITestSession : ITcpSession, ITcpSessionClient;

    private static ITestSession Session(string id)
    {
        var session = DispatchProxy.Create<ITestSession, SessionProxy>();
        ((SessionProxy)(object)session).Id = id;
        return session;
    }

    private static SessionProxy Proxy(ITestSession session) => (SessionProxy)(object)session;

    public class SessionProxy : DispatchProxy
    {
        public string Id { get; set; } = "";

        public Func<bool> OnClose { get; set; } = () => false;

        public Exception? Failure { get; set; }

        /// <summary>What the close saw: whether it was on the executor, and whether the job had ended.</summary>
        public TaskCompletionSource<(bool OnExecutor, bool JobEnded)> Closed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "get_Id") return Id;
            if (method.Name is "get_LocalEndPoint" or "get_RemoteEndPoint") return new IPEndPoint(IPAddress.Loopback, 10011);
            if (method.Name == "CloseAsync")
            {
                Closed.TrySetResult((GameplayJob.IsOnExecutor, OnClose()));
                return Failure is null ? Task.FromResult(Result.Success) : Task.FromException<Result>(Failure);
            }
            throw new NotSupportedException(method.Name);
        }
    }

    private sealed class WarningLog : ILogger<TouchSocketNetworkConnectionCloser>
    {
        public TaskCompletionSource<(string Message, Exception? Error)> First { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) First.TrySetResult((formatter(state, exception), exception));
        }
    }
}
