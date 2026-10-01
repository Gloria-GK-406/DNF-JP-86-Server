using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game;

internal static class GameInventorySnapshotCodec
{
    public static byte[] EncodeInventorySnapshot(
        InventoryContainerState container,
        IReadOnlyList<InventoryRuntimeValueState> characterVirtualResources,
        IReadOnlyList<InventoryRuntimeValueState> accountVirtualResources,
        ushort accountSelectionKey,
        int accountCargoGold,
        DateTimeOffset preparedAt)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(characterVirtualResources);
        ArgumentNullException.ThrowIfNull(accountVirtualResources);
        var containerType = container.ContainerType;

        var items = container.Items.OrderBy(value => value.SlotIndex).ToArray();
        // Three currency slots (0 gold, 1 revival coin, 2 PvP point), six cubes and the five
        // A21 added at 360..364. See InventoryRuntimeValueState for what each holds.
        var mainVirtualCount = containerType == InventoryContainerType.Main ? 14 : 0;
        if (items.Length + mainVirtualCount > ushort.MaxValue)
            throw new InvalidDataException("Inventory item count cannot be represented as u16.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)containerType);
        if (containerType is InventoryContainerType.AccountCargo)
        {
            writer.Write(accountSelectionKey);
            // The cargo's own gold, between the selection key and the item count. Shared by every
            // character on the account; the gold a character carries rides in the main list's
            // first virtual slot instead.
            writer.Write(accountCargoGold);
            writer.Write(checked((ushort)(items.Length + mainVirtualCount)));
        }
        else if (containerType is InventoryContainerType.Pet)
            writer.Write(checked((ushort)items.Length));
        else
        {
            writer.Write(container.Parameter);
            writer.Write(checked((ushort)(items.Length + mainVirtualCount)));
        }

        if (containerType == InventoryContainerType.Main)
        {
            var values = characterVirtualResources
                .Concat(accountVirtualResources)
                .ToDictionary(value => value.SlotIndex);
            for (short slot = 0; slot <= 2; slot++)
                WriteVirtualInventoryItem(writer, values, slot, slot);
            foreach (var item in items)
                GameInventoryItemEntry.Write(writer, item, preparedAt, containerType);
            foreach (var material in AccountMaterialCatalog.Definitions)
            {
                WriteVirtualInventoryItem(
                    writer,
                    values,
                    checked((short)material.VirtualSlotIndex),
                    material.ItemId);
            }
        }
        else
        {
            foreach (var item in items)
                GameInventoryItemEntry.Write(writer, item, preparedAt, containerType);
        }
        return stream.ToArray();
    }

    private static void WriteVirtualInventoryItem(
        BinaryWriter writer,
        IReadOnlyDictionary<int, InventoryRuntimeValueState> values,
        short slot,
        int fixedItemId)
    {
        var count = values.TryGetValue(slot, out var value) ? value.Value : 0;
        if (value is not null && value.ItemId != fixedItemId)
            throw new InvalidDataException($"Virtual inventory slot {slot} has item {value.ItemId}, expected {fixedItemId}.");
        writer.Write(slot);
        writer.Write(fixedItemId);
        writer.Write(count);
        writer.Write(new byte[GameInventoryItemEntry.CommonLength - 10]);
    }

    public static byte[] EncodeEquipmentLocks(
        IReadOnlyList<InventoryEquipmentLockState> characterLocks,
        IReadOnlyList<InventoryEquipmentLockState> accountLocks)
    {
        var locks = characterLocks.Concat(accountLocks).OrderBy(value => value.EquipmentLockId).ToArray();
        if (locks.Length > ushort.MaxValue) throw new InvalidDataException("Equipment lock count cannot be represented as u16.");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); writer.Write(checked((ushort)locks.Length));
        foreach (var value in locks)
        {
            writer.Write((byte)value.ContainerType); writer.Write(checked((ushort)value.SlotIndex)); writer.Write(value.State);
            if (value.RemainingSeconds != 0) writer.Write(value.RemainingSeconds);
        }
        return stream.ToArray();
    }

}
