using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record CeraShopRestrictionSnapshot(IReadOnlyList<AccountCompatibilityEntryState> Entries,
    IReadOnlyList<AccountPurchaseRestrictionRow> Stored);

public interface ICeraShopPurchaseRestrictionDomainService
{
    ValueTask<CeraShopRestrictionSnapshot> ReadAsync(AccountId accountId, DateTimeOffset now, CancellationToken ct);
}
