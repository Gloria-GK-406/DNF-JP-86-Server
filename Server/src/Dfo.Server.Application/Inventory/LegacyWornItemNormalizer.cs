using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

// Normalizes old creature/artifact positions before any entry projection reads them.
// The item store settles all clears and writes in the surrounding account transaction.
internal sealed class LegacyWornItemNormalizer(
    IItemDomainStore items, IItemPlacementRuleService placements, AmbientStoreAccess stores)
{
    public async ValueTask NormalizeAsync(CharacterId character, CancellationToken ct)
    {
        var container = ContainerId.ForCharacter(character, InventoryContainerType.Equipment);
        var held = await stores.ReadAsync(items, session => session.ReadAllAsync(container, ct))
            .ConfigureAwait(false);
        var moves = new List<(StoredItem Item, int Target, ItemWorldClass Class)>();
        foreach (var item in held.Where(value => value.Core.ItemKind is 5 or 6))
        {
            var worldClass = await placements.ClassifyAsync(item.Core.ItemId, ct).ConfigureAwait(false);
            if (item.Core.ItemKind == 5 && worldClass != ItemWorldClass.Creature
                || item.Core.ItemKind == 6 && worldClass != ItemWorldClass.Equipment)
                throw new InvalidDataException($"Worn item {item.Core.ItemId} has a mismatched template and kind.");
            var worn = await placements.WornSlotForAsync(item.Core.ItemId, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException($"Worn item {item.Core.ItemId} has no template slot.");
            if (item.Core.ItemKind == 5 && worn.Space != BusinessInventorySpace.WornCreature
                || item.Core.ItemKind == 6 && worn.Space != BusinessInventorySpace.WornCreatureEquipment)
                throw new InvalidDataException($"Worn item {item.Core.ItemId} has an incompatible template slot.");
            var target = LegacyInventoryStorageCoordinates.ToStorage(worn);
            if (target.List != InventoryContainerType.Equipment)
                throw new InvalidDataException("Worn item target does not belong to the equipment list.");
            if (item.SlotIndex != target.Index)
                moves.Add((item, target.Index, worldClass));
        }
        if (moves.Count == 0) return;

        var movingSources = moves.Select(move => move.Item.SlotIndex).ToHashSet();
        var occupied = held.ToDictionary(item => item.SlotIndex);
        if (moves.Select(move => move.Target).Distinct().Count() != moves.Count
            || moves.Any(move => occupied.ContainsKey(move.Target)
                && !movingSources.Contains(move.Target)))
            throw new InvalidDataException("Legacy worn item normalization has a target collision.");

        await stores.ChangeAsync(items, async session =>
        {
            foreach (var move in moves)
                session.Clear(container, move.Item.SlotIndex);
            foreach (var move in moves)
                session.Write(container, new StoredItem(move.Target, move.Item.Core)
                {
                    WorldClass = move.Class,
                    TransferFrom = new ItemTransferSource(container, move.Item.SlotIndex),
                });
            await session.ApplyDecidedWritesAsync(ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }
}
