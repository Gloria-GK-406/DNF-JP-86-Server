using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.HttpApi;

/// <summary>
/// Spec appendix A rows 9 and 10 (RQ-015): the gameplay part of an HTTP request runs as one CMD on the
/// executor, a new root of source <see cref="GameplayCallSource.Gm"/> or <see cref="GameplayCallSource.Http"/>,
/// typed <c>gm:</c>/<c>http:</c> followed by the route's method and template. The request waits off the executor.
/// </summary>
public static class HttpGameplay
{
    /// <summary>Runs <paramref name="body"/> as the request's CMD and returns its result.</summary>
    /// <remarks>
    /// Enqueued with <see cref="CancellationToken.None"/>, like the timer and event producers: a request
    /// abandoned while queued still runs, as its handler used to run at once, and the body sees the
    /// aborted token where the handler saw it. A cancellation raised through that token is carried out
    /// of the job and rethrown here instead of being reported as a failed CMD.
    /// </remarks>
    public static async Task<T> RunAsync<T>(HttpContext context, GameplayCallSource source,
        Func<CancellationToken, ValueTask<T>> body)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(body);
        var queue = context.RequestServices.GetRequiredService<IGameplayQueue>();
        var aborted = context.RequestAborted;
        ExceptionDispatchInfo? cancelled = null;
        var value = await queue.RunAsync(new GameplayWork<T>(GameplayShardKey.World, GameplayCallChain.NewRoot(source),
            CommandTypeOf(source, context.GetEndpoint()), async _ =>
            {
                try
                {
                    return await body(aborted).ConfigureAwait(false);
                }
                catch (OperationCanceledException error) when (aborted.IsCancellationRequested)
                {
                    cancelled = ExceptionDispatchInfo.Capture(error);
                    return default!;
                }
            }), CancellationToken.None).ConfigureAwait(false);
        cancelled?.Throw();
        return value;
    }

    /// <summary>
    /// Runs every route of <paramref name="builder"/> as one CMD of <paramref name="source"/>, except those
    /// marked <see cref="OutsideGameplay"/> or <see cref="EnqueuesItself"/>: a new route is a CMD unless it
    /// says why not (RQ-010, RQ-011). A JSON body up to <paramref name="bodyLimit"/> bytes is read into
    /// memory before the CMD is queued, so the CMD never waits on the network (RQ-003, RQ-008).
    /// </summary>
    public static TBuilder RunsOnGameplay<TBuilder>(this TBuilder builder, GameplayCallSource source, int bodyLimit)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new GameplayCmdRoute(source));
        builder.AddEndpointFilterFactory((_, next) => async invocation =>
        {
            var context = invocation.HttpContext;
            var metadata = context.GetEndpoint()?.Metadata;
            if (metadata?.GetMetadata<OutsideGameplay>() is not null || metadata?.GetMetadata<EnqueuesItself>() is not null)
                return await next(invocation).ConfigureAwait(false);
            await BufferBodyAsync(context, bodyLimit).ConfigureAwait(false);
            return await RunAsync(context, source, _ => next(invocation)).ConfigureAwait(false);
        });
        return builder;
    }

    /// <remarks>
    /// The handler keeps its own checks: a body that is not JSON or declares more than the limit is left
    /// unread for it to refuse. Otherwise, where the request's limit can be lowered, the limit is enforced
    /// while buffering, in memory only. A read
    /// that fails is replayed to the handler as the same exception, so it answers as it always has.
    /// </remarks>
    private static async ValueTask BufferBodyAsync(HttpContext context, int bodyLimit)
    {
        var request = context.Request;
        if (!request.HasJsonContentType() || request.ContentLength > bodyLimit) return;
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
        {
            size.MaxRequestBodySize = bodyLimit;
            request.EnableBuffering(bodyLimit, bodyLimit);
        }
        else
        {
            // F-33: the limit could not be lowered, so the server's own limit applies while reading, as it did when
            // the handler read the body itself; beyond the threshold the buffer spills to a temporary file.
            request.EnableBuffering(bodyLimit);
        }
        try
        {
            await request.Body.DrainAsync(context.RequestAborted).ConfigureAwait(false);
            request.Body.Position = 0;
        }
        catch (Exception error)
        {
            request.Body = new FailedBody(ExceptionDispatchInfo.Capture(error));
        }
    }

    /// <summary>One CMD type per route: <c>gm:POST /api/gm/v1/mail/send</c>.</summary>
    public static string CommandTypeOf(GameplayCallSource source, Endpoint? endpoint)
    {
        var prefix = source switch
        {
            GameplayCallSource.Gm => "gm",
            GameplayCallSource.Http => "http",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "HTTP CMDs come from GM or the launcher."),
        };
        if (endpoint is not RouteEndpoint route
            || route.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is not [var method])
            throw new InvalidOperationException($"An HTTP CMD needs a route with one method: {endpoint?.DisplayName}.");
        return $"{prefix}:{method} {route.RoutePattern.RawText}";
    }

    private sealed class FailedBody(ExceptionDispatchInfo failure) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Fail<int>();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Fail<int>());
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromResult(Fail<int>());
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private T Fail<T>()
        {
            failure.Throw();
            return default!;
        }
    }
}

/// <summary>A route that stays off the executor; the reason names its spec appendix B row (RQ-011).</summary>
public sealed record OutsideGameplay(string Reason);

/// <summary>A route whose handler enqueues its own CMD because part of it must run off the executor.</summary>
public sealed record EnqueuesItself(GameplayCallSource Source);

/// <summary>A route that <see cref="HttpGameplay.RunsOnGameplay{TBuilder}"/> runs as one CMD.</summary>
public sealed record GameplayCmdRoute(GameplayCallSource Source);
