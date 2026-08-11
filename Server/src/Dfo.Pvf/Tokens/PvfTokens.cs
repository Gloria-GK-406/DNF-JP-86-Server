using System.Buffers.Binary;

namespace Dfo.Pvf.Tokens;

/// <summary>Record type of one script token.</summary>
public enum PvfTokenKind : byte
{
    /// <summary>Signed 32-bit literal.</summary>
    Int32 = 0,

    /// <summary>IEEE-754 single, stored in the value bits.</summary>
    Single = 2,

    /// <summary>Bare identifier. Section tags such as <c>[name]</c> arrive as identifiers.</summary>
    Identifier = 3,

    /// <summary>Backtick-delimited block marker, form <c>{5=`…`}</c>.</summary>
    BlockMarker5 = 5,

    /// <summary>Backtick-quoted string.</summary>
    QuotedString = 6,

    /// <summary>Backtick-delimited block marker, form <c>{7=`…`}</c>.</summary>
    BlockMarker7 = 7,
}

/// <summary>
/// One five-byte script record: a kind and a 32-bit payload. For string kinds the
/// payload is an encoded name-table offset rather than the text itself.
/// </summary>
public readonly record struct PvfToken(PvfTokenKind Kind, int Value)
{
    /// <summary>Bytes occupied by one record.</summary>
    public const int Size = 5;

    public bool IsString =>
        Kind is PvfTokenKind.Identifier or PvfTokenKind.QuotedString
            or PvfTokenKind.BlockMarker5 or PvfTokenKind.BlockMarker7;

    public float AsSingle() => BitConverter.Int32BitsToSingle(Value);
}

/// <summary>
/// A tag name resolved to its name-table offset once, so matching a section during a
/// scan is an integer comparison rather than a decode-and-compare.
/// </summary>
/// <remarks>
/// The archive interns identifiers: every occurrence of a given tag shares one offset,
/// so comparing offsets is equivalent to comparing text.
/// </remarks>
public readonly record struct TagRef(int Offset)
{
    /// <summary>A tag that does not occur anywhere in the archive.</summary>
    public static readonly TagRef Unresolved = new(-1);

    public bool IsResolved => Offset >= 0;

    public bool Matches(in PvfToken token) =>
        IsResolved && token.Kind == PvfTokenKind.Identifier && token.Value == Offset;
}

/// <summary>
/// Forward-only reader over a script file's token records.
/// </summary>
/// <remarks>
/// Declared as a <c>ref struct</c> so it cannot outlive the span it reads, and so it
/// cannot be captured into a field, a lambda or an async state machine.
/// </remarks>
public ref struct PvfTokenReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public PvfTokenReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    /// <summary>Byte offset of the next record.</summary>
    public readonly int Position => _position;

    /// <summary>Total records available.</summary>
    public readonly int Count => _data.Length / PvfToken.Size;

    public void Reset() => _position = 0;

    /// <summary>Positions the reader at a record index.</summary>
    public void Seek(int recordIndex) =>
        _position = Math.Clamp(recordIndex, 0, Count) * PvfToken.Size;

    /// <summary>Reads the next record, returning false at end of file.</summary>
    public bool Read(out PvfToken token)
    {
        if (_position + PvfToken.Size > _data.Length)
        {
            token = default;
            return false;
        }

        var kind = (PvfTokenKind)_data[_position];
        var value = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(_position + 1, 4));
        _position += PvfToken.Size;
        token = new PvfToken(kind, value);
        return true;
    }

    /// <summary>Reads the next record without consuming it.</summary>
    public readonly bool Peek(out PvfToken token)
    {
        var copy = this;
        return copy.Read(out token);
    }

    /// <summary>
    /// Advances to just past the next occurrence of <paramref name="tag"/>.
    /// Returns false when the tag does not occur from the current position.
    /// </summary>
    public bool TryMovePast(TagRef tag)
    {
        if (!tag.IsResolved)
            return false;

        while (Read(out var token))
        {
            if (tag.Matches(token))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reads the next value record of the current section, stopping before the next
    /// identifier. Sections are delimited by identifiers, so this never runs past one.
    /// </summary>
    public bool TryReadSectionValue(out PvfToken token)
    {
        if (!Peek(out var next) || next.Kind == PvfTokenKind.Identifier)
        {
            token = default;
            return false;
        }

        return Read(out token);
    }

    /// <summary>Reads the first integer of the current section.</summary>
    public bool TryReadInt32(out int value)
    {
        while (TryReadSectionValue(out var token))
        {
            if (token.Kind == PvfTokenKind.Int32)
            {
                value = token.Value;
                return true;
            }
        }

        value = 0;
        return false;
    }
}
