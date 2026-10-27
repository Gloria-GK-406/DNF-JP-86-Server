using Dfo.Server.HttpApi;
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
        Map("GET", "", HttpRouteOperation.DailyRefill, (HttpContext c) => new HttpRouteInput.Session(Secret(c)));
        Map("POST", "", HttpRouteOperation.DailyRefillChange, async (HttpContext c) => new HttpRouteInput.DailyChange(Secret(c), await Read(c)));
        Map("GET", "/items", HttpRouteOperation.DailyRefillItems, (HttpContext c) => new HttpRouteInput.Catalog(Secret(c), GmInitializationEndpoints.Filter(c)));
        Map("GET", "/items/{id:int}", HttpRouteOperation.DailyRefillItem, (HttpContext c, int id) => new HttpRouteInput.CatalogItem(Secret(c), id));
        void Map(string method, string path, HttpRouteOperation operation, Delegate parser) => GmRouteMapping.Map(group, method, Prefix + path, operation, parser, permission);
    }
    private static string Secret(HttpContext c) => c.Request.Cookies[GmAccessBoundary.SessionCookie]!;
    private static async Task<DailyRefillChange> Read(HttpContext c)
    {
        if (!c.Request.HasJsonContentType() || c.Request.ContentLength is > 32768) throw new ArgumentException("补给请求必须为 JSON，且不超过 32 KiB。");
        if (c.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = 32768;
        return await c.Request.ReadFromJsonAsync<DailyRefillChange>(Json, c.RequestAborted) ?? throw new ArgumentException("补给字段缺失。");
    }
}
