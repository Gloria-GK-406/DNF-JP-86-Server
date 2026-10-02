using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Abstractions.Administration;

public interface IGmCeraPersistence
{
    ValueTask AcquireWriteAsync(CancellationToken token);
    ValueTask<GmStoredPage<GmCeraAccountIdentity>> AccountsAsync(GmPlayerFilter filter, CancellationToken token);
    ValueTask<GmCeraOperation?> OperationAsync(long administrator, Guid id, CancellationToken token);
    ValueTask<GmPage<GmCeraReceipt>> OperationsAsync(long administrator, int page, int size, CancellationToken token);
    ValueTask SaveOperationAsync(long administrator, GmCeraOperation operation, CancellationToken token);
}
public interface IGmCeraRuleService
{
    GmCeraInput Normalize(GmCeraInput input);
    string PayloadHash(GmCeraInput input);
    GmCeraDecision Decide(AccountCeraBalances before, GmCeraInput input);
}
public interface IAccountWalletDomainService
{
    ValueTask<AccountCeraBalances?> ReadAsync(AccountId account, CancellationToken token);
}
public sealed record AccountWalletChanged(AccountId Account) : INotification<AccountWalletChanged>;
public interface IGmCeraReminderDomainService
{
    ValueTask<string> RemindAsync(IReadOnlyList<int> accounts, CancellationToken token);
}
public interface IGmCeraDomainService
{
    ValueTask<GmCeraQueryResult> QueryAsync(string secret, GmCeraQuery kind, GmPlayerFilter filter, Guid id, GmCeraInput? input, CancellationToken token);
    ValueTask<GmCeraGrantResult> GrantAsync(string secret, Guid id, GmCeraInput input, CancellationToken token);
    ValueTask<bool> RecordFailureAsync(string secret, Guid id, string outcome, string reason, CancellationToken token);
}
