using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.GmWeb;

internal static class GmCeraEndpoints
{
    private const string Prefix = "/api/gm/v1/cera";
    private static readonly JsonSerializerOptions InputOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new RequestIdConverter() },
    };
    private static GmRequiredPermission Permission => new(GmPermission.GrantCera);
    internal static void Map(RouteGroupBuilder group)
    {
        Get("/accounts", GmCeraQuery.Accounts); Get("/operations", GmCeraQuery.Operations);
        group.MapGet(Prefix + "/operations/{id}", (HttpContext context, ICommandBus commands, string id) => Safe(context, () => Query(context, commands, GmCeraQuery.Operation, Id(id)))).WithMetadata(Permission);
        group.MapPost(Prefix + "/preview", (HttpContext context, ICommandBus commands) => Safe(context, async () =>
        {
            var input = await Read(context);
            return Results.Ok((await commands.SendAsync(new QueryGmCeraCommand(Secret(context), GmCeraQuery.Preview, input: Input(input)), context.RequestAborted)).Value);
        })).WithMetadata(Permission);
        group.MapPost(Prefix + "/grant", Grant).WithMetadata(Permission);
        void Get(string path, GmCeraQuery kind) => group.MapGet(Prefix + path, (HttpContext context, ICommandBus commands) => Safe(context, () => Query(context, commands, kind))).WithMetadata(Permission);
    }
    private static async Task<IResult> Query(HttpContext context, ICommandBus commands, GmCeraQuery kind, Guid id = default) =>
        Results.Ok((await commands.SendAsync(new QueryGmCeraCommand(Secret(context), kind, Filter(context), id), context.RequestAborted)).Value);
    private static async Task<IResult> Grant(HttpContext context, ICommandBus commands, IGmCeraReminderDomainService reminders)
    {
        GmCeraRequest? input = null; var executing = false;
        try
        {
            input = await Read(context);
            var normalized = (GmCeraInput)(await commands.SendAsync(new QueryGmCeraCommand(Secret(context), GmCeraQuery.Normalize,
                id: input.RequestId, input: Input(input)), context.RequestAborted)).Value;
            executing = true;
            var granted = await commands.SendAsync(new GrantGmCeraCommand(Secret(context), input.RequestId, normalized), context.RequestAborted);
            var reminder = "attempted";
            try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); reminder = await reminders.RemindAsync(granted.Receipt.Input.AccountIds, timeout.Token); }
            catch (Exception error) { Log(context, error, "Committed GM wallet reminder failed."); reminder = "unconfirmed"; }
            return Results.Ok(new { granted.Receipt, granted.Replayed, reminder });
        }
        catch (Exception error)
        {
            var uncertain = TransactionOutcomeUncertainException.Contains(error);
            if (input is not null && input.RequestId != Guid.Empty && error is not GmCeraException { Reason: GmCeraRefusal.Unauthorized or GmCeraRefusal.Forbidden })
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await commands.SendAsync(new RecordGmCeraFailureCommand(Secret(context), input.RequestId,
                        uncertain ? "Unknown" : executing ? "RolledBack" : "Rejected", uncertain ? "提交状态暂未确认" : "发放请求未提交，原因：" + (error is GmCeraException c ? c.Message : "存储或规则处理失败")), timeout.Token);
                }
                catch (Exception auditFailure) { Log(context, auditFailure, "GM cera failure audit could not be recorded."); return Error(500, "结果暂未确认，失败记录未能保存；请保留原编号查询或重试。", "unknown"); }
            if (uncertain) { Log(context, error, "GM wallet transaction outcome is uncertain."); return Error(500, "结果暂未确认，请用原编号查询或重试。", "unknown"); }
            return Failure(context, error, executing ? "rolledBack" : "rejected");
        }
    }
    private static async Task<IResult> Safe(HttpContext context, Func<Task<IResult>> action)
    { try { return await action(); } catch (Exception error) when (error is not OperationCanceledException) { return Failure(context, error); } }
    private static IResult Failure(HttpContext context, Exception error, string? outcome = null)
    {
        if (error is GmCeraException cera) return Error(cera.Reason switch { GmCeraRefusal.Unauthorized => 401, GmCeraRefusal.Forbidden => 403, GmCeraRefusal.NotFound => 404, GmCeraRefusal.Conflict => 409, _ => 400 }, cera.Message, outcome);
        if (error is JsonException or BadHttpRequestException or NotSupportedException) return Error(400, "点券请求字段无效或超过 32 KiB。", outcome);
        Log(context, error, "GM cera request failed.");
        return Error(500, outcome == "rolledBack" ? "发放已回滚，请核对后重试。" : "点券操作失败，请稍后重试。", outcome);
    }
    private static void Log(HttpContext context, Exception error, string message) => context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Dfo.Server.GmWeb.Cera").LogError(error, "{Failure}", message);
    private static IResult Error(int status, string message, string? outcome = null) => Results.Json(new { message, outcome }, statusCode: status);
    private static string Secret(HttpContext context) => context.Request.Cookies[GmAccessBoundary.SessionCookie]!;
    private static Guid Id(string id) => Guid.TryParse(id, out var value) && value != Guid.Empty ? value : throw new GmCeraException(GmCeraRefusal.Invalid, "编号无效。");
    private static GmCeraInput Input(GmCeraRequest input) => new(input.AccountIds, input.Currency, input.Amount, input.Reason);
    private static async Task<GmCeraRequest> Read(HttpContext context)
    {
        if (!context.Request.HasJsonContentType() || context.Request.ContentLength is > 32768) throw new GmCeraException(GmCeraRefusal.Invalid, "点券请求超过 32 KiB 或不是 JSON。");
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = 32768;
        return await context.Request.ReadFromJsonAsync<GmCeraRequest>(InputOptions, context.RequestAborted) ?? throw new GmCeraException(GmCeraRefusal.Invalid, "点券字段缺失。");
    }
    private static GmPlayerFilter Filter(HttpContext context)
    {
        var query = context.Request.Query;
        foreach (var name in new[] { "page", "pageSize", "accountId", "q" })
            if (query.TryGetValue(name, out var values) && values.Count != 1) throw new GmCeraException(GmCeraRefusal.Invalid, "查询参数重复。");
        int Parse(string name, int fallback) => !query.ContainsKey(name) ? fallback : int.TryParse(query[name], NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : throw new GmCeraException(GmCeraRefusal.Invalid, "查询参数无效。");
        var q = query["q"].ToString(); if (q.Length > 128) throw new GmCeraException(GmCeraRefusal.Invalid, "搜索最多 128 个字符。");
        return new(Parse("page", 1), Parse("pageSize", 20), AccountId: query.ContainsKey("accountId") ? Parse("accountId", 0) : null, Q: string.IsNullOrWhiteSpace(q) ? null : q.Trim());
    }
    private sealed class RequestIdConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && Guid.TryParse(reader.GetString(), out var id) ? id : throw new JsonException("Invalid UUID.");
        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }
}
