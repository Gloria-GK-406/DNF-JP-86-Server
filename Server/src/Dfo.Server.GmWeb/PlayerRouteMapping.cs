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
    internal static IResult Error(int status, string code, string message, string? outcome = null) => outcome is null
        ? Results.Json(new { message, code }, statusCode: status) : Results.Json(new { message, code, outcome }, statusCode: status);

    /// <summary>
    /// A player route off the executor (the mail catalog, icons, tooltips): its refusals answer with the same codes as
    /// the CMD routes. Cancellation is the request's own and is not answered.
    /// </summary>
    internal static async Task<IResult> OutsideAsync(HttpContext context, Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (PlayerPortalException refusal) { return PlayerRouteReplyMapper.Refusal(refusal); }
        catch (HttpRouteInputException parsed) { return Error(parsed.StatusCode, "invalid", parsed.Message); }
        catch (KeyNotFoundException) { return Error(404, "not_found", "未找到该物品。"); }
        catch (ArgumentException error) { return Error(400, "invalid", error.Message); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Dfo.Server.GmWeb.PlayerPortal")
                .LogError(error, "Player catalog request {Path} failed.", context.Request.Path.Value);
            return Error(500, "unknown", "物品资料读取失败，请稍后重试。");
        }
    }

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

    /// <summary>The player's writes: their failures also say whether anything was committed (SD-1a <c>outcome</c>).</summary>
    private static readonly HashSet<HttpRouteOperation> Writes = [HttpRouteOperation.PlayerGrantCera, HttpRouteOperation.PlayerSendMail];

    public ValueTask<IResult> MapAsync(HttpContext context, HttpRouteOperation operation, HttpRouteReply reply)
    {
        // The grant committed; only its reminder failed. It answers as the success it is (TASK-04 decision 8.6).
        if (reply is { Outcome: "reminderFailed", Value: PlayerCeraGrantResult reminded })
        {
            Log(context, reply.Error, operation, reply.Outcome);
            return ValueTask.FromResult(Results.Ok(new { reminded.Receipt, reminded.Replayed, reminder = "failed" }));
        }
        if (reply.Error is { } error)
        {
            if (Writes.Contains(operation) && reply.Outcome is null)
                reply = reply with { Outcome = Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(error) ? "unknown" : "rejected" };
            return ValueTask.FromResult(Failure(context, operation, reply, error));
        }
        if (reply.Value is PlayerCeraGrantResult granted)
            return ValueTask.FromResult(Results.Ok(new { granted.Receipt, granted.Replayed, reminder = reply.Reminder }));
        // The new-mail notice follows the commit as the mailbox's own event, so there is no reminder to report.
        if (reply.Value is PlayerMailSendResult sent)
            return ValueTask.FromResult(Results.Ok(new { sent.Receipt, sent.Replayed }));
        if (operation == HttpRouteOperation.PlayerLogout)
        {
            context.Response.Cookies.Delete(PlayerRouteMapping.SessionCookie, PlayerRouteMapping.Cookie(context));
            return ValueTask.FromResult(Results.NoContent());
        }
        return ValueTask.FromResult(Results.Ok(reply.Value));
    }

    private static IResult Failure(HttpContext context, HttpRouteOperation operation, HttpRouteReply reply, Exception error)
    {
        // A write's outcome travels with its failure; "auditFailed" means the result is unconfirmed to the player.
        var outcome = !Writes.Contains(operation) ? null : reply.Outcome == "auditFailed" ? "unknown" : reply.Outcome;
        if (!reply.ExecutorFailed && reply.Outcome is not ("unknown" or "auditFailed"))
        {
            switch (error)
            {
                case PlayerPortalException refusal: return Refusal(refusal, outcome);
                case HttpRouteInputException parsed: return PlayerRouteMapping.Error(parsed.StatusCode, "invalid", parsed.Message, outcome);
                case ArgumentException or JsonException or BadHttpRequestException:
                    return PlayerRouteMapping.Error(400, "invalid", error.Message, outcome);
            }
        }
        Log(context, error, operation, reply.Outcome);
        return PlayerRouteMapping.Error(500, "unknown", reply.ExecutorFailed && reply.Outcome == "rolledBack"
            ? "未提交，请稍后用原编号重试。" : "结果暂未确认，请稍后重试。", outcome);
    }

    private static void Log(HttpContext context, Exception? error, HttpRouteOperation operation, string? outcome) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Dfo.Server.GmWeb.PlayerPortal")
            .LogError(error, "Player operation {Operation} failed with outcome {Outcome}.", operation, outcome);

    internal static IResult Refusal(PlayerPortalException refusal, string? outcome = null) => refusal.Reason switch
    {
        PlayerPortalRefusal.PortalClosed => PlayerRouteMapping.Error(403, "portal_closed", refusal.Message, outcome),
        PlayerPortalRefusal.FeatureDisabled => PlayerRouteMapping.Error(403, "feature_disabled", refusal.Message, outcome),
        PlayerPortalRefusal.Unauthenticated => PlayerRouteMapping.Error(401, "unauthenticated", refusal.Message, outcome),
        PlayerPortalRefusal.NotFound => PlayerRouteMapping.Error(404, "not_found", refusal.Message, outcome),
        PlayerPortalRefusal.LimitExceeded => PlayerRouteMapping.Error(409, "limit_exceeded", refusal.Message, outcome),
        PlayerPortalRefusal.RequestConflict => PlayerRouteMapping.Error(409, "request_conflict", refusal.Message, outcome),
        _ => PlayerRouteMapping.Error(400, "invalid", refusal.Message, outcome),
    };
}
