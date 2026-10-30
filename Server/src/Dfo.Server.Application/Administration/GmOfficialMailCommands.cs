using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.HttpOperations;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Administration;

/// <summary>A draft, template, recipient, operation or preview query; preparing is the send's own step and is not a route.</summary>
public sealed record QueryGmMailCommand(string Secret, GmMailQuery Kind, GmPlayerFilter Filter, Guid Id = default, GmMailInput? Input = null) : HttpOperationCommand;
public sealed record EditGmMailCommand(string Secret, GmMailEdit Kind, Guid Id, GmMailEditorRequest Request) : HttpOperationCommand;
public sealed record SendGmMailCommand(string Secret, GmMailEditorRequest Request) : HttpOperationCommand;

internal sealed class GmOfficialMailCommandHandler(IGmOfficialMailDomainService mail, ICommandWorkUnits units, ICommandFailureAudit failureAudit)
    : ICommandHandler<QueryGmMailCommand, HttpRouteReply>, ICommandHandler<EditGmMailCommand, HttpRouteReply>,
      ICommandHandler<SendGmMailCommand, HttpRouteReply>
{
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryGmMailCommand command, CancellationToken token)
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Gm);
        if (command.Kind is not (GmMailQuery.Draft or GmMailQuery.Templates or GmMailQuery.Recipients
            or GmMailQuery.Operations or GmMailQuery.Operation or GmMailQuery.Preview))
            throw new InvalidOperationException("The mail query is not a route operation.");
        return HttpOperationWork.ReplyAsync(context, async () => (await HttpOperationWork.WriteAsync(units, context, "query-gm-mail",
            t => mail.QueryAsync(command.Secret, command.Kind, command.Filter, command.Id, command.Input, t), token).ConfigureAwait(false)).Value);
    }

    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, EditGmMailCommand command, CancellationToken token)
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Gm);
        if (!Enum.IsDefined(command.Kind)) throw new InvalidOperationException("The mail edit is not a route operation.");
        return HttpOperationWork.ReplyAsync(context, async () => await HttpOperationWork.WriteAsync(units, context, "edit-gm-mail",
            t => mail.EditAsync(command.Secret, command.Kind, command.Id, command.Request, t), token).ConfigureAwait(false));
    }

    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, SendGmMailCommand command, CancellationToken token)
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Gm);
        return HttpOperationWork.ReplyAsync(context, async () => await SendAsync(context, command, token).ConfigureAwait(false));
    }

    private async ValueTask<HttpRouteReply> SendAsync(CommandExecutionContext c, SendGmMailCommand command, CancellationToken ct)
    {
        var executing = false;
        var input = command.Request;
        try
        {
            var prepared = (GmMailPreparation)(await HttpOperationWork.WriteAsync(units, c, "query-gm-mail-prepare", t => mail.QueryAsync(command.Secret, GmMailQuery.Prepare,
                new(), input.RequestId, new(input.Title, input.Body, input.RecipientIds, input.Items, input.Gold), t), ct, CommandFailureAuditRole.Preparation)).Value;
            executing = true;
            return new(await HttpOperationWork.WriteAsync(units, c, "send-gm-mail", t => mail.SendAsync(command.Secret, prepared, t),
                ct, CommandFailureAuditRole.Execution));
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
            if (input.RequestId != Guid.Empty && error is not GmMailException { Reason: GmMailRefusal.Unauthorized or GmMailRefusal.Forbidden })
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await HttpOperationWork.WriteAsync(units, c, "record-gm-mail-failure", t => RecordFailureAsync(command, executing
                        ? CommandFailureAuditOutcome.RolledBack : CommandFailureAuditOutcome.Rejected, error, t), timeout.Token);
                }
                catch (Exception auditError) when (HttpOperationWork.BusinessFailure(auditError) && c.Request.Failure is null)
                { return new(Error: auditError, Outcome: "auditFailed"); }
            return new(Error: error, Outcome: executing ? "rolledBack" : "rejected");
        }
    }

    private ValueTask<bool> RecordFailureAsync(SendGmMailCommand command, CommandFailureAuditOutcome outcome, Exception cause, CancellationToken ct) =>
        mail.RecordFailureAsync(command.Secret, command.Request.RequestId, outcome.ToString(),
            GmGrantFailureReason.Of(outcome, "发送请求未提交，原因：", cause is GmMailException e ? e.Message : null), ct);
}
