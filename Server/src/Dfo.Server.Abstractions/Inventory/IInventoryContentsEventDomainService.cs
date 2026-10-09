using Dfo.Server.Abstractions.DomainEvents;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>A committed business fact about membership, quantity or template changes in a container.</summary>
public sealed record InventoryContentsChanged(ContainerId Container) : IDomainEvent;

/// <summary>Records contents changes inside the item's current writable transaction.</summary>
public interface IInventoryContentsEventDomainService
{
    ValueTask RecordAsync(ContainerId container, CancellationToken ct);
}
