using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.Authentication;

/// <summary>
/// Login maintenance runs on the executor (spec appendix A row 4): each expiry sweep is one timer CMD,
/// and a superseded connection is disconnected and closed by the CMD that superseded it (F-65).
/// </summary>
public sealed class LoginSessionMaintenanceExecutorTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_superseded_connection_is_disconnected_and_then_closed_by_the_cmd_that_superseded_it()
    {
        var scope = new DomainTransactionScope();
        var clock = new RecordingClock();
        var steps = new ConcurrentQueue<Seen>();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessions = new LoginSessionRegistry(scope, clock, new LoginSessionOptions(),
            new RecordingOnlineUsers(steps), new RecordingCloser(steps, closed));
        var old = new NetworkConnectionId("Game:superseded");

        await SupersedeAsync(scope, sessions, clock, old);

        Assert.True(closed.Task.IsCompleted, "The close was not registered by the superseding CMD itself.");
        Assert.Equal(["disconnect Game:superseded", "close Game:superseded"], steps.Select(x => x.Step));
        Assert.All(steps, seen =>
        {
            Assert.True(seen.OnExecutor);
            var chain = Assert.NotNull(seen.Chain);
            Assert.Equal("test:" + nameof(SupersedeAsync), chain.CommandType);
        });
        Assert.Single(steps.Select(x => x.Chain!.Value.RootId).Distinct());
    }

    [Fact]
    public async Task An_expiry_sweep_runs_as_a_timer_job_on_the_executor()
    {
        await using var provider = Executor();
        var scope = new DomainTransactionScope();
        var clock = new RecordingClock();
        var sessions = new LoginSessionRegistry(scope, clock, new LoginSessionOptions());
        var issued = await TestGameplay.RunAsync(async () =>
        {
            await using var tx = scope.Begin();
            var session = sessions.Issue(new AccountId(5), clock.Now, clock.Now.AddSeconds(1));
            await tx.CommitAsync(default);
            return session;
        });
        clock.Now = clock.Now.AddSeconds(2);
        clock.Reads.Clear();
        var service = Service(provider, sessions, clock);

        await service.StartAsync(CancellationToken.None);
        try
        {
            // The clock is read by SweepExpired, and only there, once the service runs.
            while (clock.Reads.IsEmpty) await Task.Delay(10).WaitAsync(Patience);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        var read = clock.Reads.First();
        Assert.True(read.OnExecutor);
        var chain = Assert.NotNull(read.Chain);
        Assert.Equal(GameplayCallSource.Timer, chain.Source);
        Assert.Null(chain.SourceConnection);
        Assert.Equal("timer:LoginSessionMaintenanceService", chain.CommandType);
        // The expired session is gone: its pair no longer binds.
        await TestGameplay.RunAsync(async () =>
        {
            await using var tx = scope.Begin();
            Assert.Null(sessions.TryBind(new NetworkConnectionId("Game:late"), issued.MId, issued.Ticket));
        });
    }

    [Fact]
    public async Task Stopping_the_service_ends_both_loops_without_a_fault()
    {
        await using var provider = Executor();
        var clock = new RecordingClock();
        var sessions = new LoginSessionRegistry(new DomainTransactionScope(), clock, new LoginSessionOptions());
        var service = Service(provider, sessions, clock);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None).WaitAsync(Patience);

        var loops = Assert.IsAssignableFrom<Task>(service.ExecuteTask);
        Assert.True(loops.IsCompleted);
        Assert.False(loops.IsFaulted, loops.Exception?.ToString());
    }

    private static ServiceProvider Executor() => new ServiceCollection().AddGameplayExecution().BuildServiceProvider();

    private static LoginSessionMaintenanceService Service(
        IServiceProvider provider,
        LoginSessionRegistry sessions,
        TimeProvider clock) =>
        new(sessions, clock, provider.GetRequiredService<IGameplayQueue>())
        {
            SweepInterval = TimeSpan.FromMilliseconds(50),
        };

    /// <summary>Binds <paramref name="old"/>, then commits a second login of the same account.</summary>
    private static Task SupersedeAsync(DomainTransactionScope scope, LoginSessionRegistry sessions, RecordingClock clock,
        NetworkConnectionId old) => TestGameplay.RunAsync(async () =>
    {
        LoginSessionRegistry.IssuedLoginSession first;
        await using (var tx = scope.Begin())
        {
            first = sessions.Issue(new AccountId(4), clock.Now, clock.Now.AddSeconds(180));
            await tx.CommitAsync(default);
        }
        await using (var tx = scope.Begin())
        {
            Assert.NotNull(sessions.TryBind(old, first.MId, first.Ticket));
            await tx.CommitAsync(default);
        }
        await using (var tx = scope.Begin())
        {
            sessions.Issue(new AccountId(4), clock.Now, clock.Now.AddSeconds(180));
            await tx.CommitAsync(default);
        }
        Assert.True(sessions.IsSuperseded(old));
    });

    private sealed record Seen(string Step, bool OnExecutor, GameplayCallChain? Chain);

    private sealed class RecordingClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public ConcurrentQueue<Seen> Reads { get; } = new();

        public override DateTimeOffset GetUtcNow()
        {
            Reads.Enqueue(new("now", GameplayJob.IsOnExecutor, GameplayJob.Current));
            return Now;
        }
    }

    private sealed class RecordingOnlineUsers(ConcurrentQueue<Seen> steps) : IOnlineUserDomainService
    {
        public bool TryDisconnectConnection(NetworkConnectionId connectionId)
        {
            steps.Enqueue(new($"disconnect {connectionId}", GameplayJob.IsOnExecutor, GameplayJob.Current));
            return true;
        }

        public OnlineUserSnapshot GetRequired(OnlineUserId onlineUserId) => throw new NotSupportedException();

        public bool TryGetByConnection(NetworkConnectionId connectionId, out OnlineUserSnapshot? onlineUser) =>
            throw new NotSupportedException();

        public OnlineUserSnapshot RegisterConnection(NetworkConnectionContext connection) => throw new NotSupportedException();

        public bool TryDisconnect(OnlineUserId onlineUserId, NetworkConnectionId connectionId, long expectedGeneration) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingCloser(ConcurrentQueue<Seen> steps, TaskCompletionSource closed) : INetworkConnectionCloser
    {
        public ValueTask RequestCloseExactAsync(NetworkConnectionContext expected, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask RequestCloseAsync(NetworkConnectionId connectionId, CancellationToken cancellationToken)
        {
            steps.Enqueue(new($"close {connectionId}", GameplayJob.IsOnExecutor, GameplayJob.Current));
            closed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
