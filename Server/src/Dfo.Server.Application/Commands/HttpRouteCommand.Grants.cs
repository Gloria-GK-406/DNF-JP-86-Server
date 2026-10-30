using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Commands;

internal sealed partial class HttpRouteCommandHandler
{
    private async ValueTask<HttpRouteReply> GrantCera(CommandExecutionContext c, HttpRouteCommand command, HttpRouteInput.CeraGrantRequest p, CancellationToken ct)
    {
        var executing = false;
        GmCeraGrantResult granted;
        try
        {
            var input = p.Value;
            var normalized = (GmCeraInput)(await Write(c, "query-gm-cera-normalize", t => cera.QueryAsync(p.Secret, GmCeraQuery.Normalize,
                new(), input.RequestId, new(input.AccountIds, input.Currency, input.Amount, input.Reason), t), ct, CommandFailureAuditRole.Preparation)).Value;
            // The old fine command froze this independently of the transport arrays.
            normalized = normalized with { AccountIds = (int[])normalized.AccountIds.Clone() };
            executing = true;
            granted = await Write(c, "grant-gm-cera", t => cera.GrantAsync(p.Secret, input.RequestId, normalized, t), ct, CommandFailureAuditRole.Execution);
        }
        catch (Exception error)
        {
            if (c.Request!.Failure is not null)
            {
                if (p.Value.RequestId == Guid.Empty) throw;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await failureAudit.RecordAsync(c, error, async (fact, t) => await RecordCeraFailureAsync(p, fact.Outcome, fact.Cause, t), timeout.Token);
                throw;
            }
            if (!BusinessFailure(error)) throw;
            if (p.Value.RequestId != Guid.Empty && error is not GmCeraException { Reason: GmCeraRefusal.Unauthorized or GmCeraRefusal.Forbidden })
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await Write(c, "record-gm-cera-failure", t => RecordCeraFailureAsync(p, executing
                        ? CommandFailureAuditOutcome.RolledBack : CommandFailureAuditOutcome.Rejected, error, t), timeout.Token);
                }
                catch (Exception auditError) when (BusinessFailure(auditError) && c.Request.Failure is null)
                { return new(Error: auditError, Outcome: "auditFailed"); }
            return new(Error: error, Outcome: executing ? "rolledBack" : "rejected");
        }
        // The reminder is deliberately after the grant catch: it can never record a rolled-back grant.
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return new(granted, Reminder: await reminders.RemindAsync(granted.Receipt.Input.AccountIds, timeout.Token));
        }
        catch (Exception error) when (BusinessFailure(error) && c.Request!.Failure is null)
        { return new(Error: error, Outcome: "reminderFailed"); }
    }

    private async ValueTask<HttpRouteReply> SendMail(CommandExecutionContext c, HttpRouteCommand command, HttpRouteInput.MailSendRequest p, CancellationToken ct)
    {
        var executing = false;
        try
        {
            var input = p.Value;
            var prepared = (GmMailPreparation)(await Write(c, "query-gm-mail-prepare", t => mail.QueryAsync(p.Secret, GmMailQuery.Prepare,
                new(), input.RequestId, new(input.Title, input.Body, input.RecipientIds, input.Items, input.Gold), t), ct, CommandFailureAuditRole.Preparation)).Value;
            executing = true;
            return new(await Write(c, "send-gm-mail", t => mail.SendAsync(p.Secret, prepared, t), ct, CommandFailureAuditRole.Execution));
        }
        catch (Exception error)
        {
            if (c.Request!.Failure is not null)
            {
                if (p.Value.RequestId == Guid.Empty) throw;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await failureAudit.RecordAsync(c, error, async (fact, t) => await RecordMailFailureAsync(p, fact.Outcome, fact.Cause, t), timeout.Token);
                throw;
            }
            if (!BusinessFailure(error)) throw;
            if (p.Value.RequestId != Guid.Empty && error is not GmMailException { Reason: GmMailRefusal.Unauthorized or GmMailRefusal.Forbidden })
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await Write(c, "record-gm-mail-failure", t => RecordMailFailureAsync(p, executing
                        ? CommandFailureAuditOutcome.RolledBack : CommandFailureAuditOutcome.Rejected, error, t), timeout.Token);
                }
                catch (Exception auditError) when (BusinessFailure(auditError) && c.Request.Failure is null)
                { return new(Error: auditError, Outcome: "auditFailed"); }
            return new(Error: error, Outcome: executing ? "rolledBack" : "rejected");
        }
    }
    private ValueTask<bool> RecordCeraFailureAsync(HttpRouteInput.CeraGrantRequest p, CommandFailureAuditOutcome outcome, Exception cause, CancellationToken ct) =>
        cera.RecordFailureAsync(p.Secret, p.Value.RequestId, outcome.ToString(),
            FailureReason(outcome, "发放请求未提交，原因：", cause is GmCeraException e ? e.Message : null), ct);
    private ValueTask<bool> RecordMailFailureAsync(HttpRouteInput.MailSendRequest p, CommandFailureAuditOutcome outcome, Exception cause, CancellationToken ct) =>
        mail.RecordFailureAsync(p.Secret, p.Value.RequestId, outcome.ToString(),
            FailureReason(outcome, "发送请求未提交，原因：", cause is GmMailException e ? e.Message : null), ct);
    private static string FailureReason(CommandFailureAuditOutcome outcome, string prefix, string? refusal) =>
        outcome == CommandFailureAuditOutcome.Unknown ? "提交状态暂未确认" : prefix + (refusal ?? "存储或规则处理失败");
}
