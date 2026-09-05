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
    /// The seventeen added bytes are zero in every entry the reference sends, so where inside the
    /// entry they belong cannot be read off the capture -- only that the entry is that much longer.
    /// They are written as a trailing run for that reason, which reproduces every observed entry
    /// byte for byte. An avatar entry appends its jewel-socket block after this run; no A21 avatar
    /// entry has been observed, so that ordering is carried over from the older client rather than
    /// measured.
    /// </remarks>
    public const int CommonLength = 101;

    /// <summary>How much of the entry is written field by field before the trailing run.</summary>
    private const int WrittenLength = 84;

    public static void Write(
        BinaryWriter writer,
        InventoryItemState item,
        DateTimeOffset preparedAt)
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
        writer.Write(new byte[CommonLength - WrittenLength]);

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
            GameInventoryItemEntry.Write(writer, item, this.PreparedAt);
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
        return new InventoryDeltaMessage(update.ContainerType, update.Items, update.PreparedAt);
    }

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

    /// <summary>The main list's first virtual slot, where the client draws carried gold.</summary>
    private const int CarriedGoldSlot = 0;

    private static InventoryItemCoreState CarriedGoldCore(int carried) =>
        new(
            ItemKind: 0,
            ItemId: 0,
            Value: Math.Max(0, carried),
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
    public static void AddEncoders(IServiceCollection services) =>
        services.AddGameNotiMessageEncoder<InventoryDeltaMessage, InventoryDeltaEncoder>();
}
