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
        var refresh = ActivatorUtilities.CreateInstance<FatigueDailyRefreshService>(shop.Services);
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
                if (work.CommandType == "timer:FatigueDailyRefreshService" && Interlocked.Exchange(ref held, 1) == 0)
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
