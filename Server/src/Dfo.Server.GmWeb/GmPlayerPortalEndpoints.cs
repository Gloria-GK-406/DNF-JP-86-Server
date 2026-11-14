using System.Text.Json;
using System.Text.Json.Serialization;
using Dfo.Server.Application.PlayerPortal;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.PlayerPortal;
using Dfo.Server.HttpApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.GmWeb;

/// <summary>The administrator's controls of the player portal (RQ-020, RQ-023); full permission, like the daily refill.</summary>
internal static class GmPlayerPortalEndpoints
{
    private const string Prefix = "/api/gm/v1/player-portal";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    /// <summary>The operations whose refusals <see cref="Failure"/> maps; named, not a range, so later values cannot fall in.</summary>
    internal static IReadOnlySet<HttpRouteOperation> Operations { get; } = new HashSet<HttpRouteOperation>
    {
        HttpRouteOperation.PlayerPortalSettings, HttpRouteOperation.PlayerPortalSettingsChange, HttpRouteOperation.PlayerPortalSessions,
        HttpRouteOperation.PlayerPortalKick, HttpRouteOperation.PlayerPortalKickAll,
    };

    internal static void Map(RouteGroupBuilder group)
    {
        var permission = new GmRequiredPermission(GmPermission.All);
        Map("GET", "/settings", HttpRouteOperation.PlayerPortalSettings, (HttpContext c) => new QueryPlayerPortalSettingsCommand(Secret(c)));
        Map("POST", "/settings", HttpRouteOperation.PlayerPortalSettingsChange, async (HttpContext c) => new ChangePlayerPortalSettingsCommand(Secret(c), await Read<PlayerSettingsChange>(c)));
        Map("GET", "/sessions", HttpRouteOperation.PlayerPortalSessions, (HttpContext c) => new QueryPlayerPortalSessionsCommand(Secret(c)));
        Map("POST", "/sessions/kick", HttpRouteOperation.PlayerPortalKick, async (HttpContext c) => new KickPlayerPortalSessionsCommand(Secret(c),
            (await Read<KickRequest>(c)).AccountId is { } id and > 0 ? id : throw new ArgumentException("账号 ID 必须为正整数。")));
        Map("POST", "/sessions/kick-all", HttpRouteOperation.PlayerPortalKickAll, async (HttpContext c) =>
        {
            await Read<KickAllRequest>(c);
            return new KickPlayerPortalSessionsCommand(Secret(c), null);
        });
        void Map(string method, string path, HttpRouteOperation operation, Delegate parser) => GmRouteMapping.Map(group, method, Prefix + path, operation, parser, permission);
    }

    /// <summary>R-1: a stale revision is 409, not the initialization branch's 500.</summary>
    internal static IResult Failure(HttpContext context, Exception error)
    {
        var status = error switch
        {
            HttpRouteInputException parsed => parsed.StatusCode,
            PlayerPortalException { Reason: PlayerPortalRefusal.SettingsConflict } => 409,
            PlayerPortalException => 400,
            GmMailException { Reason: GmMailRefusal.Unauthorized } => 401,
            GmMailException { Reason: GmMailRefusal.Forbidden } => 403,
            GmMailException => 400,
            ArgumentException or JsonException or BadHttpRequestException => 400,
            _ => 500,
        };
        if (status == 500)
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Dfo.Server.GmWeb.PlayerPortal")
                .LogError(error, "Player portal administration failed.");
        return Results.Json(new { message = status == 500 ? "玩家后台设置保存或读取失败，请重新读取确认当前生效设置。" : error.Message }, statusCode: status);
    }

    private static string Secret(HttpContext c) => c.Request.Cookies[GmAccessBoundary.SessionCookie]!;
    private static async Task<T> Read<T>(HttpContext c)
    {
        if (!c.Request.HasJsonContentType() || c.Request.ContentLength is > 32768) throw new ArgumentException("请求必须为 JSON，且不超过 32 KiB。");
        if (c.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = 32768;
        return await c.Request.ReadFromJsonAsync<T>(Json, c.RequestAborted) ?? throw new ArgumentException("请求字段缺失。");
    }
    private sealed record KickRequest(int? AccountId);
    private sealed record KickAllRequest;
}
