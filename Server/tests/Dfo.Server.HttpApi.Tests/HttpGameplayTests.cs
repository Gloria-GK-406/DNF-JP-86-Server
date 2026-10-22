using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.HttpApi.Tests;

public sealed class HttpGameplayTests
{
    /// <summary>
    /// A request abandoned while its CMD waits in the queue still runs, as the handler used to run at
    /// once; the body sees the cancelled token where the handler would have seen it.
    /// </summary>
    [Fact]
    public async Task A_request_abandoned_while_queued_still_runs_and_sees_its_cancelled_token()
    {
        var logs = new LevelCapture();
        await using var services = new ServiceCollection()
            .AddLogging(builder => builder.AddProvider(logs))
            .AddGameplayExecution()
            .BuildServiceProvider();
        var queue = services.GetRequiredService<IGameplayQueue>();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holding = queue.RunAsync(new GameplayWork(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection), "test:hold", _ => new ValueTask(hold.Task)), default);
        using var aborted = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestServices = services, RequestAborted = aborted.Token };
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("/api/gm/v1/probe"), 0,
            new EndpointMetadataCollection(new HttpMethodMetadata(["POST"])), "probe"));
        bool? sawCancelled = null;
        GameplayCallChain? chain = null;

        var request = HttpGameplay.RunAsync(context, GameplayCallSource.Gm, token =>
        {
            sawCancelled = token.IsCancellationRequested;
            chain = GameplayJob.Current;
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(1);
        });
        aborted.Cancel();
        await Task.Delay(100);
        Assert.False(request.IsCompleted);
        Assert.Null(sawCancelled);
        hold.SetResult();
        await holding;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(sawCancelled);
        Assert.Equal(GameplayCallSource.Gm, chain!.Value.Source);
        Assert.Equal("gm:POST /api/gm/v1/probe", chain.Value.CommandType);
        Assert.DoesNotContain(LogLevel.Error, logs.Levels);
    }

    private sealed class LevelCapture : ILoggerProvider
    {
        public ConcurrentQueue<LogLevel> Levels { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Sink(Levels);
        public void Dispose() { }

        private sealed class Sink(ConcurrentQueue<LogLevel> levels) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => levels.Enqueue(logLevel);
        }
    }
}
