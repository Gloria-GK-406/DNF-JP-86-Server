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
