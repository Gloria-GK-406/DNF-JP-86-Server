using Dfo.Server.Data.Administration;

namespace Dfo.Server.Abstractions.Administration;

public interface IGmAdministrationPersistence
{
    ValueTask AcquireWriteIntentAsync(CancellationToken cancellationToken);
    ValueTask<bool> HasAdministratorAsync(CancellationToken cancellationToken);
    ValueTask<GmAdministrator?> FindAdministratorAsync(string loginName, CancellationToken cancellationToken);
    ValueTask<GmAdministrator?> FindAdministratorAsync(long id, CancellationToken cancellationToken);
    ValueTask CreateAdministratorAsync(string loginName, string passwordRecord, GmPermission permissions, CancellationToken cancellationToken);
    ValueTask SaveSessionAsync(GmSession session, CancellationToken cancellationToken);
    ValueTask<GmSession?> FindSessionAsync(string hash, CancellationToken cancellationToken);
    ValueTask DeleteSessionAsync(string hash, CancellationToken cancellationToken);
    ValueTask AppendAuditAsync(GmAuditRecord record, CancellationToken cancellationToken);
    ValueTask<GmAuditPage> QueryAuditAsync(int page, int pageSize, GmAuditFilter filter, CancellationToken cancellationToken);
}

/// <summary>The GM credential half that runs off the executor (RQ-007): the administrator read and every hash or verification.</summary>
public interface IGmCredentialReader
{
    ValueTask<GmCredentialCheck> CheckLoginAsync(string loginName, string password, CancellationToken cancellationToken);
    /// <summary>The bootstrap password's record, or null when its length is outside 12..256.</summary>
    ValueTask<string?> PrepareBootstrapAsync(string password, CancellationToken cancellationToken);
}

public interface IGmAccessDomainService
{
    ValueTask<bool> IsInitializedAsync(CancellationToken cancellationToken);
    ValueTask<bool> BootstrapAsync(string loginName, string? passwordRecord, GmPermission permissions, CancellationToken cancellationToken);
    ValueTask<GmLoginResult> LoginAsync(GmCredentialCheck check, CancellationToken cancellationToken);
    ValueTask<GmIdentity?> IdentifyAsync(string? sessionSecret, CancellationToken cancellationToken);
    ValueTask<bool> LogoutAsync(string sessionSecret, CancellationToken cancellationToken);
}

public interface IGmAuditDomainService
{
    ValueTask AppendAsync(GmAuditRecord record, CancellationToken cancellationToken);
    ValueTask<GmAuditPage> QueryAsync(int page, int pageSize, GmAuditFilter filter, CancellationToken cancellationToken);
}
