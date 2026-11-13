using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.PlayerPortal;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.HttpOperations;
using Dfo.Server.Data.PlayerPortal;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.PlayerPortal;

/// <summary>Opens a player session for a password checked off the executor (SD-1: the switch is judged again in here).</summary>
public sealed record LoginPlayerPortalCommand(PlayerCredentialCheck Check) : HttpOperationCommand;
public sealed record LogoutPlayerPortalCommand(string Secret) : HttpOperationCommand;
public sealed record QueryPlayerMeCommand(string Secret) : HttpOperationCommand;

/// <summary>Adds cera to the session's own account (RQ-010); the account is never taken from the request.</summary>
public sealed record GrantPlayerCeraCommand(string Secret, PlayerCeraRequest Request) : HttpOperationCommand;
/// <summary>Mails a character of the session's own account (RQ-011); the account is never taken from the request.</summary>
public sealed record SendPlayerMailCommand(string Secret, PlayerMailRequest Request) : HttpOperationCommand;
/// <summary>The session's own records, optionally of one kind (RQ-013).</summary>
public sealed record QueryPlayerOperationsCommand(string Secret, PlayerPortalOperationKind? Kind, int Page, int PageSize) : HttpOperationCommand;

public sealed record QueryPlayerPortalSettingsCommand(string GmSecret) : HttpOperationCommand;
public sealed record ChangePlayerPortalSettingsCommand(string GmSecret, PlayerSettingsChange Change) : HttpOperationCommand;
public sealed record QueryPlayerPortalSessionsCommand(string GmSecret) : HttpOperationCommand;
/// <summary>Ends the sessions of <see cref="AccountId"/>, or of every player when it is null.</summary>
public sealed record KickPlayerPortalSessionsCommand(string GmSecret, int? AccountId) : HttpOperationCommand;

/// <summary>
/// The player routes run as GM-source CMDs (they share the GmWeb listener). A refusal is the reply's error; the login
/// answers with its status instead, because its failure audit has to commit.
/// </summary>
internal sealed class PlayerPortalCommandHandler(IPlayerPortalAccessDomainService access, IPlayerPortalAccountDomainService account,
    IPlayerPortalAdministrationDomainService administration, ICommandWorkUnits units)
    : ICommandHandler<LoginPlayerPortalCommand, HttpRouteReply>, ICommandHandler<LogoutPlayerPortalCommand, HttpRouteReply>,
      ICommandHandler<QueryPlayerMeCommand, HttpRouteReply>, ICommandHandler<QueryPlayerOperationsCommand, HttpRouteReply>,
      ICommandHandler<QueryPlayerPortalSettingsCommand, HttpRouteReply>,
      ICommandHandler<ChangePlayerPortalSettingsCommand, HttpRouteReply>, ICommandHandler<QueryPlayerPortalSessionsCommand, HttpRouteReply>,
      ICommandHandler<KickPlayerPortalSessionsCommand, HttpRouteReply>
{
    public async ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, LoginPlayerPortalCommand command, CancellationToken token)
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Gm);
        return new(await HttpOperationWork.WriteAsync(units, context, "login-player-portal", t => access.LoginAsync(command.Check, t), token).ConfigureAwait(false));
    }
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, LogoutPlayerPortalCommand command, CancellationToken token) =>
        Write(context, "logout-player-portal", async t => { await access.LogoutAsync(command.Secret, t).ConfigureAwait(false); return true; }, token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryPlayerMeCommand command, CancellationToken token) =>
        Write(context, "query-player-me", t => account.MeAsync(command.Secret, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryPlayerOperationsCommand command, CancellationToken token) =>
        Write(context, "query-player-operations", t => account.OperationsAsync(command.Secret, command.Kind, command.Page, command.PageSize, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryPlayerPortalSettingsCommand command, CancellationToken token) =>
        Write(context, "query-player-portal-settings", t => administration.SettingsAsync(command.GmSecret, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, ChangePlayerPortalSettingsCommand command, CancellationToken token) =>
        Write(context, "change-player-portal-settings", t => administration.ChangeSettingsAsync(command.GmSecret, command.Change, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryPlayerPortalSessionsCommand command, CancellationToken token) =>
        Write(context, "query-player-portal-sessions", t => administration.SessionsAsync(command.GmSecret, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, KickPlayerPortalSessionsCommand command, CancellationToken token) =>
        Write(context, "kick-player-portal-sessions", async t => new PlayerSessionsKicked(await administration.KickAsync(command.GmSecret, command.AccountId, t).ConfigureAwait(false)), token);

    private ValueTask<HttpRouteReply> Write<T>(CommandExecutionContext context, string name, Func<CancellationToken, ValueTask<T>> action, CancellationToken token)
        where T : notnull
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Gm);
        return HttpOperationWork.ReplyAsync(context, async () => await HttpOperationWork.WriteAsync(units, context, name, action, token).ConfigureAwait(false));
    }
}

/// <summary>
/// The player's cera grant, shaped like the GM grant: a preparation unit finds who is asking (the identity every failure
/// audit is written under, SD-1a), then the execution unit judges everything again and credits (RQ-007 rests on it
/// alone). A refusal is audited in a unit of its own, because the refused unit rolls back; the reminder follows the commit.
/// </summary>
internal sealed class PlayerPortalCeraCommandHandler(IPlayerPortalAccessDomainService access, IPlayerPortalCeraDomainService cera,
    IGmCeraReminderDomainService reminders, ICommandWorkUnits units, ICommandFailureAudit failureAudit)
    : ICommandHandler<GrantPlayerCeraCommand, HttpRouteReply>
{
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, GrantPlayerCeraCommand command, CancellationToken token)
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Gm);
        return HttpOperationWork.ReplyAsync(context, async () => await GrantAsync(context, command, token).ConfigureAwait(false));
    }

    private async ValueTask<HttpRouteReply> GrantAsync(CommandExecutionContext c, GrantPlayerCeraCommand command, CancellationToken ct)
    {
        PlayerIdentity? identity = null;
        PlayerCeraGrantResult granted;
        try
        {
            identity = await HttpOperationWork.WriteAsync(units, c, "prepare-player-cera", t => access.AuthorizeAsync(command.Secret, t),
                ct, CommandFailureAuditRole.Preparation).ConfigureAwait(false);
            granted = await HttpOperationWork.WriteAsync(units, c, "grant-player-cera", t => cera.GrantAsync(command.Secret, command.Request, t),
                ct, CommandFailureAuditRole.Execution).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            if (c.Request!.Failure is not null)
            {
                if (identity is null) throw;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await failureAudit.RecordAsync(c, error, async (fact, t) => await cera.RecordFailureAsync(identity, command.Request.RequestId,
                    fact.Outcome.ToString(), fact.Outcome == CommandFailureAuditOutcome.Unknown ? "提交状态暂未确认" : "未提交：存储或规则处理失败", t),
                    timeout.Token).ConfigureAwait(false);
                throw;
            }
            if (!HttpOperationWork.BusinessFailure(error)) throw;
            var refusal = error as PlayerPortalException;
            // Nobody identified, or the identity itself refused: nothing to audit under (SD-1a).
            if (identity is not null && refusal?.Reason is not (PlayerPortalRefusal.PortalClosed or PlayerPortalRefusal.Unauthenticated))
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await HttpOperationWork.WriteAsync(units, c, "record-player-cera-failure", async t =>
                    {
                        await cera.RecordFailureAsync(identity, command.Request.RequestId, refusal is null ? "RolledBack" : "Rejected",
                            "未提交：" + (refusal?.Message ?? "存储或规则处理失败"), t).ConfigureAwait(false);
                        return true;
                    }, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception auditError) when (HttpOperationWork.BusinessFailure(auditError) && c.Request.Failure is null)
                { return new(Error: auditError, Outcome: "auditFailed"); }
            return new(Error: error, Outcome: refusal is null ? "rolledBack" : "rejected");
        }
        // After the catch: a reminder is never sent for a credit that did not commit. A failed reminder keeps the committed
        // grant as the value; the reply mapper answers it as a success and logs the error.
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return new(granted, Reminder: await reminders.RemindAsync([identity.AccountId], timeout.Token).ConfigureAwait(false));
        }
        catch (Exception error) when (HttpOperationWork.BusinessFailure(error) && c.Request!.Failure is null)
        { return new(granted, Error: error, Outcome: "reminderFailed", Reminder: "failed"); }
    }
}

/// <summary>
/// The player's mail, shaped like the cera grant: a preparation unit finds who is asking (the identity every failure
/// audit is written under, SD-1a), then the execution unit judges everything again and delivers (RQ-007 rests on it
/// alone). A refusal is audited in a unit of its own. The new-mail notice to an online recipient is the mailbox's own
/// domain event after the commit, as for the GM mail, so a replay, which delivers nothing, announces nothing.
/// </summary>
internal sealed class PlayerPortalMailCommandHandler(IPlayerPortalAccessDomainService access, IPlayerPortalMailDomainService mail,
    ICommandWorkUnits units, ICommandFailureAudit failureAudit) : ICommandHandler<SendPlayerMailCommand, HttpRouteReply>
{
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, SendPlayerMailCommand command, CancellationToken token)
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Gm);
        return HttpOperationWork.ReplyAsync(context, async () => await SendAsync(context, command, token).ConfigureAwait(false));
    }

    private async ValueTask<HttpRouteReply> SendAsync(CommandExecutionContext c, SendPlayerMailCommand command, CancellationToken ct)
    {
        PlayerIdentity? identity = null;
        var request = command.Request;
        try
        {
            identity = await HttpOperationWork.WriteAsync(units, c, "prepare-player-mail", t => access.AuthorizeAsync(command.Secret, t),
                ct, CommandFailureAuditRole.Preparation).ConfigureAwait(false);
            return new(await HttpOperationWork.WriteAsync(units, c, "send-player-mail", t => mail.SendAsync(command.Secret, request, t),
                ct, CommandFailureAuditRole.Execution).ConfigureAwait(false));
        }
        catch (Exception error)
        {
            if (c.Request!.Failure is not null)
            {
                if (identity is null) throw;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await failureAudit.RecordAsync(c, error, async (fact, t) => await mail.RecordFailureAsync(identity, request.RequestId, request.CharacterId,
                    fact.Outcome.ToString(), fact.Outcome == CommandFailureAuditOutcome.Unknown ? "提交状态暂未确认" : "未提交：存储或规则处理失败", t),
                    timeout.Token).ConfigureAwait(false);
                throw;
            }
            if (!HttpOperationWork.BusinessFailure(error)) throw;
            var refusal = error as PlayerPortalException;
            // Nobody identified, or the identity itself refused: nothing to audit under (SD-1a).
            if (identity is not null && refusal?.Reason is not (PlayerPortalRefusal.PortalClosed or PlayerPortalRefusal.Unauthenticated))
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await HttpOperationWork.WriteAsync(units, c, "record-player-mail-failure", async t =>
                    {
                        await mail.RecordFailureAsync(identity, request.RequestId, request.CharacterId, refusal is null ? "RolledBack" : "Rejected",
                            "未提交：" + (refusal?.Message ?? "存储或规则处理失败"), t).ConfigureAwait(false);
                        return true;
                    }, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception auditError) when (HttpOperationWork.BusinessFailure(auditError) && c.Request.Failure is null)
                { return new(Error: auditError, Outcome: "auditFailed"); }
            return new(Error: error, Outcome: refusal is null ? "rolledBack" : "rejected");
        }
    }
}

public static class PlayerPortalCommandRegistration
{
    /// <summary>Each player and player-portal administration route's CMD.</summary>
    public static IServiceCollection AddPlayerPortalCommands(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddInternalCommandHandler<LoginPlayerPortalCommand, HttpRouteReply, PlayerPortalCommandHandler>();
        services.AddInternalCommandHandler<LogoutPlayerPortalCommand, HttpRouteReply, PlayerPortalCommandHandler>();
        services.AddInternalCommandHandler<QueryPlayerMeCommand, HttpRouteReply, PlayerPortalCommandHandler>();
        services.AddInternalCommandHandler<QueryPlayerOperationsCommand, HttpRouteReply, PlayerPortalCommandHandler>();
        services.AddInternalCommandHandler<GrantPlayerCeraCommand, HttpRouteReply, PlayerPortalCeraCommandHandler>(failureAudit: CommandFailureAuditCapability.AuditedFailure);
        services.AddInternalCommandHandler<SendPlayerMailCommand, HttpRouteReply, PlayerPortalMailCommandHandler>(failureAudit: CommandFailureAuditCapability.AuditedFailure);
        services.AddInternalCommandHandler<QueryPlayerPortalSettingsCommand, HttpRouteReply, PlayerPortalCommandHandler>();
        services.AddInternalCommandHandler<ChangePlayerPortalSettingsCommand, HttpRouteReply, PlayerPortalCommandHandler>();
        services.AddInternalCommandHandler<QueryPlayerPortalSessionsCommand, HttpRouteReply, PlayerPortalCommandHandler>();
        services.AddInternalCommandHandler<KickPlayerPortalSessionsCommand, HttpRouteReply, PlayerPortalCommandHandler>();
        return services;
    }
}
