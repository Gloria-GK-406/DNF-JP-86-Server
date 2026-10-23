using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Execution.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Application.Execution;

/// <summary>RQ-002: every key selects the one executor until sharding is designed.</summary>
public sealed class GameplayShardSelector(GameplayExecutor executor)
{
    public GameplayExecutor Select(GameplayShardKey key) => executor;
}

public sealed class GameplayQueue(GameplayShardSelector selector) : IGameplayQueue
{
    public ValueTask<T> RunAsync<T>(GameplayWork<T> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        return selector.Select(work.Key).RunAsync(work, cancellationToken);
    }

    public ValueTask RunAsync(GameplayWork work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        return selector.Select(work.Key).RunAsync(work, cancellationToken);
    }
}

public enum GameplaySourceTickOutcome { Enqueued, Coalesced, Rejected }

/// <summary>
/// <paramref name="Completion"/> is already complete when the tick was coalesced, and already failed
/// with the refusal when it was rejected.
/// </summary>
public readonly record struct GameplaySourceTick(GameplaySourceTickOutcome Outcome, Task Completion);

/// <summary>
/// RQ-006: one timer or wake-up source has at most one CMD outstanding; a tick that arrives before the
/// previous CMD completes is coalesced instead of enqueued.
/// </summary>
public sealed class CoalescingGameplaySource(IGameplayQueue queue)
{
    private int outstanding;

    public GameplaySourceTick TryRun(GameplayWork work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (Interlocked.CompareExchange(ref outstanding, 1, 0) != 0)
            return new GameplaySourceTick(GameplaySourceTickOutcome.Coalesced, Task.CompletedTask);

        // A refusal completes the call before it returns; a CMD that already ran and failed may too,
        // so "never started" is what tells the two apart.
        var start = new StartMark();
        var body = work.Body;
        var pending = queue.RunAsync(work with
        {
            Body = ct =>
            {
                start.Started = true;
                return body(ct);
            },
        }, cancellationToken);
        if (pending.IsCompleted && !pending.IsCompletedSuccessfully && !start.Started)
        {
            Volatile.Write(ref outstanding, 0);
            return new GameplaySourceTick(GameplaySourceTickOutcome.Rejected, pending.AsTask());
        }
        return new GameplaySourceTick(GameplaySourceTickOutcome.Enqueued, ReleaseAfterAsync(pending));
    }

    private async Task ReleaseAfterAsync(ValueTask pending)
    {
        try
        {
            await pending.ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref outstanding, 0);
        }
    }

    private sealed class StartMark
    {
        public volatile bool Started;
    }
}

public static class GameplayExecutionServiceCollectionExtensions
{
    /// <summary>
    /// Called by <c>AddCommandDispatching</c>, so every container with a command bus or dispatcher has
    /// the one executor; it stops when the container is disposed.
    /// </summary>
    public static IServiceCollection AddGameplayExecution(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // A container built from AddCommandDispatching alone may have no logging; the executor needs it.
        services.AddLogging();
        services.TryAddSingleton<GameplayExecutor>();
        services.TryAddSingleton<GameplayShardSelector>();
        services.TryAddSingleton<IGameplayQueue, GameplayQueue>();
        // Every send, close and immediate projection of a job is registered in its outbox and handed
        // to the outbound sender when the job ends (RQ-030); the transport registers none of them.
        // A close a CMD's outbox carries is the cause of that connection's teardown (RQ-050).
        services.TryAddSingleton<GameplayCloseCauses>();
        services.TryAddSingleton<GameplayDepthGuard>();
        services.TryAddSingleton(provider =>
        {
            var causes = provider.GetRequiredService<GameplayCloseCauses>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GameplayOutbox>>();
            return provider.GetService<IOutboundSender>() is { } sender
                ? new GameplayOutboxes(sender, causes, logger)
                : new GameplayOutboxes(causes, logger);
        });
        services.TryAddSingleton<INetworkMessageSender, OutboxNetworkMessageSender>();
        services.TryAddSingleton<INetworkConnectionCloser, OutboxConnectionCloser>();
        services.TryAddSingleton<IOutboundOrdering, GameplayOutboxOrdering>();
        // A host stops the executor after its services and producers (RQ-009); a bare container
        // still stops it on disposal.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, GameplayExecutorLifetime>());
        return services;
    }
}
