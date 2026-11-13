using System.Net;
using System.Security.Claims;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.PlayerPortal;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.PlayerPortal;
using Dfo.Server.HttpApi;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace Dfo.Server.GmWeb;

public sealed record GmRequiredPermission(GmPermission Permission);
internal sealed class GmPublicEndpoint;

/// <summary>
/// The back office's one boundary for both prefixes (SD-2): listener, HTTPS, same-origin and antiforgery checks are
/// this one piece of code for the GM and the player API alike (RQ-006). Only the identity is per prefix: the GM
/// prefix knows only the GM session cookie, the player prefix only the player session cookie (RQ-002).
/// </summary>
internal sealed class GmAccessBoundary(RequestDelegate next)
{
    internal const string SessionCookie = "Dfo.Gm.Session";
    internal const string IdentityKey = "Dfo.Gm.Identity";
    internal const string PlayerPrefix = "/api/player/v1";
    internal const string PlayerStatusPath = PlayerPrefix + "/auth/status";

    public async Task InvokeAsync(HttpContext context, IGmAccessDomainService access, IPlayerPortalAccessDomainService players,
        IAntiforgery antiforgery, GmWebOptions options, PlayerRequestLimiter limiter)
    {
        var player = context.Request.Path.StartsWithSegments(PlayerPrefix);
        if (!player && !context.Request.Path.StartsWithSegments("/api/gm/v1")) { await next(context); return; }
        if (context.Connection.LocalPort != options.ListenUrl!.Port) { context.Response.StatusCode = 404; return; }
        context.Response.Headers.CacheControl = "no-store";
        var endpoint = context.GetEndpoint();
        var isPublic = endpoint?.Metadata.GetMetadata<GmPublicEndpoint>() is not null;
        var path = context.Request.Path;
        PlayerIdentity? playerIdentity = null;
        var transportExempt = isPublic && (player ? path == PlayerStatusPath : path == "/api/gm/v1/health" || path == "/api/gm/v1/auth/status");
        if (!transportExempt && !SecureTransport(context))
        { await Refuse(context, player, 403, "forbidden", player ? "远程访问必须使用 HTTPS。" : "远程管理必须使用 HTTPS。"); return; }
        if (player)
        {
            // The master switch is judged before the session, so a closed portal never reads as a lost login (RQ-021).
            // The status route alone answers while closed: it is how the login page learns that the portal is closed.
            if (path != PlayerStatusPath && !await players.IsOpenAsync(context.RequestAborted))
            { await Refuse(context, player, 403, "portal_closed", "玩家后台已关闭。"); return; }
            var identity = playerIdentity = await players.IdentifyAsync(context.Request.Cookies[PlayerRouteMapping.SessionCookie], context.RequestAborted);
            if (identity is not null)
            {
                context.Items[PlayerRouteMapping.IdentityKey] = identity;
                // Prefixed so that no player's antiforgery binding can equal an administrator's numeric ID.
                context.User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "player:" + identity.AccountId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new Claim(ClaimTypes.Name, identity.LoginName)], "PlayerSession"));
            }
            if (!isPublic)
            {
                if (identity is null) { await Refuse(context, player, 401, "unauthenticated", "请先登录。"); return; }
                if (endpoint?.Metadata.GetMetadata<PlayerSessionRequired>() is null)
                { await Refuse(context, player, 403, "forbidden", "没有执行此操作的权限。"); return; }
            }
        }
        else
        {
            var identity = await access.IdentifyAsync(context.Request.Cookies[SessionCookie], context.RequestAborted);
            if (identity is not null)
            {
                context.Items[IdentityKey] = identity;
                context.User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, identity.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new Claim(ClaimTypes.Name, identity.LoginName)], "GmSession"));
            }
            if (!isPublic)
            {
                if (identity is null) { await Reject(context, 401, "请先登录管理后台。"); return; }
                var requirement = endpoint?.Metadata.GetMetadata<GmRequiredPermission>();
                if (requirement is null || (identity.Permissions & requirement.Permission) != requirement.Permission)
                { await Reject(context, 403, "没有执行此操作的权限。"); return; }
            }
        }
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            var origin = context.Request.Headers.Origin.ToString();
            if ((origin.Length != 0 && origin != $"{context.Request.Scheme}://{context.Request.Host}")
                || context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
            { await Refuse(context, player, 403, "forbidden", "跨站请求被拒绝。"); return; }
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { await Refuse(context, player, 403, "forbidden", "请求验证失败，请刷新页面。"); return; }
        }
        // A player's executor CMDs are capped per account before they are queued (CH-1); the refused one never runs.
        if (player && !isPublic && playerIdentity is { } held && endpoint?.Metadata.GetMetadata<HttpGameplayOperation>() is not null)
        {
            if (!limiter.TryAcquire(held.AccountId))
            { await Refuse(context, player, 429, "rate_limited", "请求过于频繁，请稍后重试。"); return; }
            try { await next(context); }
            finally { limiter.Release(held.AccountId); }
            return;
        }
        await next(context);
    }

    // With GmWeb:TrustedProxies these are the forwarded scheme and client address (GmWebModule).
    internal static bool SecureTransport(HttpContext context) => context.Request.IsHttps
        || (context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address));
    internal static Task Reject(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { message }, context.RequestAborted);
    }
    /// <summary>The GM API answers <c>{ message }</c>; the player API also names the refusal's <c>code</c> (SD-1a).</summary>
    private static Task Refuse(HttpContext context, bool player, int status, string code, string message) =>
        player ? PlayerRouteMapping.Write(context, status, code, message) : Reject(context, status, message);
}
