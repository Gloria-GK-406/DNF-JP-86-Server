using System.Text.Json;
using Dfo.Server.Application.HttpOperations;
using Dfo.Server.Data.PlayerPortal;
using Dfo.Server.HttpApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.GmWeb;

/// <summary>A player route the boundary opens only to a player session; a player route without it is refused.</summary>
internal sealed class PlayerSessionRequired;

internal static class PlayerRouteMapping
{
    internal const string SessionCookie = "Dfo.Player.Session";
    internal const string CookiePath = "/api/player";
    internal const string IdentityKey = "Dfo.Player.Identity";

    internal static RouteHandlerBuilder Map(RouteGroupBuilder group, string method, string path, HttpRouteOperation operation, Delegate parser) =>
        group.MapMethods(path, [method], parser).WithMetadata(new PlayerSessionRequired(), new HttpGameplayOperation(operation, PlayerRouteReplyMapper.Instance));

    internal static string? Secret(HttpContext context) => context.Request.Cookies[SessionCookie];

    /// <summary>Every refusal of the player API: <c>{ message, code }</c>; the front end branches on <c>code</c> only (SD-1a).</summary>
    internal static Task Write(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { message, code }, context.RequestAborted);
    }
    internal static IResult Error(int status, string code, string message) => Results.Json(new { message, code }, statusCode: status);

    internal static CookieOptions Cookie(HttpContext context, DateTimeOffset? expires = null) =>
        new() { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps, Path = CookiePath, Expires = expires, IsEssential = true };
}

/// <summary>
/// The player API's own reply mapping (SD-1a): it never goes through <see cref="GmRouteReplyMapper"/> and its operation
/// ranges, and it answers every refusal with a <c>code</c>.
/// </summary>
internal sealed class PlayerRouteReplyMapper : IHttpRouteReplyMapper
{
    internal static PlayerRouteReplyMapper Instance { get; } = new();

    public ValueTask<IResult> MapAsync(HttpContext context, HttpRouteOperation operation, HttpRouteReply reply)
    {
        if (reply.Error is { } error) return ValueTask.FromResult(Failure(context, operation, reply, error));
        if (operation == HttpRouteOperation.PlayerLogout)
        {
            context.Response.Cookies.Delete(PlayerRouteMapping.SessionCookie, PlayerRouteMapping.Cookie(context));
            return ValueTask.FromResult(Results.NoContent());
        }
        return ValueTask.FromResult(Results.Ok(reply.Value));
    }

    private static IResult Failure(HttpContext context, HttpRouteOperation operation, HttpRouteReply reply, Exception error)
    {
        if (!reply.ExecutorFailed && reply.Outcome is not ("unknown" or "auditFailed"))
        {
            switch (error)
            {
                case PlayerPortalException refusal: return Refusal(refusal);
                case HttpRouteInputException parsed: return PlayerRouteMapping.Error(parsed.StatusCode, "invalid", parsed.Message);
                case ArgumentException or JsonException or BadHttpRequestException:
                    return PlayerRouteMapping.Error(400, "invalid", error.Message);
            }
        }
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Dfo.Server.GmWeb.PlayerPortal")
            .LogError(error, "Player operation {Operation} failed with outcome {Outcome}.", operation, reply.Outcome);
        return PlayerRouteMapping.Error(500, "unknown", "结果暂未确认，请稍后重试。");
    }

    private static IResult Refusal(PlayerPortalException refusal) => refusal.Reason switch
    {
        PlayerPortalRefusal.PortalClosed => PlayerRouteMapping.Error(403, "portal_closed", refusal.Message),
        PlayerPortalRefusal.FeatureDisabled => PlayerRouteMapping.Error(403, "feature_disabled", refusal.Message),
        PlayerPortalRefusal.Unauthenticated => PlayerRouteMapping.Error(401, "unauthenticated", refusal.Message),
        PlayerPortalRefusal.NotFound => PlayerRouteMapping.Error(404, "not_found", refusal.Message),
        PlayerPortalRefusal.LimitExceeded => PlayerRouteMapping.Error(409, "limit_exceeded", refusal.Message),
        PlayerPortalRefusal.RequestConflict => PlayerRouteMapping.Error(409, "request_conflict", refusal.Message),
        _ => PlayerRouteMapping.Error(400, "invalid", refusal.Message),
    };
}
