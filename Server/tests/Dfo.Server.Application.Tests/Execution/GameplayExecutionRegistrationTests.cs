using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// Every container with a command bus carries the one executor and its queue: the dispatcher and the
/// bus will inject the queue, so the registration has to sit where they are registered.
/// </summary>
public sealed class GameplayExecutionRegistrationTests
{
    [Fact]
    public async Task Command_dispatching_alone_registers_one_executor_behind_the_queue()
    {
        // Deliberately no AddLogging: test containers built this way often have none.
        await using var provider = new ServiceCollection().AddCommandDispatching().BuildServiceProvider();

        var executor = provider.GetRequiredService<GameplayExecutor>();
        Assert.IsType<GameplayQueue>(provider.GetRequiredService<IGameplayQueue>());
        Assert.Same(executor, provider.GetRequiredService<GameplayExecutor>());
        var selector = provider.GetRequiredService<GameplayShardSelector>();
        Assert.Same(executor, selector.Select(GameplayShardKey.World));
        Assert.Same(executor, selector.Select(new GameplayShardKey("channel")));
    }

    [Fact]
    public async Task A_wrapped_region_given_the_container_runs_on_the_container_executor()
    {
        await using var provider = new ServiceCollection().AddCommandDispatching().BuildServiceProvider();
        var executor = provider.GetRequiredService<GameplayExecutor>();
        Assert.False(executor.IsStarted);

        var ran = await TestGameplay.RunAsync(provider, () => Task.FromResult(GameplayJob.IsOnExecutor));

        Assert.True(ran);
        Assert.True(executor.IsStarted);
    }

    [Fact]
    public async Task Disposing_the_container_ends_the_executor_loop()
    {
        var provider = new ServiceCollection().AddCommandDispatching().BuildServiceProvider();
        var executor = provider.GetRequiredService<GameplayExecutor>();
        await TestGameplay.RunAsync(provider, () => Task.CompletedTask);

        await provider.DisposeAsync();

        Assert.True(executor.Completion.IsCompleted);
    }
}
