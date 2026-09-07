using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

/// <summary>
/// The two counts the item entry puts in front of its option arrays, and why neither may exceed
/// what the writer actually goes on to write.
/// </summary>
/// <remarks>
/// Both counts tell the client how many entries of an array that follows them are real. The entry
/// is a fixed hundred and one bytes either way -- the arrays are always present -- so a count
/// larger than what the writer emits does not lengthen anything. It tells the client to read bytes
/// this writer never put a value in.
/// <para>
/// For the count at <c>rec+0x2F</c> that is not only wrong, it is a way to crash whoever receives
/// it. The A21 client drives an unbounded <c>rep movsd</c> from it into a fixed stack buffer, in
/// both handlers that read this record: <c>0x0117C670</c> (NOTI 0x000D) copies into a buffer forty
/// bytes from its /GS cookie, and <c>0x0117D720</c> (NOTI 0x000E) into one a hundred and ninety-six
/// bytes from its own. Each guards the count with <c>jle</c> against zero and nothing against a
/// ceiling, so any value from one to a hundred and twenty-seven can overrun one of them
/// (<c>Docs/a21-vmp1/vmp1-9.md</c>, section five; static disassembly, not observed live).
/// </para>
/// <para>
/// The bound asserted here is two, and it is not either of those client figures. It is how many
/// option dwords this writer actually emits after the count: <c>EmblemId1</c> and <c>EmblemId2</c>.
/// Choosing it that way makes the assertion checkable against this repository rather than against a
/// number read out of someone else's disassembly, and it happens to sit far below every client
/// ceiling. The equipment path has clamped the same field the same way for as long as it has
/// existed -- <c>StateSynchronizationPayloadCodec.Entry.cs</c>, <c>Math.Min(2, ...)</c> -- and the
/// inventory path was the one place that did not.
/// </para>
/// <para>
/// Nothing else in the suite reaches this writer with a non-zero <c>EmblemSocketCount</c>. That is
/// measured, not assumed: a production probe that throws on any non-zero value reddens nine cases,
/// all of them in this class, and leaves every other test project green. Seven places in
/// <c>Server/tests</c> do set the field, and none of them arrives here -- six in
/// <c>InventoryLoadCharacterizationTests</c>, which seeds 85, 105, 125, 145, 175 and 205 through
/// storage and asserts they load back unchanged, and one in <c>SelectCharacterPayloadCodecTests</c>
/// that belongs to a support-character test and goes out through the NOTI-2 equipped-item writer
/// instead. So no recorded frame hash covers this path: removing the clamp reddens only this class
/// and leaves all sixty-two anchored frames identical. That is why these assertions read the byte
/// directly rather than relying on the suite going green.
/// </para>
/// <para>
/// Those six seeded values are the evidence that the value being clamped is real rather than
/// hypothetical: a byte well above two survives storage and load into exactly the kind of core this
/// writer is handed.
/// </para>
/// </remarks>
public sealed class EntryWriterOptionCountTests
{
    private static readonly DateTimeOffset PreparedAt =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The delta's own header: a container type and a count.</summary>
    private const int HeaderLength = 3;

    /// <summary>Where the emblem/jewel count sits inside the entry.</summary>
    private const int EmblemCountOffset = 0x1A;

    /// <summary>Where the additional-option count sits inside the entry.</summary>
    private const int OptionCountOffset = 0x2F;

    /// <summary>How many option dwords the writer emits after the count at 0x2F.</summary>
    private const int WrittenOptionEntries = 2;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(10, 2)]
    [InlineData(11, 2)]
    [InlineData(127, 2)]
    [InlineData(128, 2)]
    [InlineData(255, 2)]
    public void TheOptionCountNeverExceedsTheOptionsTheWriterActuallyWrites(
        byte stored, byte written)
    {
        var body = Body(Core(emblemSocketCount: stored));

        Assert.Equal(written, body[HeaderLength + OptionCountOffset]);
        Assert.True(body[HeaderLength + OptionCountOffset] <= WrittenOptionEntries);
    }

    /// <summary>
    /// Every value a stored byte can hold, not only the ones a test author thought to name.
    /// </summary>
    [Fact]
    public void NoStoredEmblemSocketCountCanPutAnOverLargeCountOnTheWire()
    {
        for (var stored = 0; stored <= byte.MaxValue; stored++)
        {
            var body = Body(Core(emblemSocketCount: (byte)stored));
            var onTheWire = body[HeaderLength + OptionCountOffset];

            Assert.True(
                onTheWire <= WrittenOptionEntries,
                $"stored EmblemSocketCount {stored} reached the wire as {onTheWire}, which tells "
                    + $"the client to read {onTheWire} option dwords where the writer wrote "
                    + $"{WrittenOptionEntries}.");
        }
    }

    /// <summary>
    /// The count in front of the emblem sub-array, which is bounded by how it is built rather than
    /// by a clamp.
    /// </summary>
    /// <remarks>
    /// It counts the non-zero members of a two-element set, so it cannot exceed two however the
    /// stored chronicle options read. This is asserted rather than assumed because the bound is the
    /// only thing keeping it in step with the two ids written after it, and nothing else says so.
    /// <para>
    /// Unlike the count at 0x2F this one is not a crash vector: the client walks it into a
    /// <c>push_back</c> on a growing vector rather than a fixed stack buffer
    /// (<c>vmp1-9.md</c> section four). It would still be a lie about the record's contents.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(7, 0, 1)]
    [InlineData(0, 9, 1)]
    [InlineData(7, 9, 2)]
    [InlineData(int.MaxValue, int.MinValue, 2)]
    public void TheEmblemCountCountsTheTwoChronicleIdsAndCannotExceedThem(
        int firstId, int secondId, byte expected)
    {
        var body = Body(Core(chronicle0Id: firstId, chronicle1Id: secondId));

        Assert.Equal(expected, body[HeaderLength + EmblemCountOffset]);
        Assert.True(body[HeaderLength + EmblemCountOffset] <= 2);
    }

    private static byte[] Body(InventoryItemCoreState core) =>
        new InventoryDeltaMessage(
            InventoryContainerType.Main,
            [new InventoryItemState(new ItemIdentity(1), 3, core)],
            PreparedAt).EncodeBody();

    private static InventoryItemCoreState Core(
        byte emblemSocketCount = 0,
        int chronicle0Id = 0,
        int chronicle1Id = 0) => new(
        2, 1001, 3, 4, 50, 1, 6, 7, 8, 9, -1,
        new ChronicleOptionState(chronicle0Id, 0, 0, 0, 0),
        new ChronicleOptionState(chronicle1Id, 0, 0, 0, 0),
        0, emblemSocketCount, 0, 0, 0,
        new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0),
        0, 0xFF, 0, new RandomOptionState(0, 0, 0),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
