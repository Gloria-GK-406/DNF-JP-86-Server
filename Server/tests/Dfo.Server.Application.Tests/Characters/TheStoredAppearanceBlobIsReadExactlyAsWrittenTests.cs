using System.IO;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Game;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// Reading a stored appearance blob back into the entries it was written from, and refusing the two
/// shapes that are not one.
/// </summary>
/// <remarks>
/// <b>Nothing named this decoder before.</b> Its two refusals had never had a case at all, and its
/// reading of a non-empty blob was covered only in passing, by a lifecycle case that wrote a part
/// into <c>characters.appearance_blob</c> and asserted the roster handed it back. That case was
/// replaced -- rightly, because the assertion was a statement of the defect -- and the coverage it
/// happened to provide went with it.
/// <para>
/// The values below are chosen so that the first case cannot pass a decoder that fills the right
/// members from the wrong place in the entry, or reads one at the wrong width: no two members carry
/// the same value and none is zero, so a member taken from a neighbouring offset, or left unread and
/// defaulted, shows up. The expansion length is deliberately not 4, because the decoder does not use
/// it to size the four expansion bytes and a case that made them agree would hide that.
/// </para>
/// <para>
/// <b>Signedness is not among the things these cases can catch, and none of them claims to.</b>
/// <see cref="CharacterRosterAppearanceState.EnchantValue"/> is a <c>uint</c>, so a signed read has
/// to be converted back and the conversion is bit-preserving -- replacing <c>ReadUInt32</c> with
/// <c>(uint)ReadInt32</c> leaves every case here green. That was measured, not assumed. Its value
/// being above <see cref="int.MaxValue"/> pins the four bytes it occupies; it does not pin how they
/// are read.
/// </para>
/// <para>
/// The two refusals differ only in their message, so the cases below assert the message itself.
/// Asserting merely that <see cref="InvalidDataException"/> was raised would let either refusal
/// stand in for the other.
/// </para>
/// </remarks>
public sealed class TheStoredAppearanceBlobIsReadExactlyAsWrittenTests
{
    private const string Truncated = "Character appearance blob is truncated.";
    private const string Trailing = "Character appearance blob contains trailing bytes.";

    private static readonly CharacterRosterAppearanceState First = new(
        Slot: 7,
        DisplayItemId: 305419896,
        ExpansionLength: 9,
        ExpansionData: [0xA1, 0xB2, 0xC3, 0xD4],
        State: 3,
        LinkItemId: 654321,
        EnchantValue: 3_000_000_000,
        Flag20: 200);

    private static readonly CharacterRosterAppearanceState Second = new(
        Slot: 23,
        DisplayItemId: 1000003,
        ExpansionLength: 11,
        ExpansionData: [0x0E, 0x1D, 0x2C, 0x3B],
        State: 5,
        LinkItemId: 44444,
        EnchantValue: 4_000_000_017,
        Flag20: 99);

    [Fact]
    public void Every_part_of_an_entry_comes_back_as_it_was_written()
    {
        var decoded = Assert.Single(
            SelectCharacterPreparationService.DecodeAppearances(Blob(First)));

        Assert.Equal(First.Slot, decoded.Slot);
        Assert.Equal(First.DisplayItemId, decoded.DisplayItemId);
        Assert.Equal(First.ExpansionLength, decoded.ExpansionLength);
        Assert.Equal(First.ExpansionData, decoded.ExpansionData);
        Assert.Equal(First.State, decoded.State);
        Assert.Equal(First.LinkItemId, decoded.LinkItemId);
        Assert.Equal(First.EnchantValue, decoded.EnchantValue);
        Assert.Equal(First.Flag20, decoded.Flag20);
    }

    [Fact]
    public void Two_entries_come_back_in_the_order_they_were_written()
    {
        var decoded = SelectCharacterPreparationService.DecodeAppearances(Blob(First, Second));

        Assert.Equal(2, decoded.Count);
        Assert.Equal(First.Slot, decoded[0].Slot);
        Assert.Equal(First.EnchantValue, decoded[0].EnchantValue);
        Assert.Equal(Second.Slot, decoded[1].Slot);
        Assert.Equal(Second.EnchantValue, decoded[1].EnchantValue);
    }

    [Fact]
    public void A_blob_that_promises_more_than_it_carries_is_refused_as_truncated()
    {
        byte[] blob = [1, 0x11, 0x22, 0x33, 0x44, 0x55];

        var refusal = Assert.Throws<InvalidDataException>(
            () => SelectCharacterPreparationService.DecodeAppearances(blob));

        Assert.Equal(Truncated, refusal.Message);
    }

    /// <summary>
    /// The refusal survives a blob whose first entry reads fine and whose second falls short.
    /// </summary>
    /// <remarks>
    /// A blob too short to hold anything at all would reach the same refusal on its first pass, so
    /// this case adds the half where reading has already succeeded once. It does not prove the
    /// guard sits inside the loop -- an equivalent up-front length check would pass this too.
    /// </remarks>
    [Fact]
    public void The_shortfall_is_found_after_the_entries_that_do_fit_have_been_read()
    {
        var blob = new List<byte>(Blob(First, Second)[..24]) { 0x01 };
        blob[0] = 2;

        var refusal = Assert.Throws<InvalidDataException>(
            () => SelectCharacterPreparationService.DecodeAppearances([.. blob]));

        Assert.Equal(Truncated, refusal.Message);
    }

    [Fact]
    public void A_blob_carrying_more_than_it_declared_is_refused_for_its_trailing_bytes()
    {
        var blob = new List<byte>(Blob(First)) { 0x77, 0x88, 0x99 };

        var refusal = Assert.Throws<InvalidDataException>(
            () => SelectCharacterPreparationService.DecodeAppearances([.. blob]));

        Assert.Equal(Trailing, refusal.Message);
    }

    [Fact]
    public void A_blob_with_nothing_in_it_is_no_entries_at_all()
    {
        Assert.Empty(SelectCharacterPreparationService.DecodeAppearances([]));
        Assert.Empty(SelectCharacterPreparationService.DecodeAppearances(null!));
    }

    private static byte[] Blob(params CharacterRosterAppearanceState[] entries)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(checked((byte)entries.Length));
        foreach (var entry in entries)
        {
            writer.Write(entry.Slot);
            writer.Write(entry.DisplayItemId);
            writer.Write(entry.ExpansionLength);
            writer.Write(entry.ExpansionData);
            writer.Write(entry.State);
            writer.Write(entry.LinkItemId);
            writer.Write(entry.EnchantValue);
            writer.Write(entry.Flag20);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
