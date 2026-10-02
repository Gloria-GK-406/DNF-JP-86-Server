using System.Net;
using System.Security.Claims;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Data.Administration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace Dfo.Server.GmWeb;

public sealed record GmRequiredPermission(GmPermission Permission);
internal sealed class GmPublicEndpoint;

internal sealed class GmAccessBoundary(RequestDelegate next)
{
    internal const string SessionCookie = "Dfo.Gm.Session";
    internal const string IdentityKey = "Dfo.Gm.Identity";

    public async Task InvokeAsync(HttpContext context, IGmAccessDomainService access, IAntiforgery antiforgery, GmWebOptions options)
    {
        if (!context.Request.Path.StartsWithSegments("/api/gm/v1")) { await next(context); return; }
        if (context.Connection.LocalPort != options.ListenUrl!.Port) { context.Response.StatusCode = 404; return; }
        context.Response.Headers.CacheControl = "no-store";
        var endpoint = context.GetEndpoint();
        var isPublic = endpoint?.Metadata.GetMetadata<GmPublicEndpoint>() is not null;
        if (!isPublic && !SecureTransport(context)) { await Reject(context, 403, "远程管理必须使用 HTTPS。"); return; }
        if (isPublic && context.Request.Path != "/api/gm/v1/health" && context.Request.Path != "/api/gm/v1/auth/status"
            && !SecureTransport(context)) { await Reject(context, 403, "远程管理必须使用 HTTPS。"); return; }
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
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            var origin = context.Request.Headers.Origin.ToString();
            if ((origin.Length != 0 && origin != $"{context.Request.Scheme}://{context.Request.Host}")
                || context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
            { await Reject(context, 403, "跨站请求被拒绝。"); return; }
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { await Reject(context, 403, "请求验证失败，请刷新页面。"); return; }
        }
        await next(context);
    }

    internal static bool SecureTransport(HttpContext context) => context.Request.IsHttps
        || (context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address));
    internal static Task Reject(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { message }, context.RequestAborted);
    }
}
