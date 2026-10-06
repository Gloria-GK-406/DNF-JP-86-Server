using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemLifecycleDomainService(IItemDomainStore store,
    IDomainTransactionAccessor transactions, IItemPlacementRuleService placements) : IItemLifecycleDomainService
{
    private IDomainTransaction Transaction => transactions.Current
        ?? throw new InvalidOperationException("Item lifecycle changes require an open domain transaction.");

    public async ValueTask<ItemRecord> CreateAsync(ItemSnapshot snapshot, CancellationToken ct)
    {
        if (snapshot is not { LegacyKind: 0, ItemId: 1, Type: ItemTraits.None, IsPile: true })
        {
            var placement = await placements.PlacementForAsync(snapshot.ItemId, ct).ConfigureAwait(false);
            if (placement is null || snapshot.LegacyKind != placement.ItemKind || snapshot.Type != placement.Traits
                || snapshot.IsPile != (placement.WorldClass == ItemWorldClass.Interchangeable))
                throw new InvalidDataException("Item snapshot does not match a known template.");
        }
        return await store.SessionIn(Transaction).CreateAsync(snapshot, ct).ConfigureAwait(false);
    }
    public ValueTask DeleteItemAsync(ItemRecord record, CancellationToken ct) =>
        ItemLifecycleScope.In(Transaction).DeleteItemAsync(record, ct);
    public ValueTask<(ItemRecord Taken, ItemRecord Remainder)> SplitAsync(ItemRecord record, int count, CancellationToken ct) =>
        ItemLifecycleScope.In(Transaction).SplitAsync(record, count, ct);
}
