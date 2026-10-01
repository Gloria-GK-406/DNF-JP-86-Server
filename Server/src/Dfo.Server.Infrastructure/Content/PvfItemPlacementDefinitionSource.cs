using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Answers where an item belongs, from the archive's stackable and equipment indexes.
/// </summary>
/// <remarks>
/// The resolution is the daily refill's, reused rather than rewritten. That path had to answer the
/// same question -- an item identifier says nothing about which band of the inventory it occupies,
/// and the answer comes from the item's own <c>[stackable type]</c> -- and having two derivations
/// of it would let a refilled potion and a granted one land in different places.
/// </remarks>
internal sealed class PvfItemPlacementDefinitionSource(
    PvfArchiveProvider provider,
    IInventorySlotMappingRuleService? slotMappings = null)
    : IItemPlacementDefinitionSource
{
    private readonly IInventorySlotMappingRuleService slotMappings =
        slotMappings ?? new InventorySlotMappingRuleService();
    private const string StackableListPath = "stackable/stackable.lst";
    private const string EquipmentListPath = "equipment/equipment.lst";

    private readonly Lock _gate = new();
    private readonly Dictionary<int, ItemPlacement?> _cache = [];
    private IReadOnlyDictionary<int, (int Id, string RelativePath)>? _stackables;
    private IReadOnlyDictionary<int, string>? _equipment;

    public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_cache.TryGetValue(itemId, out var cached))
                return ValueTask.FromResult(cached);

            var archive = provider.Archive;
            _stackables ??= PvfListFile.Read(archive, StackableListPath).ToDictionary(value => value.Id);
            _equipment ??= PvfEntryLifecycleContentSource.ReadEquipmentPaths(archive);

            var metadata = PvfEntryLifecycleContentSource.ResolveItemMetadataFor(
                archive, itemId, _stackables, _equipment);

            // An empty range means the archive declares nothing holdable under this identifier.
            // Answered as absent rather than as a placement nobody can use, so a caller cannot
            // insert an item into a range that does not exist.
            var placement = metadata.SlotEnd < metadata.SlotStart
                ? null
                : new ItemPlacement(
                    metadata.ItemKind,
                    metadata.StackLimit,
                    DestinationFor(metadata))
                {
                    WorldClass = metadata.WorldClass,
                };
            _cache[itemId] = placement;
            return ValueTask.FromResult(placement);
        }
    }

    private BusinessSlotRange RangeFromMainSlots(int first, int last)
    {
        var start = slotMappings.ToBusinessSlot(new GameSlot(InventoryContainerType.Main, first));
        var end = slotMappings.ToBusinessSlot(new GameSlot(InventoryContainerType.Main, last));
        if (start.Space != end.Space)
            throw new InvalidDataException(
                $"PVF item placement Main/{first}..{last} crosses business spaces.");
        return new(start.Space, start.Index, end.Index);
    }

    private BusinessSlotRange DestinationFor(PvfEntryLifecycleContentSource.RefillItemMetadata metadata) =>
        metadata switch
        {
            { WorldClass: ItemWorldClass.Avatar } => new(BusinessInventorySpace.Avatar, 0, 209),
            { WorldClass: ItemWorldClass.Creature } => new(BusinessInventorySpace.CreatureInventory, 0, 139),
            { ItemKind: 6 } => new(BusinessInventorySpace.CreatureEquipment, 0, 48),
            { ItemKind: 7 } => new(BusinessInventorySpace.CreatureConsumable, 0, 50),
            _ => RangeFromMainSlots(metadata.SlotStart, metadata.SlotEnd),
        };
}
