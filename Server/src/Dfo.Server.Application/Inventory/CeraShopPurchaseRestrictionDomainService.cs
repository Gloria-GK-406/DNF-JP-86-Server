using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.Inventory;

internal sealed class CeraShopPurchaseRestrictionDomainService(IAccountPurchaseRestrictionTable restrictions,
    ICharacterBuyRestrictTable legacy, ICharacterTable roster) : ICeraShopPurchaseRestrictionDomainService
{
    // ClientStatic: NOTI 0x0344 treats this timestamp as a permanent restriction.
    internal const int PermanentEnd = 2147439600;

    public async ValueTask<CeraShopRestrictionSnapshot> ReadAsync(AccountId accountId, DateTimeOffset now, CancellationToken ct)
    {
        var characters = await roster.ReadByAccountAsync(accountId, ct).ConfigureAwait(false);
        var slots = characters.ToDictionary(c => c.CharacterId, c => c.SlotIndex);
        var old = await legacy.ReadByCharactersAsync(slots.Keys.ToArray(), ct).ConfigureAwait(false);
        var entries = AccountRelationCompatibilityEntryProjection.ResolveAccountCompatibilityEntries(
            old.Select(r => (slots[r.CharacterId], r.SortOrder, r.EntryId, r.SentinelOrValue, r.Flag)).ToArray(),
            characters.Select(c => (c.CharacterId, c.SlotIndex, c.Level)).ToArray()).ToDictionary(r => r.EntryId);
        var stored = await restrictions.ReadAsync(accountId, ct).ConfigureAwait(false);
        foreach (var row in stored)
        {
            if (row.RestrictionId <= 0 || row.EndsAtUnixSeconds <= 0 || row.EndsAtUnixSeconds > PermanentEnd)
                throw new InvalidDataException("Invalid persisted CERA purchase restriction.");
            entries[row.RestrictionId] = new(row.RestrictionId, row.EndsAtUnixSeconds,
                row.EndsAtUnixSeconds == PermanentEnd || row.EndsAtUnixSeconds > now.ToUnixTimeSeconds() ? 0 : 1);
        }
        return new(entries.Values.OrderBy(r => r.EntryId).ToArray(), stored);
    }
}
