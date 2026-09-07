using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

/// <summary>
/// What the item-entry writer refuses to put on the wire, and the shape it accepts instead.
/// </summary>
/// <remarks>
/// This class holds what the writer <b>refuses</b>, and one shape it accepts. An item that carries
/// avatar facts without being an avatar, or an avatar whose stored jewel socket is not thirty
/// bytes, is refused rather than written. How long an accepted entry comes out is decided by the
/// container and asserted in <see cref="EntryWriterContainerShapeTests"/>, not here.
/// <para>
/// Neither refusal has a constraint behind it. The kind is a byte inside the core, which nothing in
/// the database can reach; the socket length is checked on the stored column but the value written
/// here need not have come from that column.
/// </para>
/// <para>
/// Correction (2026-09-07): this used to say an avatar entry carries a jewel-socket block after the
/// common run, and asserted it at a hundred and forty-three bytes. Keyed on the item's kind, which
/// is what that writer did, it was wrong: it put the block on an avatar in the main bag and broke
/// that list's fixed-101 stride (STATUS_STACK_BUFFER_OVERRUN).
/// </para>
/// <para>
/// Correction to that correction (same day): removing the block outright was also wrong, and
/// crashed the avatar list the other way. The block belongs to the <b>container</b>, not the item:
/// the avatar list carries it on every entry, the equipment list on avatars only, and no other
/// container ever. That rule and its two matched halves are asserted in
/// <see cref="EntryWriterContainerShapeTests"/>; what stays here is what the writer refuses.
/// The jewel socket therefore is on the wire again -- eighteen of its thirty stored bytes, which is
/// the A21 length -- so the thirty-byte check below guards a value that is read rather than a
/// value nothing looks at.
/// </para>
/// </remarks>
public sealed class EntryWriterAvatarShapeTests
{
    private static readonly DateTimeOffset PreparedAt =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>One item entry, in bytes. Transcribed rather than read off the constant.</summary>
    private const int ObservedEntryLength = 101;

    /// <summary>The delta's own header: a container type and a count.</summary>
    private const int ObservedDeltaHeaderLength = 3;

    private const int JewelSocketLength = 30;

    /// <summary>A socket value no other field of the entry would produce.</summary>
    private const byte JewelSocketFill = 0xA5;

    [Fact]
    public void AnItemThatIsNotAnAvatarMayNotCarryAvatarFacts()
    {
        var refused = Assert.Throws<InvalidDataException>(() => Body(
            new InventoryItemState(
                new ItemIdentity(1), 3, Core(2, 1001, 3), Facts(JewelSocketLength))));

        Assert.Equal("Non-avatar inventory item carries avatar detail.", refused.Message);
    }

    [Theory]
    [InlineData(JewelSocketLength - 1)]
    [InlineData(JewelSocketLength + 1)]
    public void AnAvatarWhoseJewelSocketIsNotThirtyBytesIsRefused(int socketLength)
    {
        var refused = Assert.Throws<InvalidDataException>(() => Body(
            new InventoryItemState(
                new ItemIdentity(2), 3, Core(8, 2002, 9001), Facts(socketLength))));

        Assert.Equal("Avatar jewel socket must contain exactly 30 bytes.", refused.Message);
    }

    /// <summary>The shape the two refusals exist to protect, written out so it is not assumed.</summary>
    /// <remarks>
    /// Without this the two above would also pass if the writer refused every avatar, which is the
    /// reading they are most likely to be misread as. It says only that an avatar in an ordinary
    /// container comes out at the common length; how long one comes out in the containers that do
    /// carry the appearance block is asserted in <see cref="EntryWriterContainerShapeTests"/>.
    /// </remarks>
    [Fact]
    public void AnAvatarWithItsThirtyBytesIsWrittenAsTheSameHundredAndOneByteEntry()
    {
        var body = Body(new InventoryItemState(
            new ItemIdentity(2), 3, Core(8, 2002, 9001), Facts(JewelSocketLength)));

        Assert.Equal(ObservedDeltaHeaderLength + ObservedEntryLength, body.Length);
    }

    /// <summary>
    /// The same fixed length for the kinds that are not avatars, so the avatar assertion above is
    /// a statement about every item rather than about avatars alone.
    /// </summary>
    /// <remarks>
    /// Kind 0 is the shape carried gold is written in -- an entry with nothing but a slot, an id
    /// and a figure -- and kind 2 is an ordinary pile. Neither may carry avatar facts, so neither
    /// is expressible in the theory that covers kind 8.
    /// </remarks>
    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)2)]
    public void AnItemThatIsNotAnAvatarIsWrittenAsTheSameHundredAndOneByteEntry(byte kind)
    {
        var body = Body(new InventoryItemState(
            new ItemIdentity(4), 3, Core(kind, 4004, 5)));

        Assert.Equal(ObservedDeltaHeaderLength + ObservedEntryLength, body.Length);
    }

    /// <summary>
    /// That the avatar entry is the same length is not enough: it must be the same entry.
    /// </summary>
    /// <remarks>
    /// A writer that dropped the trailing block but moved the socket somewhere inside the run would
    /// still satisfy the length assertion above while overwriting fields the client reads. The two
    /// entries are compared from <c>rec+0x1A</c> onward because the bytes before it legitimately
    /// differ: an avatar's <c>Data</c> at <c>rec+0x06</c> carries its remaining seconds rather than
    /// the core's value, and the marker at <c>rec+0x16</c> is clamped for one kind and not the
    /// other.
    /// </remarks>
    [Fact]
    public void AnAvatarCarriesNoJewelBytesAnywhereInsideTheEntry()
    {
        const int optionRegionStart = ObservedDeltaHeaderLength + 0x1A;

        var avatar = Body(new InventoryItemState(
            new ItemIdentity(2), 3, Core(8, 2002, 9001), Facts(JewelSocketLength)));
        var ordinary = Body(new InventoryItemState(
            new ItemIdentity(2), 3, Core(2, 2002, 9001)));

        Assert.Equal(
            ordinary[optionRegionStart..],
            avatar[optionRegionStart..]);
        Assert.DoesNotContain(JewelSocketFill, avatar);
    }

    private static byte[] Body(InventoryItemState item) =>
        new InventoryDeltaMessage(InventoryContainerType.Main, [item], PreparedAt).EncodeBody();

    private static InventoryAvatarDetailState Facts(int socketLength)
    {
        var socket = new byte[socketLength];
        Array.Fill(socket, JewelSocketFill);
        return new(1_700_000_500, 44, socket, 0x1122, 0x3344, 0);
    }

    private static InventoryItemCoreState Core(byte kind, int itemId, int value) => new(
        kind, itemId, value, 4, 50, 1, 6, 7, 8, 9, -1,
        new ChronicleOptionState(0, 0, 0, 0, 0),
        new ChronicleOptionState(0, 0, 0, 0, 0),
        0, 0, 0, 0, 0,
        new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0),
        0, 0xFF, 0, new RandomOptionState(0, 0, 0),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
