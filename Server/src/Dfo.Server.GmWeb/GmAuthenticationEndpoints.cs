using System.Text.Json;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Application.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;
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
            Results.Ok(new { initialized = await access.IsInitializedAsync(token) })).WithMetadata(new GmPublicEndpoint());
        group.MapGet("/api/gm/v1/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        }).WithMetadata(new GmPublicEndpoint());
        group.MapPost("/api/gm/v1/auth/login", LoginAsync).WithMetadata(new GmPublicEndpoint());
        group.MapGet("/api/gm/v1/auth/session", (HttpContext context) => Results.Ok(IdentityResponse((GmIdentity)context.Items[GmAccessBoundary.IdentityKey]!)))
            .WithMetadata(new GmRequiredPermission(GmPermission.None));
        group.MapPost("/api/gm/v1/auth/logout", LogoutAsync).WithMetadata(new GmRequiredPermission(GmPermission.None));
        group.MapGet("/api/gm/v1/audit", QueryAuditAsync).WithMetadata(new GmRequiredPermission(GmPermission.ViewAudit));
    }

    private static async Task<IResult> LoginAsync(HttpContext context, ICommandBus commands, GmLoginLimiter limiter)
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
            var result = await commands.SendAsync(new LoginGmAdministratorCommand(request.LoginName, request.Password), context.RequestAborted);
            accepted = result.Identity is not null;
            if (!accepted) return Error(401, "账号或密码错误。");
            context.Response.Cookies.Append(GmAccessBoundary.SessionCookie, result.SessionSecret!, Cookie(context, result.Identity!.ExpiresAt));
            return Results.Ok(IdentityResponse(result.Identity));
        }
        finally { complete(accepted); }
    }
    private static async Task<IResult> LogoutAsync(HttpContext context, ICommandBus commands)
    {
        if (!await commands.SendAsync(new LogoutGmAdministratorCommand(context.Request.Cookies[GmAccessBoundary.SessionCookie]!), context.RequestAborted))
            return Error(401, "会话已失效。");
        context.Response.Cookies.Delete(GmAccessBoundary.SessionCookie, Cookie(context));
        return Results.NoContent();
    }
    private static async Task<IResult> QueryAuditAsync(HttpContext context, ICommandBus commands)
    {
        var page = 1; var pageSize = 20;
        if ((context.Request.Query.TryGetValue("page", out var p) && !int.TryParse(p, out page))
            || (context.Request.Query.TryGetValue("pageSize", out var s) && !int.TryParse(s, out pageSize))
            || page is < 1 or > 1000000 || pageSize is < 1 or > 100) return Error(400, "分页参数无效。");
        return Results.Ok(await commands.SendAsync(new QueryGmAuditCommand(page, pageSize), context.RequestAborted));
    }
    private static object IdentityResponse(GmIdentity identity) => new { identity.Id, identity.LoginName,
        permissions = Enum.GetValues<GmPermission>().Where(p => p != GmPermission.None && p != GmPermission.All && identity.Permissions.HasFlag(p)).Select(p => p.ToString()), identity.ExpiresAt };
    private static CookieOptions Cookie(HttpContext context, DateTimeOffset? expires = null) =>
        new() { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps, Path = "/", Expires = expires, IsEssential = true };
    private static IResult Error(int code, string message) => Results.Json(new { message }, statusCode: code);
    private sealed record Credentials(string? LoginName, string? Password);
}
