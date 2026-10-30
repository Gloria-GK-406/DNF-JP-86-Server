using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Commands;

internal sealed partial class HttpRouteCommandHandler
{
    private async ValueTask<HttpRouteReply> ExecuteAsync(CommandExecutionContext c, HttpRouteCommand q, CancellationToken ct)
    {
        try
        {
            object value = q.Operation switch
            {
                HttpRouteOperation.Register when q.Input is HttpRouteInput.Registration p => await Write(c, "register-account", t => authentication.RegisterAsync(p.Value, t), ct),
                HttpRouteOperation.Login when q.Input is HttpRouteInput.Authentication p => await Write(c, "authenticate-and-issue-ticket", t => authentication.AuthenticateAndIssueTicketAsync(p.Value, t), ct),
                HttpRouteOperation.GmLogin when q.Input is HttpRouteInput.GmAuthentication p => await Write(c, "login-gm-administrator", t => access.LoginAsync(p.Value, t), ct),
                HttpRouteOperation.Logout when q.Input is HttpRouteInput.Session p => await Write(c, "logout-gm-administrator", t => access.LogoutAsync(p.Secret, t), ct),
                HttpRouteOperation.Audit when q.Input is HttpRouteInput.AuditQuery p => await Write(c, "query-gm-audit", t => audit.QueryAsync(p.Page, p.PageSize, p.Filter, t), ct),
                HttpRouteOperation.Overview when q.Input is HttpRouteInput.Empty => await Write(c, "query-gm-overview", players.OverviewAsync, ct),
                HttpRouteOperation.Accounts when q.Input is HttpRouteInput.Players p => await Write(c, "query-gm-accounts", t => players.AccountsAsync(p.Filter, t), ct),
                HttpRouteOperation.Characters when q.Input is HttpRouteInput.Players p => await Write(c, "query-gm-characters", t => players.CharactersAsync(p.Filter, t), ct),
                HttpRouteOperation.Account when q.Input is HttpRouteInput.Player p => await Write(c, "find-gm-account", t => players.AccountAsync(p.Id, t), ct),
                HttpRouteOperation.Character when q.Input is HttpRouteInput.Player p => await Write(c, "find-gm-character", t => players.CharacterAsync(p.Id, t), ct),
                HttpRouteOperation.CeraAccounts or HttpRouteOperation.CeraOperations or HttpRouteOperation.CeraOperation or HttpRouteOperation.CeraPreview when q.Input is HttpRouteInput.Cera p =>
                    (await Write(c, "query-gm-cera", t => cera.QueryAsync(p.Secret, p.Kind, p.Filter, p.Id, p.Input, t), ct)).Value,
                HttpRouteOperation.MailDraft or HttpRouteOperation.MailTemplates or HttpRouteOperation.MailRecipients or HttpRouteOperation.MailOperations or HttpRouteOperation.MailOperation or HttpRouteOperation.MailPreview when q.Input is HttpRouteInput.Mail p =>
                    (await Write(c, "query-gm-mail", t => mail.QueryAsync(p.Secret, p.Kind, p.Filter, p.Id, p.Input, t), ct)).Value,
                HttpRouteOperation.MailSaveDraft or HttpRouteOperation.MailCreateTemplate or HttpRouteOperation.MailUpdateTemplate or HttpRouteOperation.MailDeleteTemplate when q.Input is HttpRouteInput.MailEditRequest p =>
                    await Write(c, "edit-gm-mail", t => mail.EditAsync(p.Secret, p.Kind, p.Id, p.Value, t), ct),
                HttpRouteOperation.Initialization when q.Input is HttpRouteInput.Session p => await Write(c, "query-gm-initialization", t => initialization.ReadAsync(p.Secret, t), ct),
                HttpRouteOperation.InitializationAccount when q.Input is HttpRouteInput.AccountInitializationRequest p => await Write(c, "save-gm-account-initialization", t => initialization.SaveAsync(p.Secret, p.Value, null, t), ct),
                HttpRouteOperation.InitializationCharacter when q.Input is HttpRouteInput.CharacterInitializationRequest p => await Write(c, "save-gm-character-initialization", t => initialization.SaveAsync(p.Secret, null, p.Value, t), ct),
                HttpRouteOperation.InitializationItems when q.Input is HttpRouteInput.Catalog p => await Write(c, "search-gm-initialization-items", t => initialization.SearchAsync(p.Secret, p.Filter, t), ct),
                HttpRouteOperation.InitializationItem when q.Input is HttpRouteInput.CatalogItem p => await Write(c, "read-gm-initialization-item", t => initialization.ItemAsync(p.Secret, p.Id, t), ct),
                HttpRouteOperation.DailyRefill when q.Input is HttpRouteInput.Session p => await Write(c, "query-gm-daily-refill", t => daily.ListAsync(p.Secret, t), ct),
                HttpRouteOperation.DailyRefillChange when q.Input is HttpRouteInput.DailyChange p => await Write(c, "change-gm-daily-refill", t => daily.ChangeAsync(p.Secret, p.Value, t), ct),
                HttpRouteOperation.DailyRefillItems when q.Input is HttpRouteInput.Catalog p => await Write(c, "search-gm-daily-refill-items", t => daily.SearchAsync(p.Secret, p.Filter, t), ct),
                HttpRouteOperation.DailyRefillItem when q.Input is HttpRouteInput.CatalogItem p => await Write(c, "read-gm-daily-refill-item", t => daily.ItemAsync(p.Secret, p.Id, t), ct),
                HttpRouteOperation.Events when q.Input is HttpRouteInput.Session p => await Read(c, "query-gm-events", t => events.ListAsync(p.Secret, t), ct),
                HttpRouteOperation.WeeklyAttendance when q.Input is HttpRouteInput.Session p => await Read(c, "query-gm-weekly-attendance-settings", t => events.WeeklyAttendanceSettingsAsync(p.Secret, t), ct),
                HttpRouteOperation.JoustSettings when q.Input is HttpRouteInput.Session p => await Read(c, "query-gm-joust-settings", t => joust.SettingsAsync(p.Secret, t), ct),
                HttpRouteOperation.JoustRound when q.Input is HttpRouteInput.Session p => await Read(c, "query-gm-joust-current-round", t => joust.CurrentRoundAsync(p.Secret, t), ct),
                HttpRouteOperation.JoustChange when q.Input is HttpRouteInput.JoustChangeRequest p => await Write(c, "change-gm-joust-settings", t => joust.SaveAsync(p.Secret, p.Value, t), ct),
                HttpRouteOperation.JoustChampion when q.Input is HttpRouteInput.JoustChampionRequest p => await Write(c, "change-gm-joust-champion", t => joust.SetChampionAsync(p.Secret, p.Value, t), ct),
                HttpRouteOperation.EventsChange when q.Input is HttpRouteInput.EventChangeRequest p => await Write(c, "change-gm-events", t => events.ChangeAsync(p.Secret, p.Value, t), ct),
                HttpRouteOperation.CeraGrant when q.Input is HttpRouteInput.CeraGrantRequest p => await GrantCera(c, q, p, ct),
                HttpRouteOperation.MailSend when q.Input is HttpRouteInput.MailSendRequest p => await SendMail(c, q, p, ct),
                _ => throw new InvalidOperationException("The HTTP operation and its transport input do not match."),
            };
            return value is HttpRouteReply reply ? reply : new(value);
        }
        catch (Exception error) when (BusinessFailure(error) && c.Request!.Failure is null
            && q.Operation is not (HttpRouteOperation.Register or HttpRouteOperation.Login or HttpRouteOperation.GmLogin
                or HttpRouteOperation.Logout or HttpRouteOperation.Audit))
        { return new(Error: error); }
    }

    private static bool BusinessFailure(Exception error) => error is not OperationCanceledException
        && !TransactionOutcomeUncertainException.Contains(error);
    private sealed record Stage<T>(T? Value, Exception? Error) where T : notnull;
    private async ValueTask<T> Write<T>(CommandExecutionContext context, string name,
        Func<CancellationToken, ValueTask<T>> action, CancellationToken ct,
        CommandFailureAuditRole audit = CommandFailureAuditRole.None) where T : notnull =>
        Unwrap(await units.WriteAsync(context, new(name, [], FailureAudit: audit), (unit, token) => Action(context, unit, action, token), ct));
    private async ValueTask<T> Read<T>(CommandExecutionContext context, string name,
        Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) where T : notnull =>
        Unwrap(await units.ReadOnlyAsync(context, new(name, []), (unit, token) => Action(context, unit, action, token), ct));
    private static async ValueTask<Stage<T>> Action<T>(CommandExecutionContext context, CommandWorkUnit unit,
        Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) where T : notnull
    {
        try { return new(await action(ct), null); }
        catch (Exception error) when (BusinessFailure(error) && context.Request!.Failure is null && unit.TransactionIsOpen)
        { unit.RequireRollback(error); return new(default, error); }
    }
    private static T Unwrap<T>(Stage<T> result) where T : notnull
    {
        if (result.Error is { } error) ExceptionDispatchInfo.Throw(error);
        return result.Value!;
    }
}
