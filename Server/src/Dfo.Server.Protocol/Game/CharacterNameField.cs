using System.Buffers.Binary;

namespace Dfo.Server.Protocol.Game;

/// <summary>
/// The length-prefixed character name shared by the name check, creation and deletion requests.
/// </summary>
/// <remarks>
/// All three encode it identically -- a little-endian <c>i32</c> byte count followed by that many
/// raw bytes -- and differ only in what precedes it. The count is a byte count, not a character
/// count, and the bytes are handed on undecoded: the domain decides what is a legal name, the wire
/// layer only decides where the field ends.
///
/// The declared length is never trusted. It is checked against the bytes actually present before
/// any slice is taken, so a frame claiming a huge name cannot read past its own body.
/// </remarks>
internal static class CharacterNameField
{
    public const int LengthPrefixSize = sizeof(int);

    /// <summary>Reads the name starting at <paramref name="offset"/>, or fails cleanly.</summary>
    public static bool TryRead(ReadOnlySpan<byte> body, int offset, out byte[] name)
    {
        name = [];
        if (offset < 0 || body.Length < offset + LengthPrefixSize)
        {
            return false;
        }

        var declaredLength = BinaryPrimitives.ReadInt32LittleEndian(body[offset..]);
        if (declaredLength <= 0)
        {
            return false;
        }

        // Compared by subtraction, never by adding the declared length to the offset. The length is
        // attacker-controlled, and `start + declaredLength` overflows to a negative number for
        // values near int.MaxValue, which would make the bounds check pass and the slice throw.
        // `body.Length - start` cannot go negative: the prefix check above proved start <= Length.
        var start = offset + LengthPrefixSize;
        if (declaredLength > body.Length - start)
        {
            return false;
        }

        name = body.Slice(start, declaredLength).ToArray();
        return true;
    }
}
