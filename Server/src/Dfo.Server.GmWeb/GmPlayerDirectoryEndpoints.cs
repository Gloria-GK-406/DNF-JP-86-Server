using Dfo.Server.HttpApi;
using System.Globalization;
using Dfo.Server.Application.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.GmWeb;

internal static class GmPlayerDirectoryEndpoints
{
    internal static void Map(RouteGroupBuilder group)
    {
        var permission = new GmRequiredPermission(GmPermission.QueryPlayers);
        Map("/overview", HttpRouteOperation.Overview, () => new HttpRouteInput.Empty());
        Map("/accounts", HttpRouteOperation.Accounts, (HttpContext c) => Players(c, false));
        Map("/characters", HttpRouteOperation.Characters, (HttpContext c) => Players(c, true));
        Map("/accounts/{accountId}", HttpRouteOperation.Account, (string accountId) => Player(accountId, "账号"));
        Map("/characters/{characterId}", HttpRouteOperation.Character, (string characterId) => Player(characterId, "角色"));
        void Map(string path, HttpRouteOperation operation, Delegate parser) => GmRouteMapping.Map(group, "GET", "/api/gm/v1" + path, operation, parser, permission);
    }
    private static HttpRouteInput Players(HttpContext c, bool characters) => TryFilter(c.Request.Query, characters, out var filter)
        ? new HttpRouteInput.Players(filter!) : throw new HttpRouteInputException(400, "查询参数无效。");
    private static HttpRouteInput Player(string value, string kind) => Id(value, out var id)
        ? new HttpRouteInput.Player(id) : throw new HttpRouteInputException(400, kind + " ID 无效。");
    private static async Task<IResult> Safe(HttpContext context, Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Dfo.Server.GmWeb.PlayerDirectory")
                .LogError(error, "GM player directory query failed.");
            return Error(500, "查询失败，请稍后重试。");
        }
    }
    private static IResult Error(int status, string message) => Results.Json(new { message }, statusCode: status);
    private static bool Id(string value, out int id) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    private static bool TryFilter(IQueryCollection query, bool characters, out GmPlayerFilter? filter)
    {
        filter = null;
        var known = characters ? new[] { "page", "pageSize", "accountId", "characterId", "q", "onlineOnly" }
            : new[] { "page", "pageSize", "accountId", "q", "onlineOnly" };
        if (known.Any(key => query.TryGetValue(key, out var values) && values.Count != 1)) return false;
        var page = 1; var size = 20; int? account = null; int? character = null;
        if (query.TryGetValue("page", out var p) && (!Id(p.ToString(), out page) || page > 1000000)) return false;
        if (query.TryGetValue("pageSize", out var s) && (!Id(s.ToString(), out size) || size > 100)) return false;
        if (query.TryGetValue("accountId", out var a)) { if (!Id(a.ToString(), out var id)) return false; account = id; }
        if (characters && query.TryGetValue("characterId", out var c)) { if (!Id(c.ToString(), out var id)) return false; character = id; }
        var online = false;
        if (query.TryGetValue("onlineOnly", out var o)) { if (o.ToString() is not ("true" or "false")) return false; online = o == "true"; }
        var text = query.TryGetValue("q", out var q) ? q.ToString() : null;
        if (text?.Length > 128) return false;
        text = text?.Trim();
        filter = new(page, size, account, character, string.IsNullOrEmpty(text) ? null : text, online);
        return true;
    }
}
