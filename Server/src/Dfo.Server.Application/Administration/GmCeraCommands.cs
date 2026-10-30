using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.HttpOperations;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Administration;

/// <summary>An account, operation or preview query; normalizing is the grant's own preparation and is not a route.</summary>
public sealed record QueryGmCeraCommand(string Secret, GmCeraQuery Kind, GmPlayerFilter Filter, Guid Id = default, GmCeraInput? Input = null) : HttpOperationCommand;
public sealed record GrantGmCeraCommand(string Secret, GmCeraRequest Request) : HttpOperationCommand;

internal sealed class GmCeraCommandHandler(IGmCeraDomainService cera, IGmCeraReminderDomainService reminders,
    ICommandWorkUnits units, ICommandFailureAudit failureAudit)
    : ICommandHandler<QueryGmCeraCommand, HttpRouteReply>, ICommandHandler<GrantGmCeraCommand, HttpRouteReply>
{
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryGmCeraCommand command, CancellationToken token)
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Gm);
        if (command.Kind is not (GmCeraQuery.Accounts or GmCeraQuery.Operations or GmCeraQuery.Operation or GmCeraQuery.Preview))
            throw new InvalidOperationException("The cera query is not a route operation.");
        return HttpOperationWork.ReplyAsync(context, async () => (await HttpOperationWork.WriteAsync(units, context, "query-gm-cera",
            t => cera.QueryAsync(command.Secret, command.Kind, command.Filter, command.Id, command.Input, t), token).ConfigureAwait(false)).Value);
    }

    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, GrantGmCeraCommand command, CancellationToken token)
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Gm);
        return HttpOperationWork.ReplyAsync(context, async () => await GrantAsync(context, command, token).ConfigureAwait(false));
    }

    private async ValueTask<HttpRouteReply> GrantAsync(CommandExecutionContext c, GrantGmCeraCommand command, CancellationToken ct)
    {
        var executing = false;
        GmCeraGrantResult granted;
        var input = command.Request;
        try
        {
            var normalized = (GmCeraInput)(await HttpOperationWork.WriteAsync(units, c, "query-gm-cera-normalize", t => cera.QueryAsync(command.Secret, GmCeraQuery.Normalize,
                new(), input.RequestId, new(input.AccountIds, input.Currency, input.Amount, input.Reason), t), ct, CommandFailureAuditRole.Preparation)).Value;
            // The old fine command froze this independently of the transport arrays.
            normalized = normalized with { AccountIds = (int[])normalized.AccountIds.Clone() };
            executing = true;
            granted = await HttpOperationWork.WriteAsync(units, c, "grant-gm-cera", t => cera.GrantAsync(command.Secret, input.RequestId, normalized, t),
                ct, CommandFailureAuditRole.Execution);
        }
        catch (Exception error)
        {
            if (c.Request!.Failure is not null)
            {
                if (input.RequestId == Guid.Empty) throw;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await failureAudit.RecordAsync(c, error, async (fact, t) => await RecordFailureAsync(command, fact.Outcome, fact.Cause, t), timeout.Token);
                throw;
            }
            if (!HttpOperationWork.BusinessFailure(error)) throw;
            if (input.RequestId != Guid.Empty && error is not GmCeraException { Reason: GmCeraRefusal.Unauthorized or GmCeraRefusal.Forbidden })
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await HttpOperationWork.WriteAsync(units, c, "record-gm-cera-failure", t => RecordFailureAsync(command, executing
                        ? CommandFailureAuditOutcome.RolledBack : CommandFailureAuditOutcome.Rejected, error, t), timeout.Token);
                }
                catch (Exception auditError) when (HttpOperationWork.BusinessFailure(auditError) && c.Request.Failure is null)
                { return new(Error: auditError, Outcome: "auditFailed"); }
            return new(Error: error, Outcome: executing ? "rolledBack" : "rejected");
        }
        // The reminder is deliberately after the grant catch: it can never record a rolled-back grant.
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return new(granted, Reminder: await reminders.RemindAsync(granted.Receipt.Input.AccountIds, timeout.Token));
        }
        catch (Exception error) when (HttpOperationWork.BusinessFailure(error) && c.Request!.Failure is null)
        { return new(Error: error, Outcome: "reminderFailed"); }
    }

    private ValueTask<bool> RecordFailureAsync(GrantGmCeraCommand command, CommandFailureAuditOutcome outcome, Exception cause, CancellationToken ct) =>
        cera.RecordFailureAsync(command.Secret, command.Request.RequestId, outcome.ToString(),
            GmGrantFailureReason.Of(outcome, "发放请求未提交，原因：", cause is GmCeraException e ? e.Message : null), ct);
}

internal static class GmGrantFailureReason
{
    internal static string Of(CommandFailureAuditOutcome outcome, string prefix, string? refusal) =>
        outcome == CommandFailureAuditOutcome.Unknown ? "提交状态暂未确认" : prefix + (refusal ?? "存储或规则处理失败");
}
