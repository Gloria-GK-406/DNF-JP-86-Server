using System.Text.Json;
using System.Text.Json.Serialization;
using Dfo.Server.Application.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Inventory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
namespace Dfo.Server.GmWeb;

internal static class GmDailyRefillEndpoints
{
    private const string Prefix = "/api/gm/v1/daily-refill";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    internal static void Map(RouteGroupBuilder group)
    {
        var permission = new GmRequiredPermission(GmPermission.All);
        group.MapGet(Prefix, (HttpContext c, ICommandBus bus) => GmInitializationEndpoints.Safe(c, async () =>
            Results.Ok(await bus.SendAsync(new QueryGmDailyRefillCommand(Secret(c)), c.RequestAborted)))).WithMetadata(permission);
        group.MapPost(Prefix, (HttpContext c, ICommandBus bus) => GmInitializationEndpoints.Safe(c, async () =>
            Results.Ok(await bus.SendAsync(new ChangeGmDailyRefillCommand(Secret(c), await Read(c)), c.RequestAborted)))).WithMetadata(permission);
        group.MapGet(Prefix + "/items", (HttpContext c, ICommandBus bus) => GmInitializationEndpoints.Safe(c, async () =>
            Results.Ok(await bus.SendAsync(new SearchGmDailyRefillItemsCommand(Secret(c), GmInitializationEndpoints.Filter(c)), c.RequestAborted)))).WithMetadata(permission);
        group.MapGet(Prefix + "/items/{id:int}", (HttpContext c, ICommandBus bus, int id) => GmInitializationEndpoints.Safe(c, async () =>
            Results.Ok(await bus.SendAsync(new ReadGmDailyRefillItemCommand(Secret(c), id), c.RequestAborted)))).WithMetadata(permission);
    }
    private static string Secret(HttpContext c) => c.Request.Cookies[GmAccessBoundary.SessionCookie]!;
    private static async Task<DailyRefillChange> Read(HttpContext c)
    {
        if (!c.Request.HasJsonContentType() || c.Request.ContentLength is > 32768) throw new ArgumentException("补给请求必须为 JSON，且不超过 32 KiB。");
        if (c.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = 32768;
        return await c.Request.ReadFromJsonAsync<DailyRefillChange>(Json, c.RequestAborted) ?? throw new ArgumentException("补给字段缺失。");
    }
}
