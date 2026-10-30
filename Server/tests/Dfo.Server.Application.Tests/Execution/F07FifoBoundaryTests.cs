using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Protocol.Game.Fatigue;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Execution;

public sealed class F07FifoBoundaryTests
{
    [Fact]
    public async Task A_request_already_queued_after_the_scan_runs_before_the_session_refresh()
    {
        var order = new ConcurrentQueue<string>();
        ScanBoundaryQueue? boundary = null;
        using var shop = new ShopFixture(services => services.AddSingleton<IGameplayQueue>(provider =>
            boundary = new ScanBoundaryQueue(new GameplayQueue(provider.GetRequiredService<GameplayShardSelector>()))));
        var refresh = ActivatorUtilities.CreateInstance<Dfo.Server.Protocol.Game.StateSynchronization.SelectedSessionRefreshService>(shop.Services, new Dfo.Server.Protocol.Game.StateSynchronization.SelectedSessionRefreshTopics([Dfo.Server.Abstractions.Sessions.SelectedSessionRefreshTopic.Fatigue]));
        shop.Sender.BeforeSend = () => order.Enqueue("refresh");
        var run = refresh.RefreshAsync(default);
        var queue = Assert.IsType<ScanBoundaryQueue>(boundary);
        await queue.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var request = queue.RunAsync(new GameplayWork(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection), "test:intervening-request", _ =>
            { order.Enqueue("request"); return ValueTask.CompletedTask; }), default).AsTask();
        queue.Release.TrySetResult();
        await Task.WhenAll(run, request).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { "request", "refresh" }, order);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stale_scan_inputs_never_register_and_the_next_scan_covers_the_current_period_or_session(bool replace)
    {
        ScanBoundaryQueue? boundary = null;
        using var shop = new ShopFixture(services => services.AddSingleton<IGameplayQueue>(provider =>
            boundary = new ScanBoundaryQueue(new GameplayQueue(provider.GetRequiredService<GameplayShardSelector>()))));
        var refresh = ActivatorUtilities.CreateInstance<Dfo.Server.Protocol.Game.StateSynchronization.SelectedSessionRefreshService>(shop.Services, new Dfo.Server.Protocol.Game.StateSynchronization.SelectedSessionRefreshTopics([Dfo.Server.Abstractions.Sessions.SelectedSessionRefreshTopic.Fatigue]));
        var first = refresh.RefreshAsync(default); var queue = Assert.IsType<ScanBoundaryQueue>(boundary);
        await queue.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var change = queue.RunAsync(new GameplayWork(GameplayShardKey.World, GameplayCallChain.NewRoot(GameplayCallSource.Connection), "test:change-current", _ =>
        { if (replace) shop.Select(2); else shop.Clock.Advance(TimeSpan.FromDays(1)); return ValueTask.CompletedTask; }), default).AsTask();
        queue.Release.TrySetResult(); await Task.WhenAll(first, change).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(shop.Sender.Messages);
        await refresh.RefreshAsync(default); await refresh.RefreshAsync(default);
        Assert.Single(shop.Sender.Messages);
    }
    [Fact]
    public async Task Drain_includes_an_accepted_manual_round_waiting_for_the_entire_previous_round()
    {
        ScanBoundaryQueue? boundary = null;
        using var shop = new ShopFixture(services => services.AddSingleton<IGameplayQueue>(provider =>
            boundary = new ScanBoundaryQueue(new GameplayQueue(provider.GetRequiredService<GameplayShardSelector>()))));
        var refresh = ActivatorUtilities.CreateInstance<Dfo.Server.Protocol.Game.StateSynchronization.SelectedSessionRefreshService>(shop.Services, new Dfo.Server.Protocol.Game.StateSynchronization.SelectedSessionRefreshTopics([Dfo.Server.Abstractions.Sessions.SelectedSessionRefreshTopic.Fatigue]));
        var first = refresh.RefreshAsync(default); var queue = Assert.IsType<ScanBoundaryQueue>(boundary);
        await queue.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = refresh.RefreshAsync(default);
        var drain = ((IGameplayProducerDrain)refresh).DrainAsync(default).AsTask();
        Assert.False(first.IsCompleted); Assert.False(second.IsCompleted); Assert.False(drain.IsCompleted);
        queue.Release.TrySetResult(); await Task.WhenAll(first, second, drain).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(shop.Sender.Messages);
        await Assert.ThrowsAsync<GameplayExecutorStoppedException>(() => refresh.RefreshAsync(default));
    }
    private sealed class ScanBoundaryQueue(IGameplayQueue inner) : IGameplayQueue
    {
        private int held;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<T> RunAsync<T>(GameplayWork<T> work, CancellationToken token)
        {
            var body = work.Body;
            return inner.RunAsync(work with { Body = async ct =>
            {
                var value = await body(ct);
                if (work.CommandType == "timer:SelectedSessionRefreshService/fatigue" && Interlocked.Exchange(ref held, 1) == 0)
                { Entered.TrySetResult(); await Release.Task; }
                return value;
            } }, token);
        }
        public async ValueTask RunAsync(GameplayWork work, CancellationToken token)
        {
            await RunAsync(new GameplayWork<bool>(work.Key, work.Chain, work.CommandType, async ct =>
            { await work.Body(ct); return true; }, work.Source), token);
        }
    }
}
