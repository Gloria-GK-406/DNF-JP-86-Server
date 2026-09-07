using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

/// <summary>
/// Which container an item is in decides the shape of its entry, and the item's own kind does not.
/// </summary>
/// <remarks>
/// The A21 client reads an item record and then, depending on the container the list belongs to,
/// reads a length-prefixed block after it. The rule is the client's own, read off
/// <c>0x01637740</c> -- which is not virtualized and can be checked instruction by instruction:
/// <code>
/// F(buf, itemId, selector):
///     if (selector == 1) { readLengthPrefixed(buf); return; }                       // avatar list
///     if (selector == 3) { if (isAvatar(itemId)) readLengthPrefixed(buf); return; } // equipment
///     return;                                                                       // everything else
/// </code>
/// <para>
/// Getting the domain of that rule wrong has cost this repository two live crashes, one in each
/// direction. Keying it on the item's kind put the block on an avatar sitting in the main bag,
/// which pushed every following entry out of that list's fixed-101 stride
/// (STATUS_STACK_BUFFER_OVERRUN). Removing it outright left the avatar list's entries short, so the
/// client read four bytes past what was sent, took the receive buffer's ASCII-zero padding as a
/// length (0x30303030) and copied that many bytes onto its stack (STATUS_ACCESS_VIOLATION). Both
/// were observed against a live client.
/// </para>
/// <para>
/// So the assertions here come in matched pairs: what carries the block, and what must not. The
/// second half is the one that was wrong last time, and no recorded frame hash covers it -- the
/// entry sequence only sends five of the containers, and the delta frame can carry others.
/// </para>
/// </remarks>
public sealed class EntryWriterContainerShapeTests
{
    private static readonly DateTimeOffset PreparedAt =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private const int HeaderLength = 3;
    private const int CommonLength = 101;

    /// <summary>Four for the socket's length, eighteen for it, four for the colours' length, four for them.</summary>
    /// <remarks>
    /// Eighteen, not the thirty that are stored: A21 sends the first three of the socket's five
    /// six-byte slots. The reference A21 server names it <c>A21AvatarJewelBytes = 18</c>. Thirty is
    /// the older client's figure and it parses too -- the block is length-prefixed, so any length
    /// lines up -- which is why this is asserted here rather than left to a live check that cannot
    /// tell the two apart.
    /// </remarks>
    private const int BlockLength = 30;

    /// <summary>How many bytes of the socket reach the wire.</summary>
    private const int WireJewelBytes = 18;

    private const byte SocketFill = 0xA5;

    /// <summary>The containers whose entries must never grow, whatever they hold.</summary>
    public static TheoryData<InventoryContainerType> ContainersThatNeverCarryTheBlock() =>
    [
        InventoryContainerType.Main,
        InventoryContainerType.PersonalCargo,
        InventoryContainerType.Pet,
        InventoryContainerType.AccountCargo,
    ];

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)2)]
    [InlineData((byte)8)]
    public void TheAvatarListCarriesTheBlockWhateverTheItemIs(byte kind)
    {
        var body = Body(InventoryContainerType.Avatar, Item(kind));

        Assert.Equal(HeaderLength + CommonLength + BlockLength, body.Length);
    }

    [Fact]
    public void TheAvatarListsBlockIsTwoLengthPrefixedRuns()
    {
        var body = Body(InventoryContainerType.Avatar, Item(8));
        var block = body.AsSpan(HeaderLength + CommonLength).ToArray();

        Assert.Equal(BlockLength, block.Length);
        Assert.Equal<byte[]>([WireJewelBytes, 0, 0, 0], block[..4]);
        Assert.Equal(Enumerable.Repeat(SocketFill, WireJewelBytes), block[4..22]);
        Assert.Equal<byte[]>([4, 0, 0, 0], block[22..26]);
        Assert.Equal<byte[]>([0x22, 0x11, 0x44, 0x33], block[26..30]);
    }

    /// <summary>
    /// The equipment list is the one place the item's own kind still decides, which is what the
    /// client's <c>selector == 3</c> branch does.
    /// </summary>
    [Theory]
    [InlineData((byte)8, HeaderLength + CommonLength + BlockLength)]
    [InlineData((byte)2, HeaderLength + CommonLength)]
    [InlineData((byte)0, HeaderLength + CommonLength)]
    public void TheEquipmentListCarriesTheBlockOnlyForAnAvatar(byte kind, int expected)
    {
        Assert.Equal(expected, Body(InventoryContainerType.Equipment, Item(kind)).Length);
    }

    /// <summary>
    /// The half that was wrong last time: an avatar in an ordinary container stays a hundred and one
    /// bytes. Nothing else in the suite would notice if this stopped being true.
    /// </summary>
    [Theory]
    [MemberData(nameof(ContainersThatNeverCarryTheBlock))]
    public void AnOrdinaryContainerNeverCarriesTheBlockNotEvenForAnAvatar(
        InventoryContainerType container)
    {
        foreach (var kind in new byte[] { 0, 2, 8 })
        {
            var body = Body(container, Item(kind));

            Assert.True(
                body.Length == HeaderLength + CommonLength,
                $"{container} wrote {body.Length - HeaderLength} bytes for an item of kind {kind}; "
                    + "only the avatar list and an avatar in the equipment list may exceed "
                    + $"{CommonLength}.");
        }
    }

    /// <summary>
    /// Length alone does not prove the shape: entries must also line up, or the client reads the
    /// second one at the wrong offset. This is what eleven avatars in a live avatar bag showed, and
    /// it is asserted here so it does not depend on that having been done once.
    /// </summary>
    [Fact]
    public void AvatarListEntriesLineUpBackToBack()
    {
        var body = Body(
            InventoryContainerType.Avatar, Item(8, slot: 0), Item(8, slot: 1), Item(8, slot: 2));
        var stride = CommonLength + BlockLength;

        Assert.Equal(HeaderLength + (3 * stride), body.Length);
        for (var index = 0; index < 3; index++)
        {
            var slot = BitConverter.ToInt16(body, HeaderLength + (index * stride));
            Assert.Equal(index, slot);
        }
    }

    /// <summary>
    /// The avatar list reads the block unconditionally, so an entry with no avatar facts still has
    /// to write one. Zero-filled, because there is nothing to say.
    /// </summary>
    [Fact]
    public void AnItemWithNoAvatarFactsStillWritesTheBlockInTheAvatarList()
    {
        var body = Body(InventoryContainerType.Avatar, Item(2));
        var block = body.AsSpan(HeaderLength + CommonLength).ToArray();

        Assert.Equal(BlockLength, block.Length);
        Assert.Equal<byte[]>([WireJewelBytes, 0, 0, 0], block[..4]);
        Assert.Equal<byte[]>(new byte[WireJewelBytes], block[4..22]);
        Assert.Equal<byte[]>([4, 0, 0, 0], block[22..26]);
        Assert.Equal<byte[]>(new byte[4], block[26..30]);
    }

    private static byte[] Body(InventoryContainerType container, params InventoryItemState[] items) =>
        new InventoryDeltaMessage(container, items, PreparedAt).EncodeBody();

    private static InventoryItemState Item(byte kind, int slot = 3)
    {
        var detail = kind == 8 ? Facts() : null;
        return new InventoryItemState(new ItemIdentity(slot + 1), slot, Core(kind), detail);
    }

    private static InventoryAvatarDetailState Facts()
    {
        var socket = new byte[30];
        Array.Fill(socket, SocketFill);
        return new(1_700_000_500, 44, socket, 0x1122, 0x3344, 0);
    }

    private static InventoryItemCoreState Core(byte kind) => new(
        kind, 1001, 3, 4, 50, 1, 6, 7, 8, 9, -1,
        new ChronicleOptionState(0, 0, 0, 0, 0),
        new ChronicleOptionState(0, 0, 0, 0, 0),
        0, 0, 0, 0, 0,
        new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0),
        0, 0xFF, 0, new RandomOptionState(0, 0, 0),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
