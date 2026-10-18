namespace Dfo.Server.Data.Administration;

[Flags]
public enum GmPermission { None = 0, QueryPlayers = 1, SendMail = 2, GrantCera = 4, ViewAudit = 8, All = 15 }
public sealed record GmAdministrator(long Id, string LoginName, string PasswordRecord, bool Enabled, GmPermission Permissions);
public sealed record GmIdentity(long Id, string LoginName, GmPermission Permissions, DateTimeOffset ExpiresAt);
public sealed record GmSession(string TokenHash, long AdministratorId, DateTimeOffset ExpiresAt);
public sealed record GmLoginResult(GmIdentity? Identity, string? SessionSecret);
public sealed record GmAuditRecord(string OperationId, DateTimeOffset OccurredAt, string Action,
    long? AdministratorId, string? AdministratorName, string Target, string Outcome, string Reason);
public sealed record GmAuditPage(IReadOnlyList<GmAuditRecord> Items, long Total, int Page, int PageSize);
/// <summary>Audit query filter; <see cref="Outcome"/> is one stored outcome or <see cref="Attention"/> (everything not succeeded).</summary>
public sealed record GmAuditFilter(string? Action = null, string? Outcome = null)
{
    public const string Attention = "attention";
    public static GmAuditFilter All { get; } = new();
    public static IReadOnlySet<string> Actions { get; } = new HashSet<string>(StringComparer.Ordinal) { "ChangeDailyRefill", "ChangeEvent", "ChangeJoustParameters", "EditMailTemplate",
        "GrantCera", "Login", "Logout", "SaveInitialization", "SaveMailDraft", "SendOfficialMail", "SetJoustChampion" };
    public static IReadOnlySet<string> Outcomes { get; } = new HashSet<string>(StringComparer.Ordinal) { Attention, "Succeeded", "Rejected", "RolledBack", "Unknown" };
    public bool IsValid => (Action is null || Actions.Contains(Action)) && (Outcome is null || Outcomes.Contains(Outcome));
}
