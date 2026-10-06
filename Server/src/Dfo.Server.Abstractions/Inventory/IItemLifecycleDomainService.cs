using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public interface IItemLifecycleDomainService
{
    ValueTask<ItemRecord> CreateAsync(ItemSnapshot snapshot, CancellationToken ct);
    ValueTask DeleteItemAsync(ItemRecord record, CancellationToken ct);
    ValueTask<(ItemRecord Taken, ItemRecord Remainder)> SplitAsync(ItemRecord record, int count, CancellationToken ct);
}
