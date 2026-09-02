using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

internal static partial class StateSynchronizationPayloadCodec
{
    /// <param name="townId">
    /// The town this entry places the character in -- the same value NOTI 0x0017 carries. It is a
    /// lookup key on the client, not a display field: the byte is handed to a keyed town manager
    /// and the object that comes back is dereferenced without a null check, so a town the content
    /// does not declare terminates the client. See protocol/game/client-to-server/0x0004.md.
    /// </param>
    public static byte[] EncodeEntryAccepted(
        ActiveCharacterState character,
        AccountEntryState account,
        CharacterQuestEntryState quests,
        byte townId,
        DateTimeOffset preparedAt)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(quests);
        var characterId = checked((ushort)character.Identity.CharacterId.Value);
        // A backstop, not the guarantee. Zero is the value the old defect produced and the one the
        // content can never declare; the towns that do exist are not a contiguous range, so only
        // EntryPlacementRuleService's lookup can tell a real town from a plausible number, and it
        // already refuses to resolve a spawn in a town the archive does not define.
        if (townId == 0)
        {
            throw new InvalidDataException(
                "Entry town cannot be zero: the client resolves this byte to a town object and "
                + "dereferences the result unchecked.");
        }

        if (account.Entitlements.Premiums.Count > byte.MaxValue)
            throw new InvalidDataException("Premium count cannot be represented as u8.");

        using var stream = new MemoryStream(320);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(0); // observed account-registration compatibility field
        writer.Write(checked((int)character.CreatedAt.ToUnixTimeSeconds()));
        writer.Write(characterId);
        writer.Write((ushort)0); // observed leading fatigue compatibility field
        writer.Write(character.Fatigue.Total);
        writer.Write(character.Fatigue.Used);
        writer.Write(checked((byte)account.Entitlements.Premiums.Count));
        foreach (var premium in account.Entitlements.Premiums.OrderBy(value => value.PremiumType))
        {
            // The observed ACK layout carries only the low byte. Legacy loading
            // projected the stored integer to byte before this body was built.
            writer.Write(unchecked((byte)premium.PremiumType));
            writer.Write(Math.Max(0, premium.ExpiresAtUnixSeconds - preparedAt.ToUnixTimeSeconds()));
        }
        writer.Write(account.Wallet.Cera);

        var slots = quests.ActiveQuests.ToDictionary(value => value.Slot);
        for (short slot = 0; slot < 30; slot++)
        {
            if (slots.TryGetValue(slot, out var quest))
            {
                writer.Write(checked((ushort)quest.QuestId));
                writer.Write(checked((uint)quest.TriggerValue));
            }
            else
            {
                writer.Write(ushort.MaxValue);
                writer.Write(0u);
            }
        }
        for (var index = 0; index < 4; index++)
            writer.Write(index < quests.NotificationQuestIds.Count
                ? quests.NotificationQuestIds[index]
                : 0);

        // Offset 218. The reference server fills this from the character's stored town while calling
        // the field AckCharSlotIndex; the name is its own misnomer and the value is what matters.
        writer.Write(townId);
        writer.Write((byte)0);
        if (character.Progression.Level <= 1)
            writer.Write((byte)0);
        else
        {
            writer.Write((byte)1);
            writer.Write((byte)0x4E);
        }
        writer.Write((ushort)0); // observed fatigue-battery empty state
        writer.Write((ushort)0); // observed grown-up-buff empty state
        writer.Write((byte)0); // observed no trade punishment
        writer.Write((ushort)0); // observed 86JP compatibility field
        writer.Write(new byte[8]);
        writer.Write((byte)0); // observed tutorial-skippable state
        writer.Write((ushort)0);
        writer.Write(new byte[22]);
        return stream.ToArray();
    }

    public static byte[] EncodeInventorySnapshot(
        InventoryContainerState container,
        IReadOnlyList<InventoryRuntimeValueState> characterVirtualResources,
        IReadOnlyList<InventoryRuntimeValueState> accountVirtualResources,
        ushort accountSelectionKey,
        int accountValue32,
        DateTimeOffset preparedAt)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(characterVirtualResources);
        ArgumentNullException.ThrowIfNull(accountVirtualResources);
        var containerType = container.ContainerType;

        var items = container.Items.OrderBy(value => value.SlotIndex).ToArray();
        // Three runtime slots, six cubes and the five A21 added at 360..364.
        var mainVirtualCount = containerType == InventoryContainerType.Main ? 14 : 0;
        if (items.Length + mainVirtualCount > ushort.MaxValue)
            throw new InvalidDataException("Inventory item count cannot be represented as u16.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)containerType);
        if (containerType is InventoryContainerType.AccountCargo)
        {
            writer.Write(accountSelectionKey);
            writer.Write(accountValue32);
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
                GameInventoryItemEntry.Write(writer, item, preparedAt);
            int[] cubeItemIds = [3033, 3034, 3035, 3036, 3037, 3262];
            for (short slot = 354; slot <= 359; slot++)
                WriteVirtualInventoryItem(writer, values, slot, cubeItemIds[slot - 354]);
            // Five slots the older client's main container did not have. The reference session's
            // snapshot declares fourteen entries -- three at slots 0..2, six cubes at 354..359, and
            // these -- where this server declared nine. The ids are transcribed; what they hold is
            // not established, and like the cubes they carry a count of zero until something sets one.
            int[] extendedItemIds = [10100115, 10100116, 10099773, 10099774, 10099775];
            for (short slot = 360; slot <= 364; slot++)
                WriteVirtualInventoryItem(writer, values, slot, extendedItemIds[slot - 360]);
        }
        else
        {
            foreach (var item in items)
                GameInventoryItemEntry.Write(writer, item, preparedAt);
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

    private static InvalidDataException Missing(string name) =>
        new($"Required inventory synchronization value '{name}' is unavailable.");
}
