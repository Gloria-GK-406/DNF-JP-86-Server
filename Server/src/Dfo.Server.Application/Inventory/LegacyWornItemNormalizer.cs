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
        var held = await stores.ReadAsync(items, session => session.GetAllAsync(container, ct))
            .ConfigureAwait(false);
        var moves = new List<(ItemReference Item, int Target)>();
        foreach (var item in held.Where(value => value.Snapshot.LegacyKind is 5 or 6))
        {
            var worldClass = await placements.ClassifyAsync(item.ItemId, ct).ConfigureAwait(false);
            if (item.Snapshot.LegacyKind == 5 && worldClass != ItemWorldClass.Creature
                || item.Snapshot.LegacyKind == 6 && worldClass != ItemWorldClass.Equipment)
                throw new InvalidDataException($"Worn item {item.ItemId} has a mismatched template and kind.");
            var worn = await placements.WornSlotForAsync(item.ItemId, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException($"Worn item {item.ItemId} has no template slot.");
            if (item.Snapshot.LegacyKind == 5 && worn.Space != BusinessInventorySpace.WornCreature
                || item.Snapshot.LegacyKind == 6 && worn.Space != BusinessInventorySpace.WornCreatureEquipment)
                throw new InvalidDataException($"Worn item {item.ItemId} has an incompatible template slot.");
            var target = LegacyInventoryStorageCoordinates.ToStorage(worn);
            if (target.List != InventoryContainerType.Equipment)
                throw new InvalidDataException("Worn item target does not belong to the equipment list.");
            if (item.SlotIndex != target.Index)
                moves.Add((item, target.Index));
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
            var records = new List<(ItemRecord Record, int Target)>();
            foreach (var move in moves)
                records.Add((await session.TakeAsync(move.Item, null, ct).ConfigureAwait(false), move.Target));
            foreach (var move in records)
                await session.PutAsync(container, move.Target, move.Record, ct).ConfigureAwait(false);
            await session.ApplyDecidedWritesAsync(ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }
}
