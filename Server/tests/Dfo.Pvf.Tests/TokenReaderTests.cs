using System.Buffers.Binary;
using Dfo.Pvf.Tokens;

namespace Dfo.Pvf.Tests;

public sealed class TokenReaderTests
{
    private static byte[] Script(params (PvfTokenKind Kind, int Value)[] tokens)
    {
        var bytes = new byte[tokens.Length * PvfToken.Size];
        for (var i = 0; i < tokens.Length; i++)
        {
            bytes[i * PvfToken.Size] = (byte)tokens[i].Kind;
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan((i * PvfToken.Size) + 1, 4),
                tokens[i].Value);
        }

        return bytes;
    }

    [Fact]
    public void Reads_every_record_in_order()
    {
        var script = Script(
            (PvfTokenKind.Identifier, 100),
            (PvfTokenKind.Int32, 42),
            (PvfTokenKind.QuotedString, 201));

        var reader = new PvfTokenReader(script);

        Assert.Equal(3, reader.Count);
        Assert.True(reader.Read(out var first));
        Assert.Equal(new PvfToken(PvfTokenKind.Identifier, 100), first);
        Assert.True(reader.Read(out var second));
        Assert.Equal(new PvfToken(PvfTokenKind.Int32, 42), second);
        Assert.True(reader.Read(out var third));
        Assert.Equal(new PvfToken(PvfTokenKind.QuotedString, 201), third);
        Assert.False(reader.Read(out _));
    }

    [Fact]
    public void Trailing_partial_record_is_ignored_rather_than_misread()
    {
        var script = Script((PvfTokenKind.Int32, 7)).Concat(new byte[] { 3, 0, 0 }).ToArray();

        var reader = new PvfTokenReader(script);

        Assert.True(reader.Read(out var only));
        Assert.Equal(7, only.Value);
        Assert.False(reader.Read(out _));
    }

    [Fact]
    public void Single_tokens_decode_from_their_value_bits()
    {
        var script = Script((PvfTokenKind.Single, BitConverter.SingleToInt32Bits(1.5f)));

        var reader = new PvfTokenReader(script);

        Assert.True(reader.Read(out var token));
        Assert.Equal(1.5f, token.AsSingle());
    }

    [Fact]
    public void TryMovePast_positions_after_the_tag()
    {
        var script = Script(
            (PvfTokenKind.Identifier, 10),
            (PvfTokenKind.Int32, 1),
            (PvfTokenKind.Identifier, 20),
            (PvfTokenKind.Int32, 99));

        var reader = new PvfTokenReader(script);

        Assert.True(reader.TryMovePast(new TagRef(20)));
        Assert.True(reader.TryReadInt32(out var value));
        Assert.Equal(99, value);
    }

    [Fact]
    public void TryMovePast_reports_absence_without_throwing()
    {
        var reader = new PvfTokenReader(Script((PvfTokenKind.Identifier, 10)));

        Assert.False(reader.TryMovePast(new TagRef(999)));
        Assert.False(reader.TryMovePast(TagRef.Unresolved));
    }

    [Fact]
    public void Section_values_stop_at_the_next_identifier()
    {
        var script = Script(
            (PvfTokenKind.Identifier, 10),
            (PvfTokenKind.Int32, 1),
            (PvfTokenKind.Int32, 2),
            (PvfTokenKind.Identifier, 20),
            (PvfTokenKind.Int32, 3));

        var reader = new PvfTokenReader(script);
        Assert.True(reader.TryMovePast(new TagRef(10)));

        var values = new List<int>();
        while (reader.TryReadSectionValue(out var token))
            values.Add(token.Value);

        Assert.Equal([1, 2], values);
    }

    [Fact]
    public void Unresolved_tag_never_matches()
    {
        var token = new PvfToken(PvfTokenKind.Identifier, -1);

        Assert.False(TagRef.Unresolved.Matches(token));
        Assert.False(TagRef.Unresolved.IsResolved);
    }

    [Fact]
    public void Tag_matching_ignores_non_identifier_records_with_the_same_value()
    {
        var tag = new TagRef(55);

        Assert.True(tag.Matches(new PvfToken(PvfTokenKind.Identifier, 55)));
        Assert.False(tag.Matches(new PvfToken(PvfTokenKind.Int32, 55)));
        Assert.False(tag.Matches(new PvfToken(PvfTokenKind.QuotedString, 55)));
    }
}
