using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Initialization;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Commands;

public enum HttpRouteOperation
{
    Register, Login, GmLogin, Logout, Audit,
    Overview, Accounts, Characters, Account, Character,
    CeraAccounts, CeraOperations, CeraOperation, CeraPreview, CeraGrant,
    MailDraft, MailTemplates, MailRecipients, MailOperations, MailOperation, MailPreview,
    MailSaveDraft, MailCreateTemplate, MailUpdateTemplate, MailDeleteTemplate, MailSend,
    Initialization, InitializationAccount, InitializationCharacter, InitializationItems, InitializationItem,
    DailyRefill, DailyRefillChange, DailyRefillItems, DailyRefillItem,
    Events, WeeklyAttendance, JoustSettings, JoustRound, JoustChampion, JoustChange, EventsChange
}

/// <summary>Transport facts only. No HTTP context, service or executable continuation crosses the queue.</summary>
public abstract record HttpRouteInput
{
    private HttpRouteInput() { }
    public sealed override string ToString() => "HttpRouteInput { }";
    public sealed record Empty : HttpRouteInput;
    public sealed record Failed(Exception Cause) : HttpRouteInput;
    public sealed record Registration(PreparedRegistration Value) : HttpRouteInput;
    public sealed record Authentication(VerifiedLogin Value) : HttpRouteInput;
    public sealed record GmAuthentication(GmCredentialCheck Value) : HttpRouteInput;
    public sealed record Session(string Secret) : HttpRouteInput;
    public sealed record AuditQuery(int Page, int PageSize, GmAuditFilter Filter) : HttpRouteInput;
    public sealed record Players(GmPlayerFilter Filter) : HttpRouteInput;
    public sealed record Player(int Id) : HttpRouteInput;
    public sealed record Cera(string Secret, GmCeraQuery Kind, GmPlayerFilter Filter, Guid Id = default, GmCeraInput? Input = null) : HttpRouteInput;
    public sealed record CeraGrantRequest(string Secret, GmCeraRequest Value) : HttpRouteInput;
    public sealed record Mail(string Secret, GmMailQuery Kind, GmPlayerFilter Filter, Guid Id = default, GmMailInput? Input = null) : HttpRouteInput;
    public sealed record MailEditRequest(string Secret, GmMailEdit Kind, Guid Id, GmMailEditorRequest Value) : HttpRouteInput;
    public sealed record MailSendRequest(string Secret, GmMailEditorRequest Value) : HttpRouteInput;
    public sealed record AccountInitializationRequest(string Secret, SaveInitializationRequest<AccountInitialization> Value) : HttpRouteInput;
    public sealed record CharacterInitializationRequest(string Secret, SaveInitializationRequest<CharacterInitialization> Value) : HttpRouteInput;
    public sealed record Catalog(string Secret, ItemCatalogFilter Filter) : HttpRouteInput;
    public sealed record CatalogItem(string Secret, int Id) : HttpRouteInput;
    public sealed record DailyChange(string Secret, DailyRefillChange Value) : HttpRouteInput;
    public sealed record EventChangeRequest(string Secret, EventChange Value) : HttpRouteInput;
    public sealed record JoustChangeRequest(string Secret, JoustSettingsChange Value) : HttpRouteInput;
    public sealed record JoustChampionRequest(string Secret, JoustChampionChange Value) : HttpRouteInput;
}

public sealed record HttpRouteCommand(HttpRouteOperation Operation, HttpRouteInput Input) : ICommand<HttpRouteReply>
{ public override string ToString() => $"HttpRouteCommand {{ Operation = {Operation} }}"; }
public sealed record HttpRouteReply(object? Value = null, Exception? Error = null, string? Outcome = null, string? Reminder = null, bool ExecutorFailed = false);

internal sealed partial class HttpRouteCommandHandler(
    IAuthenticationDomainService authentication, IGmAccessDomainService access, IGmAuditDomainService audit,
    IGmPlayerDirectoryDomainService players, IGmCeraDomainService cera, IGmCeraReminderDomainService reminders,
    IGmOfficialMailDomainService mail, IGmInitializationDomainService initialization, IGmDailyRefillDomainService daily,
    IGmEventsDomainService events, IGmJoustDomainService joust, ICommandWorkUnits units, ICommandFailureAudit failureAudit)
    : ICommandHandler<HttpRouteCommand, HttpRouteReply>
{
    public async ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, HttpRouteCommand command, CancellationToken token)
    {
        Validate(context, command);
        if (command.Input is HttpRouteInput.Failed failed)
        {
            if (failed.Cause is OperationCanceledException) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failed.Cause);
            return new(Error: failed.Cause, Outcome: IsGrant(command.Operation)
                ? Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(failed.Cause) ? "unknown" : "rejected" : null);
        }
        return await ExecuteAsync(context, command, token).ConfigureAwait(false);
    }
    private static void Validate(CommandExecutionContext context, HttpRouteCommand command)
    {
        if (!Enum.IsDefined(command.Operation) || context.CommandType != typeof(HttpRouteCommand)
            || context.Request is not CommandRequestContext.TrustedInternal request
            || request.Origin.Kind != (command.Operation is HttpRouteOperation.Register or HttpRouteOperation.Login
                ? Dfo.Server.Abstractions.Execution.GameplayCallSource.Http : Dfo.Server.Abstractions.Execution.GameplayCallSource.Gm)
            || request.Origin.SourceConnection is not null)
            throw new InvalidOperationException("The HTTP command requires its exact registered operation and trusted origin.");
        request.DemandCurrent();
        var valid = command.Input is HttpRouteInput.Failed || (command.Operation, command.Input) switch
        {
            (HttpRouteOperation.Register, HttpRouteInput.Registration) or (HttpRouteOperation.Login, HttpRouteInput.Authentication)
                or (HttpRouteOperation.GmLogin, HttpRouteInput.GmAuthentication) or (HttpRouteOperation.Audit, HttpRouteInput.AuditQuery)
                or (HttpRouteOperation.Overview, HttpRouteInput.Empty)
                or (HttpRouteOperation.Accounts or HttpRouteOperation.Characters, HttpRouteInput.Players)
                or (HttpRouteOperation.Account or HttpRouteOperation.Character, HttpRouteInput.Player)
                or (HttpRouteOperation.Logout or HttpRouteOperation.Initialization or HttpRouteOperation.DailyRefill or HttpRouteOperation.Events
                    or HttpRouteOperation.WeeklyAttendance or HttpRouteOperation.JoustSettings or HttpRouteOperation.JoustRound, HttpRouteInput.Session)
                or (HttpRouteOperation.CeraGrant, HttpRouteInput.CeraGrantRequest)
                or (HttpRouteOperation.MailSend, HttpRouteInput.MailSendRequest)
                or (HttpRouteOperation.InitializationAccount, HttpRouteInput.AccountInitializationRequest)
                or (HttpRouteOperation.InitializationCharacter, HttpRouteInput.CharacterInitializationRequest)
                or (HttpRouteOperation.InitializationItems or HttpRouteOperation.DailyRefillItems, HttpRouteInput.Catalog)
                or (HttpRouteOperation.InitializationItem or HttpRouteOperation.DailyRefillItem, HttpRouteInput.CatalogItem)
                or (HttpRouteOperation.DailyRefillChange, HttpRouteInput.DailyChange)
                or (HttpRouteOperation.EventsChange, HttpRouteInput.EventChangeRequest)
                or (HttpRouteOperation.JoustChange, HttpRouteInput.JoustChangeRequest)
                or (HttpRouteOperation.JoustChampion, HttpRouteInput.JoustChampionRequest) => true,
            (HttpRouteOperation.CeraAccounts, HttpRouteInput.Cera { Kind: GmCeraQuery.Accounts })
                or (HttpRouteOperation.CeraOperations, HttpRouteInput.Cera { Kind: GmCeraQuery.Operations })
                or (HttpRouteOperation.CeraOperation, HttpRouteInput.Cera { Kind: GmCeraQuery.Operation })
                or (HttpRouteOperation.CeraPreview, HttpRouteInput.Cera { Kind: GmCeraQuery.Preview })
                or (HttpRouteOperation.MailDraft, HttpRouteInput.Mail { Kind: GmMailQuery.Draft })
                or (HttpRouteOperation.MailTemplates, HttpRouteInput.Mail { Kind: GmMailQuery.Templates })
                or (HttpRouteOperation.MailRecipients, HttpRouteInput.Mail { Kind: GmMailQuery.Recipients })
                or (HttpRouteOperation.MailOperations, HttpRouteInput.Mail { Kind: GmMailQuery.Operations })
                or (HttpRouteOperation.MailOperation, HttpRouteInput.Mail { Kind: GmMailQuery.Operation })
                or (HttpRouteOperation.MailPreview, HttpRouteInput.Mail { Kind: GmMailQuery.Preview })
                or (HttpRouteOperation.MailSaveDraft, HttpRouteInput.MailEditRequest { Kind: GmMailEdit.Draft })
                or (HttpRouteOperation.MailCreateTemplate, HttpRouteInput.MailEditRequest { Kind: GmMailEdit.CreateTemplate })
                or (HttpRouteOperation.MailUpdateTemplate, HttpRouteInput.MailEditRequest { Kind: GmMailEdit.UpdateTemplate })
                or (HttpRouteOperation.MailDeleteTemplate, HttpRouteInput.MailEditRequest { Kind: GmMailEdit.DeleteTemplate }) => true,
            _ => false
        };
        if (!valid) throw new InvalidOperationException("The HTTP operation and its transport input do not match.");
    }
    private static bool IsGrant(HttpRouteOperation operation) => operation is HttpRouteOperation.CeraGrant or HttpRouteOperation.MailSend;
}
