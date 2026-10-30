using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemLifecycleDomainService : IItemLifecycleDomainService
{
    private readonly IItemDomainStore store;
    private readonly IDomainTransactionAccessor transactions;
    private readonly IItemPlacementRuleService placements;
    private readonly IItemExpirationRuleService? expiration;
    private readonly TimeProvider clock;

    public ItemLifecycleDomainService(IItemDomainStore store, IDomainTransactionAccessor transactions,
        IItemPlacementRuleService placements, IItemExpirationRuleService expiration, TimeProvider clock)
        : this(store, transactions, placements, clock, expiration) { }

    private ItemLifecycleDomainService(IItemDomainStore store, IDomainTransactionAccessor transactions,
        IItemPlacementRuleService placements, TimeProvider clock, IItemExpirationRuleService? expiration) =>
        (this.store, this.transactions, this.placements, this.expiration, this.clock) = (store, transactions, placements, expiration, clock);

    /// <summary>
    /// The lifecycle of a run-scoped memory container: its items carry no lifetime, so a template that
    /// would expire is refused rather than given an expiry the container will never enforce.
    /// </summary>
    internal static ItemLifecycleDomainService WithoutLifetimes(IItemDomainStore store,
        IDomainTransactionAccessor transactions, IItemPlacementRuleService placements) =>
        new(store, transactions, placements, TimeProvider.System, null);

    private IDomainTransaction Transaction => transactions.Current
        ?? throw new InvalidOperationException("Item lifecycle changes require an open domain transaction.");

    public async ValueTask<ItemSnapshot> PrepareNewAsync(ItemSnapshot snapshot, CancellationToken ct, DateTimeOffset? at = null)
    {
        if (snapshot is not { LegacyKind: 0, ItemId: 1, Type: ItemTraits.None, IsPile: true })
        {
            var placement = await placements.PlacementForAsync(snapshot.ItemId, ct).ConfigureAwait(false);
            if (placement is null || snapshot.LegacyKind != placement.ItemKind || snapshot.Type != placement.Traits
                || snapshot.IsPile != (placement.WorldClass == ItemWorldClass.Interchangeable))
                throw new InvalidDataException("Item snapshot does not match a known template.");
            if (expiration is not null) snapshot = expiration.Prepare(snapshot, placement.Lifetime, at ?? clock.GetUtcNow());
            else if (placement.Lifetime.IsLimited && snapshot.Lifetime.IsPermanent)
                throw new InvalidOperationException("Limited item creation requires expiration rules.");
        }
        return snapshot;
    }
    public async ValueTask<ItemRecord> CreateAsync(ItemSnapshot snapshot, CancellationToken ct)
    {
        snapshot = await PrepareNewAsync(snapshot, ct).ConfigureAwait(false);
        return await store.SessionIn(Transaction).CreateAsync(snapshot, ct).ConfigureAwait(false);
    }
    public ValueTask DeleteItemAsync(ItemRecord record, CancellationToken ct) =>
        ItemLifecycleScope.In(Transaction).DeleteItemAsync(record, ct);
    public ValueTask<(ItemRecord Taken, ItemRecord Remainder)> SplitAsync(ItemRecord record, int count, CancellationToken ct) =>
        ItemLifecycleScope.In(Transaction).SplitAsync(record, count, ct);
}
