namespace Dfo.Server.HttpApi;

/// <summary>
/// A launcher or GM route's identity at the HTTP boundary: it selects the route's response mapping.
/// The gameplay side never sees it; each route sends its own named CMD.
/// </summary>
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
