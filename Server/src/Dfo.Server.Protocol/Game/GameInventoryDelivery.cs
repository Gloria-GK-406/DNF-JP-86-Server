using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game;

/// <summary>
/// One inventory item entry, written the way both inventory frames write it.
/// </summary>
/// <remarks>
/// Lifted here out of the state-synchronization slice on 2026-09-01. Two protocols write this
/// entry -- the entry snapshot NOTI <c>0x000D</c> and the delta NOTI <c>0x000E</c> -- and a second
/// slice now sends the delta, so the entry can no longer live inside either of them: a slice may
/// not reach into another (<c>AGENTS.md</c>, the slice-boundary rules). Nothing about the
/// bytes moved with it.
/// </remarks>
internal static class GameInventoryItemEntry
{
    /// <summary>The fixed part of one item entry, in bytes.</summary>
    /// <remarks>
    /// A21 measures 101. This was 84, which is the older client's length, and the arithmetic in the
    /// reference session settles it with no room to argue: its main-container snapshot declares
    /// fourteen entries and carries 1414 bytes of them, and 1414/101 is exactly fourteen. Its
    /// UPDATE_ITEM_LIST declares five and strides 101 between their slot fields as well.
    ///
    /// Every entry begins with that fixed length, and every value this writer emits lands at the
    /// offset the A21 client reads one from. Two containers then carry a length-prefixed appearance
    /// block after it -- see <see cref="CarriesAppearanceBlock"/>, which is where the rule lives. What was checked is the
    /// offsets, not the names: this writer calls the byte at 0x1A a chronicle-option count and the
    /// byte at 0x2F an emblem-socket count, where the client's own strings call them an emblem
    /// count and an additional-option count. Which naming is right is not settled here -- only that
    /// the widths and positions agree all the way through. The offsets are the ones the client's
    /// own decrypted debug strings name, recovered field by field in
    /// <c>Docs/a21-vmp1/vmp1-9.md</c> section three: SlotIndex@0x00, itemIndex@0x02, Data@0x06,
    /// ext_data1@0x0A, Durability@0x0B, isSealed@0x0D, enchantIndex@0x0E, enchantUpgrade@0x12,
    /// amplify_type@0x13, amplify_value@0x14, an emblem count@0x1A with its sub-array after it,
    /// period@0x2B, an additional-option count@0x2F with its dword array after it,
    /// genuineUpgrade@0x4A, emancipateEquipmentLevel@0x4B, tradeRestriction@0x4C.
    ///
    /// The last seventeen bytes (rec+0x54..0x64) are written as a zero run. The client reads them as
    /// fields and the reference A21 server names them -- a dword, four guardian-gem keys as u16, a
    /// byte, and a dword at rec+0x61 -- but this repository has nowhere to read them from, so a zero
    /// run is what it can honestly send. They are a gap, not padding. The dword at rec+0x61 is the
    /// one place the client-side disassembly and the reference server independently agree.
    ///
    /// Correction (2026-09-07): an earlier note said the seventeen bytes were of unreadable
    /// position. That was wrong and is fixed above.
    ///
    /// Correction to that correction (same day): the same note went on to say that appending a
    /// jewel-socket block after the run was the older client's layout and simply wrong. That is
    /// half right at best. The block is real and the client reads it -- but by container, not by
    /// item kind, which is what the earlier writer had keyed it on. Keying it on the kind put a
    /// block on an avatar sitting in the main bag and broke that list's stride
    /// (STATUS_STACK_BUFFER_OVERRUN); removing it outright starved the avatar list and broke that
    /// one the other way (STATUS_ACCESS_VIOLATION, from a length read out of the receive buffer's
    /// padding). Both were seen against a live client, and the rule that satisfies both is the
    /// client's own, on <see cref="CarriesAppearanceBlock"/>.
    /// </remarks>
    public const int CommonLength = 101;

    /// <summary>How much of the entry is written field by field before the trailing run.</summary>
    private const int WrittenLength = 84;

    public static void Write(
        BinaryWriter writer,
        InventoryItemState item,
        DateTimeOffset preparedAt,
        InventoryContainerType containerType)
    {
        if (item.SlotIndex is < short.MinValue or > short.MaxValue)
            throw new InvalidDataException("Inventory slot cannot be represented as i16.");
        var core = item.Core ?? throw Missing("InventoryItem.Core");
        var avatar = item.AvatarDetail;
        if (core.ItemKind == 8 && avatar is null)
            throw Missing($"AvatarDetail[slot {item.SlotIndex}]");
        if (core.ItemKind != 8 && avatar is not null)
            throw new InvalidDataException("Non-avatar inventory item carries avatar detail.");

        writer.Write(checked((short)item.SlotIndex));
        writer.Write(core.ItemId);
        writer.Write(core.ItemKind == 8
            ? ItemLifetime.At(avatar!.ExpireDate).RemainingSeconds(preparedAt)
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
        writer.Write(WrittenOptionCount(core.EmblemSocketCount));
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
        writer.Write(new byte[CommonLength - WrittenLength]);

        if (core.ItemKind == 8 && avatar!.JewelSocket.Length != 30)
            throw new InvalidDataException("Avatar jewel socket must contain exactly 30 bytes.");

        if (CarriesAppearanceBlock(containerType, core.ItemKind))
            WriteAppearanceBlock(writer, avatar);
    }

    /// <summary>Whether an entry in this container carries the appearance block after its record.</summary>
    /// <remarks>
    /// The client decides this by container, not by what the item is, and the one place the item
    /// does decide is the equipment list. The rule is read off <c>0x01637740</c>, which is not
    /// virtualized:
    /// <code>
    /// F(buf, itemId, selector):
    ///     if (selector == 1) { readLengthPrefixed(buf); return; }
    ///     if (selector == 3) { if (isAvatar(itemId)) readLengthPrefixed(buf); return; }
    ///     return;
    /// </code>
    /// The selector is the list type the frame already carries. Every other container reads a fixed
    /// hundred and one bytes and nothing more.
    /// <para>
    /// Both ways of getting this wrong have been seen against a live client. Writing the block for
    /// every avatar regardless of container puts it on one sitting in the main bag, which pushes
    /// every following entry out of that list's fixed-101 stride and ends in
    /// STATUS_STACK_BUFFER_OVERRUN. Writing it for nobody leaves the avatar list's entries short of
    /// what the client reads, so it takes the four bytes after the record -- the receive buffer's
    /// padding -- as a length and copies that many onto its stack.
    /// </para>
    /// </remarks>
    private static bool CarriesAppearanceBlock(InventoryContainerType containerType, byte itemKind) =>
        containerType switch
        {
            InventoryContainerType.Avatar => true,
            InventoryContainerType.Equipment => itemKind == 8,
            _ => false,
        };

    /// <summary>The two length-prefixed runs the client reads after the record.</summary>
    /// <remarks>
    /// An eighteen-byte jewel socket and a four-byte colour pair, each behind its own length --
    /// thirty bytes in all. See <see cref="AvatarJewelSocketOnTheWire.WireJewelBytes"/> for why
    /// eighteen and not the thirty that are stored. The avatar list reads both runs
    /// unconditionally, so an item with no avatar facts still writes them, zero-filled, because
    /// there is nothing to say. Eleven avatars in a
    /// live avatar bag parsed back to back, which is what shows the shape is aligned rather than
    /// merely accepted -- though a live check cannot tell eighteen from thirty, since the block
    /// declares its own length.
    /// <para>
    /// What the client then does with the bytes is not established. That they are consumed in the
    /// right places is; that the sockets and colours are read as sockets and colours is inferred
    /// from the field names alone.
    /// </para>
    /// </remarks>
    private static void WriteAppearanceBlock(BinaryWriter writer, InventoryAvatarDetailState? avatar)
    {
        AvatarJewelSocketOnTheWire.WriteJewelRun(writer, avatar?.JewelSocket ?? []);
        writer.Write(ColourLength);
        writer.Write((ushort)(avatar?.Color1 ?? 0));
        writer.Write((ushort)(avatar?.Color2 ?? 0));
    }

    private const int ColourLength = 4;

    /// <summary>How many option dwords this writer emits after the count at <c>rec+0x2F</c>.</summary>
    /// <remarks>
    /// Two: <c>EmblemId1</c> and <c>EmblemId2</c>. <c>Rune</c> follows them but is a u16 and belongs
    /// to no array.
    /// </remarks>
    private const byte WrittenOptionEntries = 2;

    /// <summary>The count at <c>rec+0x2F</c>, held to what actually follows it.</summary>
    /// <remarks>
    /// The stored figure is a byte and nothing constrains it, so it can name more options than this
    /// writer emits. The entry is a fixed hundred and one bytes either way -- the array is always
    /// present -- so an over-large count does not lengthen anything; it tells the client to read
    /// bytes this writer never put a value in.
    /// <para>
    /// On A21 that is also a way to crash whoever receives it. Both handlers that read this record
    /// drive an unbounded <c>rep movsd</c> from this count into a fixed stack buffer -- 0x0117C670
    /// (NOTI 0x000D) into one forty bytes from its /GS cookie, 0x0117D720 (NOTI 0x000E) into one a
    /// hundred and ninety-six from its own -- each guarding with <c>jle</c> against zero and nothing
    /// against a ceiling (<c>Docs/a21-vmp1/vmp1-9.md</c> section five; static disassembly, not
    /// observed live).
    /// </para>
    /// <para>
    /// The bound is neither of those client figures but this writer's own output, which is checkable
    /// here rather than in someone else's disassembly, and sits far below both. The equipment path
    /// has clamped the same field the same way for as long as it has existed
    /// (<c>StateSynchronizationPayloadCodec.Entry.cs</c>); this path was the one that did not.
    /// </para>
    /// </remarks>
    private static byte WrittenOptionCount(byte stored) =>
        Math.Min(WrittenOptionEntries, stored);

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

/// <summary>
/// NOTI <c>0x000E</c> <c>UPDATE_ITEM_LIST</c>: the slots of one container that just changed.
/// </summary>
/// <remarks>
/// The endpoint layer's own message rather than any slice's. Two protocols send it -- world entry's
/// daily refill and a cleared run's card reward -- and <c>AGENTS.md</c> forbids one slice reaching
/// into another, so the shared frame is lifted to <c>Game/</c> where both may use it. See
/// <c>protocol/game/server-to-client/NOTI-0x000E.md</c>; the body is a container type, a u16 count
/// and that many entries, each one written exactly as the entry snapshot writes it.
/// <para>
/// It keeps <see cref="ServerMessageType.GameStateSynchronization"/> as its catalogue member. That
/// member is the whole state-synchronization family's bucket rather than one protocol's name -- the
/// family puts its wire type on the message instead of in the catalogue -- and the frame is still
/// one of that family whoever sends it.
/// </para>
/// </remarks>
internal sealed record InventoryDeltaMessage(
    InventoryContainerType ContainerType,
    IReadOnlyList<InventoryItemState> Items,
    DateTimeOffset PreparedAt) : IGameNotiMessage
{
    /// <summary>The wire type, which is the same number the family's own encoder wrote.</summary>
    public const ushort WireType = 0x000E;

    public ServerMessageType MessageType => ServerMessageType.GameStateSynchronization;

    internal byte[] EncodeBody()
    {
        ArgumentNullException.ThrowIfNull(this.Items);
        if (this.Items.Count > ushort.MaxValue)
            throw new InvalidDataException("Inventory delta item count cannot be represented as u16.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)this.ContainerType);
        writer.Write(checked((ushort)this.Items.Count));
        foreach (var item in this.Items)
            GameInventoryItemEntry.Write(writer, item, this.PreparedAt, this.ContainerType);
        return stream.ToArray();
    }
}

internal sealed class InventoryDeltaEncoder : GameNotiMessageEncoder<InventoryDeltaMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        InventoryDeltaMessage message) =>
        OnePayload(InventoryDeltaMessage.WireType, message.EncodeBody());
}

/// <summary>
/// The one road a bag change takes to the client, whichever protocol changed the bag.
/// </summary>
/// <remarks>
/// A single function rather than two call sites building the same message, so that "world entry and
/// a card flip tell the client the same way" is a property of there being one road rather than of
/// two roads happening to agree today.
/// </remarks>
internal static class GameInventoryDelivery
{
    /// <summary>The frame that tells the client what one bounded change left behind.</summary>
    public static IGameServerMessage Notification(CommittedInventoryUpdateState update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var items = update.ContainerType == InventoryContainerType.Main
            ? update.Items.Select(item => item.SlotIndex is >= 354 and <= 364
                ? new InventoryItemState(null, item.SlotIndex, VirtualResourceCore(item.Core.ItemId, item.Core.Value))
                : item).ToArray()
            : update.Items;
        return new InventoryDeltaMessage(update.ContainerType, items, update.PreparedAt);
    }

    public static IGameServerMessage ClearedSlotNotification(
        InventoryContainerType containerType, int slotIndex, DateTimeOffset preparedAt) =>
        new InventoryDeltaMessage(containerType, [new InventoryItemState(null, slotIndex, VirtualResourceCore(-1, 0))], preparedAt);

    /// <summary>
    /// The frame that tells the client what a character now carries in gold.
    /// </summary>
    /// <remarks>
    /// The same delta frame a bag change takes, aimed at the main list's first virtual slot -- the
    /// place the client draws carried gold. The figure is absolute, so the client takes it as the
    /// new total rather than adding it, which is what makes this the way to correct a display that
    /// has drifted.
    /// <para>
    /// <b>Not for a floor pick-up.</b> That one already has its own frame, and that frame carries
    /// the amount picked up rather than a total, which the client adds. Sending both would be
    /// telling the client the same change twice in two different languages.
    /// </para>
    /// <para>
    /// Every field but the slot, the id and the figure is zero, which is the same hundred and one
    /// bytes the entry snapshot writes for this slot. Gold has no durability, no enchant and no
    /// expiry; it is a count wearing an item's shape because that is the shape the client's
    /// inventory window reads.
    /// </para>
    /// </remarks>
    public static IGameServerMessage CarriedGoldNotification(int carried, DateTimeOffset preparedAt) =>
        new InventoryDeltaMessage(
            InventoryContainerType.Main,
            [new InventoryItemState(null, CarriedGoldSlot, CarriedGoldCore(carried))],
            preparedAt);

    /// <summary>
    /// Publishes absolute account-resource values through the main inventory's virtual slots.
    /// </summary>
    public static IGameServerMessage VirtualResourceNotification(
        IReadOnlyList<InventoryRuntimeValueState> resources,
        DateTimeOffset preparedAt)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return new InventoryDeltaMessage(
            InventoryContainerType.Main,
            [.. resources.Select(resource => new InventoryItemState(
                null,
                resource.SlotIndex,
                VirtualResourceCore(resource.ItemId, resource.Value)))],
            preparedAt);
    }

    /// <summary>The main list's first virtual slot, where the client draws carried gold.</summary>
    private const int CarriedGoldSlot = 0;

    private static InventoryItemCoreState CarriedGoldCore(int carried) =>
        VirtualResourceCore(0, carried);

    private static InventoryItemCoreState VirtualResourceCore(int itemId, int value) =>
        new(
            ItemKind: 0,
            ItemId: itemId,
            Value: Math.Max(0, value),
            Attribute: 0,
            Durability: 0,
            SealFlag: 0,
            EnchantCardId: 0,
            EnchantUpgradeCount: 0,
            AmplifyType: 0,
            AmplifyValue: 0,
            Marker16: 0,
            ChronicleOption0: EmptyChronicle,
            ChronicleOption1: EmptyChronicle,
            ExpireTime: 0,
            EmblemSocketCount: 0,
            EmblemId1: 0,
            EmblemId2: 0,
            Rune: 0,
            RandomOption0: EmptyRandomOption,
            RandomOption1: EmptyRandomOption,
            RandomOption2: EmptyRandomOption,
            RandomOptionState: 0,
            RandomOptionChangedIndex: 0,
            RandomOptionChangeState: 0,
            RandomOptionChange: EmptyRandomOption,
            GenuineUpgrade: 0,
            EmancipateEquipmentLevel: 0,
            TradeRestriction: 0,
            TailUnknown0: 0,
            TailUnknown1: 0,
            TailUnknown2: 0,
            TailUnknown3: 0,
            RemainUseCount: 0,
            SortLockFlag: 0,
            EquipmentLockId: 0);

    private static ChronicleOptionState EmptyChronicle => new(0, 0, 0, 0, 0);

    private static RandomOptionState EmptyRandomOption => new(0, 0, 0);

    /// <summary>The same, for a batch of changes that settled together.</summary>
    public static IReadOnlyList<IGameServerMessage> Notifications(
        IReadOnlyList<CommittedInventoryUpdateState> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        return [.. updates.Select(Notification)];
    }

    /// <summary>
    /// Binds the delta to its encoder.
    /// </summary>
    /// <remarks>
    /// Called from the endpoint layer's own wiring rather than from a slice's. A slice that
    /// registered this frame would be the slice that owned it, which is the thing lifting it
    /// undid.
    /// </remarks>
    public static void AddEncoders(IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<InventoryDeltaMessage, InventoryDeltaEncoder>();
        services.AddGameNotiMessageEncoder<InventoryFullSnapshotMessage, InventoryFullSnapshotEncoder>();
        services.AddGameCmdMessageEncoder<CargoCapacityUpgradeMessage, CargoCapacityUpgradeEncoder>();
        services.AddGameNotiMessageEncoder<InventoryLocksMessage, InventoryLocksEncoder>();
    }
}
