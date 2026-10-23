using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// F-59: the enqueue point refuses a flow leaked from a finished job, as the bus does, so it cannot become a
/// depth 0 root that slips past the depth limit. A flow detached from the job first is an ordinary producer.
/// </summary>
public sealed class GameplayQueueLeakTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Enqueueing_from_a_flow_leaked_from_a_finished_job_is_refused()
    {
        await using var provider = new ServiceCollection().AddGameplayExecution().BuildServiceProvider();
        var queue = provider.GetRequiredService<IGameplayQueue>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new ConcurrentQueue<GameplayAssertionViolation>();
        var ran = false;
        // Started inside the job without suppressing the flow, so it carries the job's token past the job's end.
        var leaked = await TestGameplay.RunAsync(provider, () => Task.FromResult(Task.Run(async () =>
        {
            await release.Task;
            ViolationCapture.Attach(seen);
            await queue.RunAsync(new GameplayWork(GameplayShardKey.World, GameplayCallChain.NewRoot(GameplayCallSource.Timer),
                "test:leaked", _ =>
                {
                    ran = true;
                    return ValueTask.CompletedTask;
                }), CancellationToken.None);
        }))).WaitAsync(Patience);
        release.TrySetResult();

        var thrown = await Assert.ThrowsAsync<GameplayLeakedFlowException>(() => leaked.WaitAsync(Patience));

        Assert.Contains("test:leaked", thrown.Message, StringComparison.Ordinal);
        Assert.False(ran);
        Assert.Equal("queue-leaked-flow", Assert.Single(seen).Kind);
    }

    [Fact]
    public async Task A_flow_detached_from_a_job_enqueues_as_a_new_root()
    {
        await using var provider = new ServiceCollection().AddGameplayExecution().BuildServiceProvider();
        var queue = provider.GetRequiredService<IGameplayQueue>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        GameplayWork<GameplayCallChain?> Work(string type) => new(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Timer), type, _ => ValueTask.FromResult(GameplayJob.Current));

        var started = await TestGameplay.RunAsync(provider, () =>
        {
            var detached = Task.Run(async () =>
            {
                await release.Task;
                return await GameplayJob.RunDetachedAsync(() => queue.RunAsync(Work("test:detached"), CancellationToken.None));
            });
            Task<GameplayCallChain?> suppressed;
            using (ExecutionContext.SuppressFlow())
                suppressed = Task.Run(async () =>
                {
                    await release.Task;
                    return await queue.RunAsync(Work("test:suppressed"), CancellationToken.None);
                });
            return Task.FromResult(new[] { detached, suppressed });
        }).WaitAsync(Patience);
        release.TrySetResult();

        foreach (var run in started)
        {
            var chain = Assert.NotNull(await run.WaitAsync(Patience));
            Assert.Equal(0, chain.Depth);
            Assert.Null(chain.ParentId);
            Assert.Equal(GameplayCallSource.Timer, chain.Source);
        }
    }
}
