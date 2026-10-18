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

public interface IGmAccessDomainService
{
    ValueTask<bool> IsInitializedAsync(CancellationToken cancellationToken);
    ValueTask<bool> BootstrapAsync(string loginName, string password, GmPermission permissions, CancellationToken cancellationToken);
    ValueTask<GmLoginResult> LoginAsync(string loginName, string password, CancellationToken cancellationToken);
    ValueTask<GmIdentity?> IdentifyAsync(string? sessionSecret, CancellationToken cancellationToken);
    ValueTask<bool> LogoutAsync(string sessionSecret, CancellationToken cancellationToken);
}

public interface IGmAuditDomainService
{
    ValueTask AppendAsync(GmAuditRecord record, CancellationToken cancellationToken);
    ValueTask<GmAuditPage> QueryAsync(int page, int pageSize, GmAuditFilter filter, CancellationToken cancellationToken);
}
