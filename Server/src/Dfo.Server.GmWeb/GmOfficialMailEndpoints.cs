using Dfo.Server.Data.Inventory;
using System.Globalization;
using System.Text.Json;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;
using Dfo.Server.HttpApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.GmWeb;

internal static class GmOfficialMailEndpoints
{
    private const string Prefix = "/api/gm/v1/mail";
    internal static void Map(RouteGroupBuilder group)
    {
        Get("/draft", GmMailQuery.Draft); Get("/templates", GmMailQuery.Templates);
        Get("/recipients", GmMailQuery.Recipients); Get("/operations", GmMailQuery.Operations);
        group.MapGet(Prefix + "/items", (HttpContext context, IGmOfficialMailDomainService mail) => Safe(context,
            async () => Results.Ok(await mail.SearchItemsAsync(Secret(context), ItemFilter(context), context.RequestAborted))))
            .WithMetadata(Permission, new OutsideGameplay("只读 SQLite 的查询：身份识别与物品目录"));
        group.MapGet(Prefix + "/items/{itemId:int}", (HttpContext context, IGmOfficialMailDomainService mail, int itemId) => Safe(context,
            async () => Results.Ok(await mail.ItemDetailsAsync(Secret(context), itemId, context.RequestAborted))))
            .WithMetadata(Permission, new OutsideGameplay("只读 SQLite 的查询：身份识别与物品目录"));
        Map("GET", "/operations/{id}", HttpRouteOperation.MailOperation, (HttpContext c, string id) => Query(c, GmMailQuery.Operation, Id(id)));
        Map("POST", "/preview", HttpRouteOperation.MailPreview, async (HttpContext c) => new HttpRouteInput.Mail(Secret(c), GmMailQuery.Preview, new(), Input: Input(await Read(c))));
        Edit("/draft", "PUT", GmMailEdit.Draft, HttpRouteOperation.MailSaveDraft);
        Edit("/templates", "POST", GmMailEdit.CreateTemplate, HttpRouteOperation.MailCreateTemplate);
        Edit("/templates/{id}", "PUT", GmMailEdit.UpdateTemplate, HttpRouteOperation.MailUpdateTemplate);
        Edit("/templates/{id}", "DELETE", GmMailEdit.DeleteTemplate, HttpRouteOperation.MailDeleteTemplate);
        Map("POST", "/send", HttpRouteOperation.MailSend, async (HttpContext c) => new HttpRouteInput.MailSendRequest(Secret(c), await Read(c)));
        void Get(string path, GmMailQuery kind) => Map("GET", path, kind switch
        {
            GmMailQuery.Draft => HttpRouteOperation.MailDraft, GmMailQuery.Templates => HttpRouteOperation.MailTemplates,
            GmMailQuery.Recipients => HttpRouteOperation.MailRecipients, GmMailQuery.Operations => HttpRouteOperation.MailOperations,
            _ => throw new InvalidOperationException()
        }, (HttpContext c) => Query(c, kind));
        void Edit(string path, string method, GmMailEdit kind, HttpRouteOperation operation) => Map(method, path, operation,
            async (HttpContext c) => new HttpRouteInput.MailEditRequest(Secret(c), kind, c.Request.RouteValues["id"] is string id ? Id(id) : Guid.Empty, await Read(c)));
        void Map(string method, string path, HttpRouteOperation operation, Delegate parser) => GmRouteMapping.Map(group, method, Prefix + path, operation, parser, Permission);
    }
    private static GmRequiredPermission Permission => new(GmPermission.SendMail);
    private static HttpRouteInput Query(HttpContext c, GmMailQuery kind, Guid id = default) => new HttpRouteInput.Mail(Secret(c), kind, Filter(c), id);
    private static async Task<IResult> Safe(HttpContext context, Func<Task<IResult>> action)
    { try { return await action(); } catch (Exception error) when (error is not OperationCanceledException) { return Failure(context, error); } }
    internal static IResult Failure(HttpContext context, Exception error, string? outcome = null, bool alreadyLogged = false)
    {
        if (error is KeyNotFoundException) return Error(404, error.Message, outcome);
        if (error is ArgumentException) return Error(400, error.Message, outcome);
        if (error is GmMailException mail) return Error(mail.Reason switch { GmMailRefusal.Unauthorized => 401, GmMailRefusal.Forbidden => 403, GmMailRefusal.NotFound => 404, GmMailRefusal.Conflict or GmMailRefusal.Changed => 409, _ => 400 }, mail.Message, outcome);
        if (error is JsonException or BadHttpRequestException or NotSupportedException) return Error(400, "邮件请求字段无效或超过 32 KiB。", outcome);
        if (!alreadyLogged) Log(context, error, "GM mail request failed.");
        return Error(500, outcome == "rolledBack" ? "发送已回滚，请检查内容后重试。" : "邮件操作失败，请稍后重试。", outcome);
    }
    private static void Log(HttpContext context, Exception error, string message) => context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Dfo.Server.GmWeb.OfficialMail").LogError(error, "{Failure}", message);
    private static IResult Error(int status, string message, string? outcome = null) => Results.Json(new { message, outcome }, statusCode: status);
    private static string Secret(HttpContext context) => context.Request.Cookies[GmAccessBoundary.SessionCookie]!;
    private static Guid Id(string id) => Guid.TryParse(id, out var value) && value != Guid.Empty ? value : throw new GmMailException(GmMailRefusal.Invalid, "编号无效。");
    private static GmMailInput Input(GmMailEditorRequest input) => new(input.Title, input.Body, input.RecipientIds, input.Items, input.Gold);
    private static async Task<GmMailEditorRequest> Read(HttpContext context)
    {
        if (!context.Request.HasJsonContentType() || context.Request.ContentLength is > 32768) throw new GmMailException(GmMailRefusal.Invalid, "邮件请求超过 32 KiB 或不是 JSON。");
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = 32768;
        return await context.Request.ReadFromJsonAsync<GmMailEditorRequest>(cancellationToken: context.RequestAborted) ?? throw new GmMailException(GmMailRefusal.Invalid, "邮件字段缺失。");
    }
    private static GmPlayerFilter Filter(HttpContext context)
    {
        var query = context.Request.Query;
        foreach (var name in new[] { "page", "pageSize", "characterId", "q" })
            if (query.TryGetValue(name, out var values) && values.Count != 1) throw new GmMailException(GmMailRefusal.Invalid, "查询参数重复。");
        int Parse(string name, int fallback) => !query.ContainsKey(name) ? fallback : int.TryParse(query[name], NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : throw new GmMailException(GmMailRefusal.Invalid, "查询参数无效。");
        var q = query["q"].ToString(); if (q.Length > 128) throw new GmMailException(GmMailRefusal.Invalid, "搜索最多 128 个字符。");
        return new(Parse("page", 1), Parse("pageSize", 20), CharacterId: query.ContainsKey("characterId") ? Parse("characterId", 0) : null, Q: string.IsNullOrWhiteSpace(q) ? null : q.Trim());
    }
    private static ItemCatalogFilter ItemFilter(HttpContext context)
    {
        var query = context.Request.Query;
        foreach (var name in new[] { "q", "itemId", "category", "equipmentType", "minimumLevel", "maximumLevel", "rarity", "page", "pageSize" })
            if (query.TryGetValue(name, out var values) && values.Count != 1)
                throw new GmMailException(GmMailRefusal.Invalid, "查询参数重复。");
        int? Number(string name) => !query.ContainsKey(name) ? null
            : int.TryParse(query[name], NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number
            : throw new GmMailException(GmMailRefusal.Invalid, "查询参数无效。");
        string? Text(string name) => string.IsNullOrWhiteSpace(query[name]) ? null : query[name].ToString().Trim();
        return new(Text("q"), Number("itemId"), Text("category"), Text("equipmentType"), Number("minimumLevel"),
            Number("maximumLevel"), Number("rarity"), Number("page") ?? 1, Number("pageSize") ?? 20);
    }
}
