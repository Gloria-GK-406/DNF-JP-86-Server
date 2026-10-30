using System.Text.Json;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;
using Dfo.Server.HttpApi;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;

namespace Dfo.Server.GmWeb;

internal static class GmAuthenticationEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/api/gm/v1/auth/status", async (IGmAccessDomainService access, CancellationToken token) =>
            Results.Ok(new { initialized = await access.IsInitializedAsync(token) }))
            .WithMetadata(new GmPublicEndpoint(), new OutsideGameplay("只读 SQLite 的查询：是否已有管理员"));
        group.MapGet("/api/gm/v1/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        }).WithMetadata(new GmPublicEndpoint(), new OutsideGameplay("不碰玩法状态的 HTTP 路由：防伪令牌"));
        // Verification runs off the executor first (RQ-007), so the login enqueues its CMD itself.
        group.MapPost("/api/gm/v1/auth/login", LoginAsync).WithMetadata(new GmPublicEndpoint(), new EnqueuesItself(GameplayCallSource.Gm, HttpRouteOperation.GmLogin));
        group.MapGet("/api/gm/v1/auth/session", (HttpContext context) => Results.Ok(IdentityResponse((GmIdentity)context.Items[GmAccessBoundary.IdentityKey]!)))
            .WithMetadata(new GmRequiredPermission(GmPermission.None), new OutsideGameplay("不碰玩法状态的 HTTP 路由：边界已识别的身份"));
        GmRouteMapping.Map(group, "POST", "/api/gm/v1/auth/logout", HttpRouteOperation.Logout, (HttpContext c) => new LogoutGmAdministratorCommand(c.Request.Cookies[GmAccessBoundary.SessionCookie]!), new GmRequiredPermission(GmPermission.None));
        GmRouteMapping.Map(group, "GET", "/api/gm/v1/audit", HttpRouteOperation.Audit, QueryAudit, new GmRequiredPermission(GmPermission.ViewAudit));
    }

    private static async Task<IResult> LoginAsync(HttpContext context, ICommandBus commands, IGmCredentialReader credentials, GmLoginLimiter limiter)
    {
        if (!context.Request.HasJsonContentType() || context.Request.ContentLength is > 8192) return Error(400, "请求字段无效。");
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit) bodyLimit.MaxRequestBodySize = 8192;
        Credentials? request;
        try { request = await context.Request.ReadFromJsonAsync<Credentials>(cancellationToken: context.RequestAborted); }
        catch (Exception exception) when (exception is JsonException or BadHttpRequestException or NotSupportedException) { return Error(400, "请求字段无效。"); }
        if (request is not { LoginName.Length: > 0 and <= 64, Password.Length: > 0 and <= 256 }) return Error(400, "请求字段无效。");
        if (!limiter.TryBegin(context.Connection.RemoteIpAddress!.ToString(), request.LoginName, out var complete))
        { context.Response.Headers.RetryAfter = "900"; return Error(429, "尝试次数过多，请稍后重试。"); }
        var accepted = false;
        try
        {
            var check = await credentials.CheckLoginAsync(request.LoginName, request.Password, context.RequestAborted);
            var reply = await HttpGameplay.RunAsync(context, GameplayCallSource.Gm,
                new LoginGmAdministratorCommand(check));
            if (reply.Error is { } loginFailure) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(loginFailure);
            var result = (GmLoginResult)reply.Value!;
            accepted = result.Identity is not null;
            if (!accepted) return Error(401, "账号或密码错误。");
            context.Response.Cookies.Append(GmAccessBoundary.SessionCookie, result.SessionSecret!, Cookie(context, result.Identity!.ExpiresAt));
            return Results.Ok(IdentityResponse(result.Identity));
        }
        finally { complete(accepted); }
    }
    private static QueryGmAuditCommand QueryAudit(HttpContext context)
    {
        var query = context.Request.Query; var page = 1; var pageSize = 20;
        foreach (var name in new[] { "page", "pageSize", "outcome", "action" })
            if (query.TryGetValue(name, out var values) && values.Count != 1) throw new HttpRouteInputException(400, "查询参数重复。");
        if ((query.TryGetValue("page", out var p) && !int.TryParse(p, out page))
            || (query.TryGetValue("pageSize", out var s) && !int.TryParse(s, out pageSize))
            || page is < 1 or > 1000000 || pageSize is < 1 or > 100) throw new HttpRouteInputException(400, "分页参数无效。");
        string? Text(string name) => string.IsNullOrWhiteSpace(query[name]) ? null : query[name].ToString().Trim();
        var filter = new GmAuditFilter(Text("action"), Text("outcome"));
        if (filter.Action is { } action && !GmAuditFilter.Actions.Contains(action)) throw new HttpRouteInputException(400, "操作类型筛选无效。");
        if (filter.Outcome is { } outcome && !GmAuditFilter.Outcomes.Contains(outcome)) throw new HttpRouteInputException(400, "结果筛选无效。");
        return new QueryGmAuditCommand(page, pageSize, filter);
    }
    internal static IResult LogoutResponse(HttpContext context, bool accepted)
    {
        if (!accepted) return Error(401, "会话已失效。");
        context.Response.Cookies.Delete(GmAccessBoundary.SessionCookie, Cookie(context));
        return Results.NoContent();
    }
    private static object IdentityResponse(GmIdentity identity) => new { identity.Id, identity.LoginName,
        permissions = Enum.GetValues<GmPermission>().Where(p => p != GmPermission.None && p != GmPermission.All && identity.Permissions.HasFlag(p)).Select(p => p.ToString()), identity.ExpiresAt };
    private static CookieOptions Cookie(HttpContext context, DateTimeOffset? expires = null) =>
        new() { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps, Path = "/", Expires = expires, IsEssential = true };
    private static IResult Error(int code, string message) => Results.Json(new { message }, statusCode: code);
    private sealed record Credentials(string? LoginName, string? Password);
}
