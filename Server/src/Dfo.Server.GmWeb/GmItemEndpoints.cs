using Dfo.Server.Data.Administration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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
        }).WithMetadata(Anyone);
        group.MapGet(Prefix + "/{itemId:int}/icon", (HttpContext context, GmIconPackStore icons, int itemId) =>
        {
            if (itemId <= 0 || icons.Icon(itemId) is not { } png) return NotFound("图标不存在。");
            // Icons are keyed by item ID and only change when the pack is rebuilt; overrides the boundary's no-store.
            context.Response.Headers.CacheControl = "private, max-age=86400";
            return Results.Bytes(png, "image/png");
        }).WithMetadata(Anyone);
    }
    private static GmRequiredPermission Anyone => new(GmPermission.None);
    private static IResult NotFound(string message) => Results.Json(new { message }, statusCode: 404);
}
