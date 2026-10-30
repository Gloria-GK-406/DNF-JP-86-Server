using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Administration;
using Dfo.Server.HttpApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.GmWeb;

/// <summary>Static item presentation (icons, tooltips) for any signed-in administrator.</summary>
internal static class GmItemEndpoints
{
    private const string Prefix = "/api/gm/v1/items";
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet(Prefix + "/icon-pack", (GmIconPackStore icons) =>
        {
            var info = icons.Describe();
            return Results.Ok(new { available = info.Available, count = info.Count, generatedAt = info.GeneratedAt, pvfSha256 = info.PvfSha256 });
        }).WithMetadata(Anyone, new OutsideGameplay("GM 自身的内存状态：GmIconPackStore"));
        group.MapGet(Prefix + "/{itemId:int}/icon", (HttpContext context, GmIconPackStore icons, int itemId) =>
        {
            if (itemId <= 0 || icons.Icon(itemId) is not { } png) return NotFound("图标不存在。");
            // Icons are keyed by item ID and only change when the pack is rebuilt; overrides the boundary's no-store.
            context.Response.Headers.CacheControl = "private, max-age=86400";
            return Results.Bytes(png, "image/png");
        }).WithMetadata(Anyone, new OutsideGameplay("GM 自身的内存状态：GmIconPackStore"));
        group.MapGet(Prefix + "/{itemId:int}/tooltip", async (HttpContext context, IItemTooltipRuleService tooltips, ILoggerFactory logs, int itemId) =>
        {
            if (itemId <= 0) return NotFound("未找到该物品。");
            try { return await tooltips.FindAsync(itemId, context.RequestAborted) is { } tooltip ? Results.Ok(tooltip) : NotFound("未找到该物品。"); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logs.CreateLogger("Dfo.Server.GmWeb.Items").LogError(error, "Item tooltip {ItemId} could not be read.", itemId);
                return Results.Json(new { message = "物品资料读取失败，请稍后重试。" }, statusCode: 500);
            }
        }).WithMetadata(Anyone, new OutsideGameplay("PVF 内容源与缓存：物品说明"));
    }
    private static GmRequiredPermission Anyone => new(GmPermission.None);
    private static IResult NotFound(string message) => Results.Json(new { message }, statusCode: 404);
}
