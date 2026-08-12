using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Dfo.Pvf.Codec;
using Dfo.Pvf.Container;

namespace Dfo.Pvf.Strings;

/// <summary>
/// The archive's two string pools. Offsets carry their encoding in the low bit:
/// even offsets index the UTF-8 pool, odd offsets the UTF-16 pool.
/// </summary>
/// <remarks>
/// Strings are resolved on demand. Nothing is materialised up front, because
/// only a small fraction of the pools is ever queried and the pools together
/// hold hundreds of thousands of entries.
/// </remarks>
public sealed class NameTable
{
    private const uint Utf8Xor = 0xAA74472E;
    private const uint Utf16Xor = 0x9A82F037;
    private const ulong FnvOffsetBasis = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;

    private readonly byte[] _utf8Pool;
    private readonly byte[] _utf16Pool;
    private Dictionary<ulong, List<int>>? _reverseUtf8TagIndex;

    private NameTable(byte[] utf8Pool, byte[] utf16Pool)
    {
        _utf8Pool = utf8Pool;
        _utf16Pool = utf16Pool;
    }

    internal static NameTable Read(PvfFileMap map, in SectionLayout layout, int nameTableSize)
    {
        if (nameTableSize < 16)
            return new NameTable([0], [0, 0]);

        var section = ArrayPool<byte>.Shared.Rent(nameTableSize);
        try
        {
            map.Slice(layout.NameTableOffset, nameTableSize).CopyTo(section);

            // The first eight bytes are a section preamble the reader does not need.
            var cursor = 8;
            var utf8 = ReadPool(section, nameTableSize, ref cursor, "sTrA", Utf8Xor);
            var utf16 = ReadPool(section, nameTableSize, ref cursor, "sTrW", Utf16Xor);
            return new NameTable(utf8, utf16);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(section);
        }
    }

    private static byte[] ReadPool(byte[] section, int sectionLength, ref int cursor, string key, uint xor)
    {
        if (cursor + 8 > sectionLength)
            return [];

        var encodedLength = (int)(BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan(cursor)) ^ xor);
        cursor += 4;
        var lengthCheck = BinaryPrimitives.ReadInt32LittleEndian(section.AsSpan(cursor));
        cursor += 4;

        if (encodedLength <= 0 || cursor + encodedLength > sectionLength)
            return [];

        PvfCipher.DecryptStringTable(key, section.AsSpan(cursor, encodedLength));

        // The writer stores rawLength ^ encodedLength, giving the exact inflated size.
        var declaredRawLength = lengthCheck ^ encodedLength;
        byte[] pool;
        try
        {
            pool = declaredRawLength > 0
                ? ZlibCodec.Inflate(section, cursor, encodedLength, declaredRawLength)
                : ZlibCodec.InflateUnknownLength(section, cursor, encodedLength);
        }
        catch (InvalidDataException)
        {
            pool = ZlibCodec.InflateUnknownLength(section, cursor, encodedLength);
        }

        cursor += encodedLength;
        return pool;
    }

    /// <summary>Materialises the string at an encoded offset, or empty when it is absent.</summary>
    public string Resolve(int encodedOffset)
    {
        if (encodedOffset < 0)
            return string.Empty;

        if ((encodedOffset & 1) != 0)
        {
            var span = Utf16Bytes(encodedOffset);
            return span.IsEmpty ? string.Empty : Encoding.Unicode.GetString(span);
        }

        var utf8 = Utf8Bytes(encodedOffset);
        return utf8.IsEmpty ? string.Empty : Encoding.UTF8.GetString(utf8);
    }

    /// <summary>True when the offset addresses the UTF-8 pool.</summary>
    public static bool IsUtf8(int encodedOffset) => encodedOffset >= 0 && (encodedOffset & 1) == 0;

    /// <summary>
    /// Finds the encoded offset of an exact bracket-delimited UTF-8 tag, so a caller can
    /// resolve the tag once and then match it by integer comparison.
    /// </summary>
    /// <remarks>
    /// The reverse index is built on first use only and contains bracket-delimited tags,
    /// not the hundreds of thousands of file and resource names in the same string pool.
    /// Archives whose caller never resolves a tag never pay for it.
    /// </remarks>
    public bool TryFindUtf8(ReadOnlySpan<byte> text, out int encodedOffset)
    {
        encodedOffset = -1;
        if (text.Length < 2 || text[0] != (byte)'[' || text[^1] != (byte)']')
            return false;

        var index = _reverseUtf8TagIndex ?? BuildReverseUtf8TagIndex();

        var hash = FnvOffsetBasis;
        foreach (var b in text)
            hash = (hash ^ b) * FnvPrime;

        if (!index.TryGetValue(hash, out var offsets))
            return false;

        foreach (var candidate in offsets)
        {
            if (Utf8Bytes(candidate).SequenceEqual(text))
            {
                encodedOffset = candidate;
                return true;
            }
        }

        return false;
    }

    private Dictionary<ulong, List<int>> BuildReverseUtf8TagIndex()
    {
        var index = new Dictionary<ulong, List<int>>();
        var position = 0;

        while (position < _utf8Pool.Length)
        {
            var terminator = Array.IndexOf(_utf8Pool, (byte)0, position);
            var end = terminator < 0 ? _utf8Pool.Length : terminator;

            if (end - position >= 2 &&
                _utf8Pool[position] == (byte)'[' &&
                _utf8Pool[end - 1] == (byte)']')
            {
                var hash = FnvOffsetBasis;
                for (var i = position; i < end; i++)
                    hash = (hash ^ _utf8Pool[i]) * FnvPrime;

                if (!index.TryGetValue(hash, out var bucket))
                    index[hash] = bucket = [];
                bucket.Add(position << 1);
            }

            if (terminator < 0)
                break;
            position = terminator + 1;
        }

        _reverseUtf8TagIndex = index;
        return index;
    }

    /// <summary>
    /// Returns the raw UTF-8 bytes for an even offset without allocating, so callers
    /// can compare or hash a name without materialising it.
    /// </summary>
    public ReadOnlySpan<byte> Utf8Bytes(int encodedOffset)
    {
        if (encodedOffset < 0 || (encodedOffset & 1) != 0)
            return default;

        var start = encodedOffset >> 1;
        if ((uint)start >= (uint)_utf8Pool.Length)
            return default;

        var terminator = Array.IndexOf(_utf8Pool, (byte)0, start);
        var end = terminator < 0 ? _utf8Pool.Length : terminator;
        return end <= start ? default : _utf8Pool.AsSpan(start, end - start);
    }

    private ReadOnlySpan<byte> Utf16Bytes(int encodedOffset)
    {
        var start = (encodedOffset >> 1) * 2;
        if ((uint)start >= (uint)_utf16Pool.Length)
            return default;

        for (var i = start; i + 1 < _utf16Pool.Length; i += 2)
        {
            if (_utf16Pool[i] == 0 && _utf16Pool[i + 1] == 0)
                return i <= start ? default : _utf16Pool.AsSpan(start, i - start);
        }

        var remaining = (_utf16Pool.Length - start) / 2 * 2;
        return remaining > 0 ? _utf16Pool.AsSpan(start, remaining) : default;
    }
}
