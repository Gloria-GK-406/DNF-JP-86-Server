using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public interface IItemLifecycleDomainService
{
    /// <summary>Prepare only a new grant before stacking; does not mutate or renew an existing record.</summary>
    ValueTask<ItemSnapshot> PrepareNewAsync(ItemSnapshot snapshot, CancellationToken ct, DateTimeOffset? at = null);
    ValueTask<ItemRecord> CreateAsync(ItemSnapshot snapshot, CancellationToken ct);
    ValueTask DeleteItemAsync(ItemRecord record, CancellationToken ct);
    ValueTask<(ItemRecord Taken, ItemRecord Remainder)> SplitAsync(ItemRecord record, int count, CancellationToken ct);
}
