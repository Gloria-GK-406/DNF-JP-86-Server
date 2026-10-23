using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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

    /// <summary>
    /// F-30 (TASK-31 section 2.2): a CMD that fails inside its job reaches the middleware as the job's own failure.
    /// The executor's Error already carries the exception and the chain; the middleware's line is Debug.
    /// </summary>
    [Fact]
    public async Task A_gm_cmd_that_fails_is_logged_once_by_the_executor_and_the_middleware_only_at_debug()
    {
        var logs = new EntryCapture();
        await using var services = new ServiceCollection()
            .AddLogging(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Trace))
            .AddGameplayExecution()
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(Probe());
        var middleware = new HttpApiExceptionMiddleware(
            async request => await HttpGameplay.RunAsync<int>(request, GameplayCallSource.Gm,
                _ => throw new InvalidOperationException("the gm cmd failed")),
            services.GetRequiredService<ILogger<HttpApiExceptionMiddleware>>());

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        var error = Assert.Single(logs.Entries, x => x.Level >= LogLevel.Error);
        Assert.StartsWith("Gameplay gm:POST /api/gm/v1/probe failed", error.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(error.Exception);
        var request = Assert.Single(logs.Entries, x => x.Message.StartsWith("HTTP API request failed", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Debug, request.Level);
    }

    /// <summary>
    /// F-33 (TASK-31 section 6): when the request's size limit cannot be lowered to the route's, the server's own
    /// limit applies, as it did when the handler read the body itself; buffering must not cut the body short.
    /// </summary>
    [Fact]
    public async Task A_body_over_the_limit_is_read_whole_when_the_size_feature_is_read_only()
    {
        var body = JsonBody(40 * 1024);
        var (read, position) = await ReadThroughTheFilterAsync(body, new FixedSize(isReadOnly: true, limit: null));

        Assert.Equal(body, read);
        Assert.Equal(0, position);
    }

    [Fact]
    public async Task A_body_over_the_limit_is_read_whole_when_there_is_no_size_feature()
    {
        var body = JsonBody(40 * 1024);
        var (read, position) = await ReadThroughTheFilterAsync(body, size: null);

        Assert.Equal(body, read);
        Assert.Equal(0, position);
    }

    private static RouteEndpoint Probe() =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse("/api/gm/v1/probe"), 0,
            new EndpointMetadataCollection(new HttpMethodMetadata(["POST"])), "probe");

    private static byte[] JsonBody(int length) =>
        System.Text.Encoding.UTF8.GetBytes("{\"text\":\"" + new string('x', length) + "\"}");

    /// <summary>Runs a chunked JSON request through the filter that <see cref="HttpGameplay.RunsOnGameplay{TBuilder}"/> adds.</summary>
    private static async Task<(byte[] Read, long Position)> ReadThroughTheFilterAsync(byte[] body, IHttpMaxRequestBodySizeFeature? size)
    {
        await using var services = new ServiceCollection().AddLogging().AddGameplayExecution().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.Body = new ForwardOnly(body);
        context.Features.Set(size);
        context.SetEndpoint(Probe());
        var conventions = new Conventions();
        conventions.RunsOnGameplay(GameplayCallSource.Gm, bodyLimit: 32768);
        var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/api/gm/v1/probe"), 0);
        foreach (var convention in conventions.Added) convention(endpoint);
        var factory = Assert.Single(endpoint.FilterFactories);
        var filter = factory(new EndpointFilterFactoryContext
        {
            MethodInfo = typeof(HttpGameplayTests).GetMethod(nameof(Probe),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!,
            ApplicationServices = services,
        }, async invocation =>
        {
            var position = invocation.HttpContext.Request.Body.CanSeek ? invocation.HttpContext.Request.Body.Position : -1;
            using var copy = new MemoryStream();
            await invocation.HttpContext.Request.Body.CopyToAsync(copy);
            return (copy.ToArray(), position);
        });

        var result = await filter(new DefaultEndpointFilterInvocationContext(context));
        return ((byte[], long))result!;
    }

    private sealed class Conventions : IEndpointConventionBuilder
    {
        public List<Action<EndpointBuilder>> Added { get; } = [];

        public void Add(Action<EndpointBuilder> convention) => Added.Add(convention);
    }

    private sealed class FixedSize(bool isReadOnly, long? limit) : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => isReadOnly;

        public long? MaxRequestBodySize
        {
            get => limit;
            set => throw new InvalidOperationException("The size limit is read-only.");
        }
    }

    /// <summary>A request body as a socket gives it: no length, no seeking.</summary>
    private sealed class ForwardOnly(byte[] content) : Stream
    {
        private readonly MemoryStream inner = new(content);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, 4096));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class EntryCapture : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Sink(Entries);
        public void Dispose() { }

        private sealed class Sink(ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => entries.Enqueue((logLevel, formatter(state, exception), exception));
        }
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
