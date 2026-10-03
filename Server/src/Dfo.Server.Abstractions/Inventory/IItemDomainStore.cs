using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public interface IItemDomainStore : IDomainStore<IItemDomainStoreSession>;

public interface IItemDomainStoreSession
{
    ValueTask<ItemReference?> GetAsync(ContainerId container, int slot, CancellationToken ct);
    ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(ContainerId container, CancellationToken ct);
    ValueTask<ItemRecord> TakeAsync(ItemReference reference, int? count, CancellationToken ct);
    ValueTask<ItemReference> PutAsync(ContainerId container, int slot, ItemRecord record, CancellationToken ct);
    ValueTask<ItemReference> UpdateAsync(ItemReference reference, Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct);
    ValueTask<ItemRecord> CreateAsync(ItemSnapshot snapshot, CancellationToken ct);
    ValueTask DeleteItemAsync(ItemRecord record, CancellationToken ct);
    ValueTask ApplyDecidedWritesAsync(CancellationToken ct);
}
