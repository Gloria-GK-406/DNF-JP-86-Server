using System.Buffers.Binary;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

internal static partial class StateSynchronizationPayloadCodec
{
    private const int CommonItemEntryLength = 84;

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
        var mainVirtualCount = containerType == InventoryContainerType.Main ? 9 : 0;
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
                WriteInventoryItem(writer, item, preparedAt);
            int[] cubeItemIds = [3033, 3034, 3035, 3036, 3037, 3262];
            for (short slot = 354; slot <= 359; slot++)
                WriteVirtualInventoryItem(writer, values, slot, cubeItemIds[slot - 354]);
        }
        else
        {
            foreach (var item in items)
                WriteInventoryItem(writer, item, preparedAt);
        }
        return stream.ToArray();
    }

    public static byte[] EncodeInventoryDelta(
        InventoryContainerType containerType,
        IReadOnlyList<InventoryItemState> items,
        DateTimeOffset preparedAt)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count > ushort.MaxValue)
            throw new InvalidDataException("Inventory delta item count cannot be represented as u16.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)containerType);
        writer.Write(checked((ushort)items.Count));
        foreach (var item in items)
            WriteInventoryItem(writer, item, preparedAt);
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
        writer.Write(new byte[CommonItemEntryLength - 10]);
    }

    internal static void WriteInventoryItem(
        BinaryWriter writer,
        InventoryItemState item,
        DateTimeOffset preparedAt)
    {
        if (item.SlotIndex is < short.MinValue or > short.MaxValue)
            throw new InvalidDataException("Inventory slot cannot be represented as i16.");
        var core = item.Core ?? throw Missing("InventoryItem.Core");
        var avatar = item.AvatarDetail;
        if (core.ItemKind == 8 && avatar is null)
            throw Missing($"AvatarDetail[{item.StorageEntryId}]");
        if (core.ItemKind != 8 && avatar is not null)
            throw new InvalidDataException("Non-avatar inventory item carries avatar detail.");

        writer.Write(checked((short)item.SlotIndex));
        writer.Write(core.ItemId);
        writer.Write(core.ItemKind == 8
            ? Math.Max(0, avatar!.ExpireDate - checked((int)preparedAt.ToUnixTimeSeconds()))
            : core.Value);
        writer.Write(core.Attribute);
        writer.Write(core.Durability);
        writer.Write(core.SealFlag);
        writer.Write(core.EnchantCardId);
        writer.Write(core.EnchantUpgradeCount);
        writer.Write(core.AmplifyType);
        writer.Write(core.AmplifyValue);
        writer.Write(core.ItemKind == 2 ? core.Marker16 : Math.Max(0, core.Marker16));
        writer.Write((byte)2);
        writer.Write(core.ChronicleOption0.Id);
        writer.Write(core.ChronicleOption1.Id);
        writer.Write(core.ChronicleOption0.CharacterJob);
        writer.Write(core.ChronicleOption1.CharacterJob);
        writer.Write(core.ChronicleOption0.FirstGrowType);
        writer.Write(core.ChronicleOption1.FirstGrowType);
        writer.Write(core.ChronicleOption0.EquipmentType);
        writer.Write(core.ChronicleOption1.EquipmentType);
        writer.Write(core.ChronicleOption0.OptionNumber);
        writer.Write(core.ChronicleOption1.OptionNumber);
        writer.Write(core.ExpireTime);
        writer.Write(core.EmblemSocketCount);
        writer.Write(core.EmblemId1);
        writer.Write(core.EmblemId2);
        writer.Write(core.Rune);
        writer.Write((byte)3);
        writer.Write(core.RandomOption0.Type);
        writer.Write(core.RandomOption1.Type);
        writer.Write(core.RandomOption2.Type);
        writer.Write(core.RandomOption0.Value1);
        writer.Write(core.RandomOption1.Value1);
        writer.Write(core.RandomOption2.Value1);
        writer.Write(core.RandomOption0.Value2);
        writer.Write(core.RandomOption1.Value2);
        writer.Write(core.RandomOption2.Value2);
        writer.Write(core.RandomOptionState);
        writer.Write(core.RandomOptionChangedIndex);
        writer.Write(core.RandomOptionChangeState);
        writer.Write(core.RandomOptionChange.Type);
        writer.Write(core.RandomOptionChange.Value1);
        writer.Write(core.RandomOptionChange.Value2);
        writer.Write(core.GenuineUpgrade);
        writer.Write(core.EmancipateEquipmentLevel);
        writer.Write(core.TradeRestriction);
        writer.Write(core.TailUnknown0);
        writer.Write(core.TailUnknown1);
        writer.Write(core.TailUnknown2);
        writer.Write(core.TailUnknown3);
        writer.Write(core.RemainUseCount);
        writer.Write(core.SortLockFlag == 1 ? (byte)1 : (byte)0);

        if (core.ItemKind == 8)
        {
            if (avatar!.JewelSocket.Length != 30)
                throw new InvalidDataException("Avatar jewel socket must contain exactly 30 bytes.");
            writer.Write(30);
            writer.Write(avatar.JewelSocket);
            writer.Write(4);
            writer.Write(avatar.Color1);
            writer.Write(avatar.Color2);
        }
    }

    private static InvalidDataException Missing(string name) =>
        new($"Required inventory synchronization value '{name}' is unavailable.");
}
