using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Abstractions.Persistence;

public sealed record AccountPurchaseRestrictionRow(int RestrictionId, int EndsAtUnixSeconds);

public interface IAccountPurchaseRestrictionTable
{
    ValueTask<IReadOnlyList<AccountPurchaseRestrictionRow>> ReadAsync(AccountId accountId, CancellationToken ct);
    ValueTask<bool> TrySetAsync(AccountId accountId, int restrictionId, int? expectedEnd, int end, CancellationToken ct);
}
