using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Creatures;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Creatures;

internal static class TestCreatureCollection
{
    public static ICreatureCollection Create(CharacterId owner, IDomainTransaction tx, ICreatureStore store,
        Action<CreatureState> write, ICreatureRuleService rules, TimeProvider? clock = null) =>
        new CreatureCollection(() => new CreatureInstanceContext(owner, tx,
            new Session(store, write), store, rules, clock ?? TimeProvider.System));

    private sealed class Session(ICreatureStore store, Action<CreatureState> write) : ICreatureDomainStoreSession
    {
        public ValueTask<CreatureState?> ReadAsync(CharacterId owner, int uid, CancellationToken ct) => store.FindAsync(uid, ct);
        public ValueTask<IReadOnlyList<CreatureState>> ReadAllAsync(CharacterId owner, CancellationToken ct) => store.ReadAllAsync(ct);
        public ValueTask<CreatureState?> ReadCurrentAsync(CharacterId owner, CancellationToken ct) => store.CurrentAsync(ct);
        public async ValueTask<ItemReference?> FindItemAsync(CharacterId owner, int uid, CancellationToken ct)
        {
            foreach (var space in new[] { BusinessInventorySpace.CreatureInventory, BusinessInventorySpace.WornCreature })
                if (store.FindContainer(space) is { } container)
                    foreach (var item in await container.GetAllAsync(ct))
                        if (item.ItemUid?.Value == uid) return item;
            return null;
        }
        public async ValueTask<ItemReference> ReadItemAsync(ItemReference reference, CancellationToken ct)
        {
            var container = store.GetContainer(reference.BusinessSlot!.Value.Space);
            var current = await container.GetAsync(reference.SlotIndex, ct);
            if (current is null || !current.SameOccupancyAs(reference)) throw new InvalidOperationException("Stale creature reference.");
            return current;
        }
        public async ValueTask<ItemReference> WriteAsync(ItemReference reference, CreatureState state, int? expiration, CancellationToken ct)
        {
            _ = await ReadItemAsync(reference, ct);
            var updated = await store.GetContainer(reference.BusinessSlot!.Value.Space).UpdateAsync(reference,
                x => x with { ItemId = state.ItemTemplateId, CreatureDetail = state.ToDetail(), ExpireTime = expiration ?? x.ExpireTime }, ct);
            write(state);
            return updated;
        }
        public void ReviveIfEmpty(CharacterId owner, int uid) => throw new NotSupportedException();
    }
}
