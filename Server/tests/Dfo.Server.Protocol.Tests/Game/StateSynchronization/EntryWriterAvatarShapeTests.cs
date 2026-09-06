using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

/// <summary>
/// What the item-entry writer refuses to put on the wire, and the shape it accepts instead.
/// </summary>
/// <remarks>
/// Two of the writer's conditions decide the length of what it emits rather than a value inside it:
/// an entry is a hundred and one bytes unless it is an avatar's, and an avatar's carries a
/// jewel-socket block after that run. An item that disagreed with either -- facts on something that
/// is not an avatar, or a socket that is not thirty bytes -- would be written as a length the client
/// cannot read back, and every entry after it in the same frame would be read at the wrong offset.
/// <para>
/// Neither condition has a constraint behind it. The kind is a byte inside the core, which nothing
/// in the database can reach; the socket length is checked on the stored column but the value
/// written here need not have come from that column.
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

    /// <summary>What an avatar entry appends after the common run.</summary>
    /// <remarks>
    /// Four for the socket's length prefix, thirty for the socket itself, four for the colour
    /// block's prefix, and two apiece for the two colours: 4 + 30 + 4 + 2 + 2 = 42.
    /// </remarks>
    private const int ObservedAvatarTailLength = 42;

    private const int JewelSocketLength = 30;

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
    /// reading they are most likely to be misread as. It is also the only assertion in the suite
    /// over the length of an avatar entry: the recorded frame hashes all cover items of kind 2.
    /// </remarks>
    [Fact]
    public void AnAvatarWithItsThirtyBytesIsWrittenAsTheLongerEntry()
    {
        var body = Body(new InventoryItemState(
            new ItemIdentity(2), 3, Core(8, 2002, 9001), Facts(JewelSocketLength)));

        Assert.Equal(
            ObservedDeltaHeaderLength + ObservedEntryLength + ObservedAvatarTailLength,
            body.Length);
    }

    private static byte[] Body(InventoryItemState item) =>
        new InventoryDeltaMessage(InventoryContainerType.Main, [item], PreparedAt).EncodeBody();

    private static InventoryAvatarDetailState Facts(int socketLength) =>
        new(1_700_000_500, 44, new byte[socketLength], 0x1122, 0x3344, 0);

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
