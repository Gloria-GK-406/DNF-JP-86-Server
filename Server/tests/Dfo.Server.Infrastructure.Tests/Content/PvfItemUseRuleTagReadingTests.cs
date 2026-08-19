using System.Buffers.Binary;
using Dfo.Pvf.Tokens;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// Telling "the archive does not declare this" apart from "it declares it and I cannot read it".
/// </summary>
/// <remarks>
/// The real archive cannot exercise the second: all 34,542 of its stackables parse. That is exactly
/// why this is worth holding down here -- the branch protects against an archive that does not
/// exist yet, so nothing else would notice if it stopped working.
///
/// The streams are synthetic. A tag is an identifier whose value is its name-table offset, which is
/// all <see cref="TagRef"/> compares, so a section can be built without an archive behind it.
///
/// That is also the limit of what these prove. A synthetic stream says nothing about how the
/// archive actually encodes <c>[waste]</c> or <c>[village]</c> -- were those identifiers rather than
/// quoted strings, every item's stackable type would read as unreadable and these would all still
/// pass. <see cref="PvfItemUseRuleDefinitionSourceTests"/> is what holds that half down, against the
/// real file. Neither set is redundant with the other.
/// </remarks>
public sealed class PvfItemUseRuleTagReadingTests
{
    private const int TagOffset = 7;
    private const int OtherTagOffset = 9;

    private static readonly TagRef Tag = new(TagOffset);

    [Fact]
    public void An_undeclared_integer_tag_is_absence_not_an_unreadable_declaration()
    {
        var script = Script((PvfTokenKind.Identifier, OtherTagOffset), (PvfTokenKind.Int32, 5));
        var reader = new PvfTokenReader(script);
        var unreadable = false;

        var value = PvfItemUseRuleDefinitionSource.FirstInt32(ref reader, Tag, ref unreadable);

        Assert.Equal(0, value);
        Assert.False(unreadable);
    }

    [Fact]
    public void A_declared_integer_tag_with_no_integer_behind_it_is_unreadable()
    {
        var script = Script((PvfTokenKind.Identifier, TagOffset), (PvfTokenKind.Identifier, OtherTagOffset));
        var reader = new PvfTokenReader(script);
        var unreadable = false;

        var value = PvfItemUseRuleDefinitionSource.FirstInt32(ref reader, Tag, ref unreadable);

        Assert.Equal(0, value);
        Assert.True(unreadable);
    }

    [Fact]
    public void A_declared_integer_tag_that_reads_leaves_the_rule_readable()
    {
        var script = Script((PvfTokenKind.Identifier, TagOffset), (PvfTokenKind.Int32, 10_000));
        var reader = new PvfTokenReader(script);
        var unreadable = false;

        var value = PvfItemUseRuleDefinitionSource.FirstInt32(ref reader, Tag, ref unreadable);

        Assert.Equal(10_000, value);
        Assert.False(unreadable);
    }

    [Fact]
    public void An_undeclared_string_tag_is_absence_not_an_unreadable_declaration()
    {
        var script = Script((PvfTokenKind.Identifier, OtherTagOffset), (PvfTokenKind.QuotedString, 3));
        var reader = new PvfTokenReader(script);
        var unreadable = false;

        Assert.False(PvfItemUseRuleDefinitionSource.TryReadFirstString(
            ref reader, Tag, out _, ref unreadable));
        Assert.False(unreadable);
    }

    /// <remarks>
    /// The sharpest case in the whole rule: an unreadable <c>[stackable type]</c> read as absence
    /// means "consumed", so an item the archive marked unlimited would be destroyed one use at a
    /// time -- the very defect this outcome exists to remove.
    /// </remarks>
    [Fact]
    public void A_declared_string_tag_with_no_string_behind_it_is_unreadable()
    {
        var script = Script((PvfTokenKind.Identifier, TagOffset), (PvfTokenKind.Identifier, OtherTagOffset));
        var reader = new PvfTokenReader(script);
        var unreadable = false;

        Assert.False(PvfItemUseRuleDefinitionSource.TryReadFirstString(
            ref reader, Tag, out _, ref unreadable));
        Assert.True(unreadable);
    }

    [Fact]
    public void A_declared_string_tag_that_reads_returns_its_token()
    {
        var script = Script((PvfTokenKind.Identifier, TagOffset), (PvfTokenKind.QuotedString, 42));
        var reader = new PvfTokenReader(script);
        var unreadable = false;

        Assert.True(PvfItemUseRuleDefinitionSource.TryReadFirstString(
            ref reader, Tag, out var value, ref unreadable));
        Assert.Equal(42, value.Value);
        Assert.False(unreadable);
    }

    private static byte[] Script(params (PvfTokenKind Kind, int Value)[] tokens)
    {
        var bytes = new byte[tokens.Length * PvfToken.Size];
        for (var i = 0; i < tokens.Length; i++)
        {
            bytes[i * PvfToken.Size] = (byte)tokens[i].Kind;
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan((i * PvfToken.Size) + 1, 4), tokens[i].Value);
        }

        return bytes;
    }
}
