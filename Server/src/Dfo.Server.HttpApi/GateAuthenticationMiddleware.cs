using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Dfo.Server.HttpApi;

/// <summary>
/// Bearer token gate for endpoints carrying <see cref="GateEndpointMetadata"/>. It runs after the
/// listener boundary, so a gate path reached on another listener has already been answered 404.
/// </summary>
internal sealed class GateAuthenticationMiddleware(
    RequestDelegate next,
    GateOptions options,
    GateAuthFailureLimiter limiter,
    ILogger<GateAuthenticationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<GateEndpointMetadata>() is null)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (!options.TokenConfigured)
        {
            await ApiResponses.WriteErrorAsync(
                context,
                StatusCodes.Status404NotFound,
                "not_found",
                "接口不存在。").ConfigureAwait(false);
            return;
        }

        // Compared before the limiter is consulted: a valid token passes even from a limited source.
        if (options.Matches(PresentedToken(context.Request.Headers.Authorization)))
        {
            context.Features.Set(GateClientAddress.Resolve(context));
            await next(context).ConfigureAwait(false);
            return;
        }

        var source = context.Connection.RemoteIpAddress;
        var limited = limiter.IsLimited(source);
        limiter.RecordFailure(source);
        var status = limited ? StatusCodes.Status429TooManyRequests : StatusCodes.Status401Unauthorized;
        // Neither the Authorization nor the X-Forwarded-For header is logged: one may hold a guess
        // of the token, the other is untrusted on a refused request. 401s are capped per source and
        // window; 429s are not, so they stay at Debug to keep a flood out of the default log.
        logger.Log(
            limited ? LogLevel.Debug : LogLevel.Warning,
            "Gate authentication refused with {StatusCode} from {RemoteAddress}.",
            status,
            source?.ToString() ?? "unknown");
        if (limited)
        {
            await ApiResponses.WriteErrorAsync(
                context,
                status,
                "too_many_requests",
                "授权失败次数过多，请稍后重试。").ConfigureAwait(false);
            return;
        }

        context.Response.Headers.WWWAuthenticate = "Bearer";
        await ApiResponses.WriteErrorAsync(
            context,
            status,
            "unauthorized",
            "网关授权无效。").ConfigureAwait(false);
    }

    /// <summary>
    /// Exactly one <c>Bearer</c> value (scheme case-insensitive, then one or more spaces); the token
    /// part is taken verbatim. Anything else yields an empty token, which never matches.
    /// </summary>
    private static ReadOnlySpan<char> PresentedToken(StringValues authorization)
    {
        if (authorization.Count != 1 || authorization[0] is not { } header) return default;
        var value = header.AsSpan();
        var space = value.IndexOf(' ');
        if (space <= 0 || !value[..space].Equals("Bearer", StringComparison.OrdinalIgnoreCase)) return default;
        return value[(space + 1)..].TrimStart(' ');
    }
}

internal sealed class GateEndpointMetadata
{
    public static GateEndpointMetadata Instance { get; } = new();

    private GateEndpointMetadata() { }
}

/// <summary>
/// Client address of an authenticated gate request: the last <c>X-Forwarded-For</c> hop, appended by
/// the gateway that holds the token, else the connection address. Set only after the token matched.
/// </summary>
internal sealed record GateClientAddress(IPAddress Address)
{
    internal static GateClientAddress Resolve(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"];
        if (forwarded.Count > 0 && forwarded[forwarded.Count - 1] is { } last)
        {
            var hop = last.AsSpan();
            hop = hop[(hop.LastIndexOf(',') + 1)..].Trim();
            if (IPAddress.TryParse(hop, out var parsed)) return new GateClientAddress(parsed);
        }

        return new GateClientAddress(context.Connection.RemoteIpAddress ?? IPAddress.None);
    }
}
