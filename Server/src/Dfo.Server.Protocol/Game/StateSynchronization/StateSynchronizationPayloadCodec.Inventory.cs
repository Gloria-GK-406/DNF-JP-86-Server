using System.Buffers.Binary;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

internal static partial class StateSynchronizationPayloadCodec
{
    /// <summary>The fixed part of one item entry, in bytes.</summary>
    /// <remarks>
    /// A21 measures 101. This was 84, which is the older client's length, and the arithmetic in the
    /// reference session settles it with no room to argue: its main-container snapshot declares
    /// fourteen entries and carries 1414 bytes of them, and 1414/101 is exactly fourteen. Its
    /// UPDATE_ITEM_LIST declares five and strides 101 between their slot fields as well.
    ///
    /// The seventeen added bytes are zero in every entry the reference sends, so where inside the
    /// entry they belong cannot be read off the capture -- only that the entry is that much longer.
    /// They are written as a trailing run for that reason, which reproduces every observed entry
    /// byte for byte. An avatar entry appends its jewel-socket block after this run; no A21 avatar
    /// entry has been observed, so that ordering is carried over from the older client rather than
    /// measured.
    /// </remarks>
    private const int CommonItemEntryLength = 101;

    /// <summary>How much of the entry is written field by field before the trailing run.</summary>
    private const int WrittenItemEntryLength = 84;

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
                WriteInventoryItem(writer, item, preparedAt);
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
        writer.Write(PopulatedCount(core.ChronicleOption0.Id, core.ChronicleOption1.Id));
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
        writer.Write(PopulatedCount(
            core.RandomOption0.Type, core.RandomOption1.Type, core.RandomOption2.Type));
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
        writer.Write(new byte[CommonItemEntryLength - WrittenItemEntryLength]);

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

    /// <summary>How many of a fixed option array's slots actually hold something.</summary>
    /// <remarks>
    /// The byte in front of the chronicle and random option arrays used to be written as the
    /// literal 2 and 3 -- the arrays' capacities, transcribed from the older client's builder. A21
    /// puts zero there for every item the reference session sends, and it cannot be a length prefix
    /// because the entry is a fixed 101 bytes either way: the arrays are always present.
    ///
    /// Reading it as "how many are in use" reproduces every observed byte, since none of those
    /// items carries an option. Only the zero case is observed; an item that does carry one has not
    /// been seen on A21, so what it should say then is inference rather than measurement.
    ///
    /// The inference has a gap worth naming: this counts any non-empty slot, not a filled prefix.
    /// An item with slot 0 empty and slot 1 set yields 1, which under the "how many are in use"
    /// reading tells the client slot 0 is the one in use. Whether the field is a count or a filled
    /// length cannot be told apart while every observed item has neither.
    /// </remarks>
    private static byte PopulatedCount(params int[] values) =>
        checked((byte)values.Count(value => value != 0));

    private static InvalidDataException Missing(string name) =>
        new($"Required inventory synchronization value '{name}' is unavailable.");
}
