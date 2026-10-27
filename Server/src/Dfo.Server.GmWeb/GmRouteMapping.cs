using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;
using Dfo.Server.HttpApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.GmWeb;

internal static class GmRouteMapping
{
    internal static void Map(RouteGroupBuilder group, string method, string path, HttpRouteOperation operation, Delegate parser, GmRequiredPermission permission) =>
        group.MapMethods(path, [method], parser).WithMetadata(permission, new HttpGameplayOperation(operation, GmRouteReplyMapper.Instance));
}

internal sealed class GmRouteReplyMapper : IHttpRouteReplyMapper
{
    internal static GmRouteReplyMapper Instance { get; } = new();
    public async ValueTask<IResult> MapAsync(HttpContext context, HttpRouteOperation operation, HttpRouteReply reply)
    {
        if (reply.Error is { } error)
        {
            if (!reply.ExecutorFailed && reply.Outcome is "auditFailed" or "unknown" or "reminderFailed")
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(operation == HttpRouteOperation.CeraGrant
                    ? "Dfo.Server.GmWeb.Cera" : "Dfo.Server.GmWeb.OfficialMail").LogError(error,
                    "GM operation failed with outcome {Outcome}.", reply.Outcome);
            if (reply.Outcome == "auditFailed") return Error(500, "结果暂未确认，失败记录未能保存；请保留原编号查询或重试。", "unknown");
            if (reply.Outcome == "unknown") return Error(500, "结果暂未确认，请用原编号查询或重试。", "unknown");
            if (reply.Outcome == "reminderFailed") return Error(500, "点券已到账，提醒未能发出；勿新建编号重发。", "unknown");
            if (error is HttpRouteInputException parsed) return Error(parsed.StatusCode, parsed.Message);
            if (operation is >= HttpRouteOperation.CeraAccounts and <= HttpRouteOperation.CeraGrant)
                return GmCeraEndpoints.Failure(context, error, reply.Outcome, reply.ExecutorFailed);
            if (operation is >= HttpRouteOperation.MailDraft and <= HttpRouteOperation.MailSend)
                return GmOfficialMailEndpoints.Failure(context, error, reply.Outcome, reply.ExecutorFailed);
            if (operation is >= HttpRouteOperation.Initialization)
                return await GmInitializationEndpoints.Safe(context, () => Task.FromException<IResult>(error));
            if (operation is >= HttpRouteOperation.Overview and <= HttpRouteOperation.Character)
            {
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Dfo.Server.GmWeb.PlayerDirectory").LogError(error, "GM player directory query failed.");
                return Error(500, "查询失败，请稍后重试。");
            }
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
        }
        if (operation == HttpRouteOperation.Logout) return GmAuthenticationEndpoints.LogoutResponse(context, (bool)reply.Value!);
        if (operation == HttpRouteOperation.Account && reply.Value is GmAccountLookup account)
            return account.Value is null ? Error(404, "账号不存在。") : Results.Ok(account.Value);
        if (operation == HttpRouteOperation.Character && reply.Value is GmCharacterLookup character)
        {
            if (character.Value is null) return Error(404, "角色不存在。");
            var value = character.Value.Character;
            return Results.Ok(new { value.CharacterId, value.AccountId, value.LoginName, value.Name, value.NameDecodeStatus,
                value.NameBytesHex, value.Job, value.GrowType, value.Level, value.IsDeleted, value.IsOnline,
                value.Channel, value.Position, value.SampledAt, character.Value.Balances });
        }
        if (reply.Value is GmCeraGrantResult granted) return Results.Ok(new { granted.Receipt, granted.Replayed, reminder = reply.Reminder });
        if (reply.Value is GmMailSendResult sent) return Results.Ok(new { sent.Receipt, sent.Replayed, reminder = "unconfirmed" });
        return Results.Ok(reply.Value);
    }
    private static IResult Error(int code, string message, string? outcome = null) => outcome is null
        ? Results.Json(new { message }, statusCode: code) : Results.Json(new { message, outcome }, statusCode: code);
}
