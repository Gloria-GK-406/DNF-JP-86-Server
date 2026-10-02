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
        group.MapGet("/api/gm/v1/overview", (ICommandBus commands, HttpContext context) => Safe(context, async () =>
            Results.Ok(await commands.SendAsync(new QueryGmOverviewCommand(), context.RequestAborted))))
            .WithMetadata(new GmRequiredPermission(GmPermission.QueryPlayers));
        group.MapGet("/api/gm/v1/accounts", (ICommandBus commands, HttpContext context) => Safe(context, async () =>
            TryFilter(context.Request.Query, false, out var filter)
                ? Results.Ok(await commands.SendAsync(new QueryGmAccountsCommand(filter!), context.RequestAborted)) : Error(400, "查询参数无效。")))
            .WithMetadata(new GmRequiredPermission(GmPermission.QueryPlayers));
        group.MapGet("/api/gm/v1/characters", (ICommandBus commands, HttpContext context) => Safe(context, async () =>
            TryFilter(context.Request.Query, true, out var filter)
                ? Results.Ok(await commands.SendAsync(new QueryGmCharactersCommand(filter!), context.RequestAborted)) : Error(400, "查询参数无效。")))
            .WithMetadata(new GmRequiredPermission(GmPermission.QueryPlayers));
        group.MapGet("/api/gm/v1/accounts/{accountId}", (string accountId, ICommandBus commands, HttpContext context) => Safe(context, async () =>
        {
            if (!Id(accountId, out var id)) return Error(400, "账号 ID 无效。");
            var result = await commands.SendAsync(new FindGmAccountCommand(id), context.RequestAborted);
            return result.Value is null ? Error(404, "账号不存在。") : Results.Ok(result.Value);
        })).WithMetadata(new GmRequiredPermission(GmPermission.QueryPlayers));
        group.MapGet("/api/gm/v1/characters/{characterId}", (string characterId, ICommandBus commands, HttpContext context) => Safe(context, async () =>
        {
            if (!Id(characterId, out var id)) return Error(400, "角色 ID 无效。");
            var result = await commands.SendAsync(new FindGmCharacterCommand(id), context.RequestAborted);
            if (result.Value is null) return Error(404, "角色不存在。");
            var value = result.Value.Character;
            return Results.Ok(new { value.CharacterId, value.AccountId, value.LoginName, value.Name, value.NameDecodeStatus,
                value.NameBytesHex, value.Job, value.GrowType, value.Level, value.IsDeleted, value.IsOnline,
                value.Channel, value.Position, value.SampledAt, result.Value.Balances });
        })).WithMetadata(new GmRequiredPermission(GmPermission.QueryPlayers));
    }

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
