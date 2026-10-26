using Dfo.Server.Data.Inventory;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Initialization;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Initialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.GmWeb;

internal static class GmInitializationEndpoints
{
    private const string Prefix = "/api/gm/v1/initialization";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    internal static void Map(RouteGroupBuilder group)
    {
        var permission = new GmRequiredPermission(GmPermission.All);
        Map("GET", "", HttpRouteOperation.Initialization, (HttpContext c) => new HttpRouteInput.Session(Secret(c)));
        Map("POST", "/account", HttpRouteOperation.InitializationAccount, async (HttpContext c) => new HttpRouteInput.AccountInitializationRequest(Secret(c), await Read<AccountInitialization>(c)));
        Map("POST", "/character", HttpRouteOperation.InitializationCharacter, async (HttpContext c) => new HttpRouteInput.CharacterInitializationRequest(Secret(c), await Read<CharacterInitialization>(c)));
        Map("GET", "/items", HttpRouteOperation.InitializationItems, (HttpContext c) => new HttpRouteInput.Catalog(Secret(c), Filter(c)));
        Map("GET", "/items/{id:int}", HttpRouteOperation.InitializationItem, (HttpContext c, int id) => new HttpRouteInput.CatalogItem(Secret(c), id));
        void Map(string method, string path, HttpRouteOperation operation, Delegate parser) => GmRouteMapping.Map(group, method, Prefix + path, operation, parser, permission);
    }
    private static string Secret(HttpContext c) => c.Request.Cookies[GmAccessBoundary.SessionCookie]!;
    private static async Task<SaveInitializationRequest<T>> Read<T>(HttpContext c)
    {
        if (!c.Request.HasJsonContentType() || c.Request.ContentLength is > 32768) throw new ArgumentException("配置请求必须为 JSON，且不超过 32 KiB。");
        if (c.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = 32768;
        return await c.Request.ReadFromJsonAsync<SaveInitializationRequest<T>>(Json, c.RequestAborted) ?? throw new ArgumentException("配置字段缺失。");
    }
    internal static async Task<IResult> Safe(HttpContext c, Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var status = e switch
            {
                InitializationConflictException => 409,
                GmMailException m => m.Reason switch
                {
                    GmMailRefusal.Unauthorized => 401,
                    GmMailRefusal.Forbidden => 403,
                    GmMailRefusal.NotFound => 404,
                    _ => 400
                },
                KeyNotFoundException => 404,
                ArgumentException or JsonException or BadHttpRequestException => 400,
                _ => 500
            };
            if (status == 500 && c.RequestServices.GetService(typeof(ILoggerFactory)) is ILoggerFactory logs)
                logs.CreateLogger("GmInitialization").LogError(e, "Initialization request failed.");
            return Results.Json(new
            {
                message = status == 500 ? "配置保存或读取失败，请重新读取确认当前生效配置。" : e.Message,
                outcome = TransactionOutcomeUncertainException.Contains(e) ? "unknown" : "rejected"
            }, statusCode: status);
        }
    }
    internal static ItemCatalogFilter Filter(HttpContext c)
    {
        var q = c.Request.Query;
        int? Number(string key) => !q.ContainsKey(key) ? null : q[key].Count == 1 && int.TryParse(q[key], out var n) ? n : throw new ArgumentException("物品筛选参数无效。");
        string? Text(string key) => !q.ContainsKey(key) ? null : q[key].Count == 1 ? q[key].ToString() : throw new ArgumentException("物品筛选参数重复。");
        return new(Text("q"), Number("itemId"), Text("category"), Text("equipmentType"), Number("minimumLevel"), Number("maximumLevel"), Number("rarity"), Number("page") ?? 1, Number("pageSize") ?? 15);
    }
}
