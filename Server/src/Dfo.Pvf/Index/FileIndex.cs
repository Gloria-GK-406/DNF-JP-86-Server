using System.Buffers.Binary;
using System.Text;
using Dfo.Pvf.Container;
using Dfo.Pvf.Strings;

namespace Dfo.Pvf.Index;

/// <summary>One file-table row, read straight from the mapped table.</summary>
public readonly record struct PvfFileEntry(
    int NameOffset,
    int PathOffset,
    int ChunkIndex,
    int DataOffset,
    int DataSize,
    int DataType)
{
    /// <summary>Token-stream script, five bytes per record.</summary>
    public const int ScriptDataType = 1;

    /// <summary>UTF-16 text.</summary>
    public const int UnicodeTextDataType = 3;

    public bool IsScript => DataType == ScriptDataType;

    public bool IsUnicodeText => DataType == UnicodeTextDataType;
}

/// <summary>
/// Locates files by archive path.
/// </summary>
/// <remarks>
/// The lookup key is a hash of the lowercased <c>path/name</c> bytes read directly out
/// of the string pools. Nothing concatenates or materialises a path per entry, which is
/// what made the legacy index cost 87.6 MB for this archive. Hash hits are verified
/// against the real bytes, so a collision cannot return the wrong file.
/// </remarks>
internal sealed class FileIndex
{
    private const ulong FnvOffsetBasis = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;

    private readonly PvfFileMap _map;
    private readonly long _tableOffset;
    private readonly NameTable _names;
    private readonly Dictionary<ulong, int> _primary;
    private readonly Dictionary<ulong, List<int>> _collisions = [];

    public int Count { get; }

    public FileIndex(PvfFileMap map, in PvfHeader header, in SectionLayout layout, NameTable names)
    {
        _map = map;
        _tableOffset = layout.FileTableOffset;
        _names = names;
        Count = header.FileCount;
        _primary = new Dictionary<ulong, int>(Count);

        // Hashing a path prefix is shared by every file under it, so memoise the running
        // hash state after the directory and the separator.
        var prefixStates = new Dictionary<int, ulong>();

        for (var i = 0; i < Count; i++)
        {
            var entry = GetEntry(i);

            if (!prefixStates.TryGetValue(entry.PathOffset, out var prefix))
            {
                prefix = HashSegment(FnvOffsetBasis, entry.PathOffset);
                if (prefix != FnvOffsetBasis)
                    prefix = HashByte(prefix, (byte)'/');
                prefixStates[entry.PathOffset] = prefix;
            }

            var key = HashSegment(prefix, entry.NameOffset);
            if (_primary.TryAdd(key, i))
                continue;

            if (!_collisions.TryGetValue(key, out var bucket))
                _collisions[key] = bucket = [];
            bucket.Add(i);
        }
    }

    /// <summary>Reads one file-table row. No allocation; the table stays in the mapping.</summary>
    public PvfFileEntry GetEntry(int index)
    {
        if ((uint)index >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var row = _map.Slice(_tableOffset + ((long)index * PvfHeader.FileEntrySize), PvfHeader.FileEntrySize);
        return new PvfFileEntry(
            BinaryPrimitives.ReadInt32LittleEndian(row),
            BinaryPrimitives.ReadInt32LittleEndian(row[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(row[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(row[12..]),
            BinaryPrimitives.ReadInt32LittleEndian(row[16..]),
            BinaryPrimitives.ReadInt32LittleEndian(row[20..]));
    }

    /// <summary>Resolves an archive path to a file index, or -1 when it is absent.</summary>
    public int Find(string archivePath)
    {
        var normalized = NormalizePath(archivePath);
        if (normalized.Length == 0)
            return -1;

        Span<byte> stackBuffer = stackalloc byte[256];
        var maxBytes = Encoding.UTF8.GetMaxByteCount(normalized.Length);
        var buffer = maxBytes <= stackBuffer.Length
            ? stackBuffer
            : new byte[maxBytes];

        var written = Encoding.UTF8.GetBytes(normalized, buffer);
        var query = buffer[..written];
        for (var i = 0; i < query.Length; i++)
            query[i] = ToLowerAscii(query[i]);

        var key = FnvOffsetBasis;
        foreach (var b in query)
            key = HashByte(key, b);

        if (!_primary.TryGetValue(key, out var candidate))
            return -1;

        if (Matches(candidate, query))
            return candidate;

        if (_collisions.TryGetValue(key, out var bucket))
        {
            foreach (var index in bucket)
            {
                if (Matches(index, query))
                    return index;
            }
        }

        return -1;
    }

    /// <summary>Compares an entry's real path bytes with a lowercased query.</summary>
    private bool Matches(int index, ReadOnlySpan<byte> query)
    {
        var entry = GetEntry(index);
        var path = SegmentBytes(entry.PathOffset, out var pathRented);
        var name = SegmentBytes(entry.NameOffset, out var nameRented);

        var expectedLength = path.Length + (path.Length > 0 ? 1 : 0) + name.Length;
        if (expectedLength != query.Length)
            return false;

        var cursor = 0;
        for (var i = 0; i < path.Length; i++)
        {
            if (ToLowerAscii(path[i]) != query[cursor++])
                return false;
        }

        if (path.Length > 0 && query[cursor++] != (byte)'/')
            return false;

        for (var i = 0; i < name.Length; i++)
        {
            if (ToLowerAscii(name[i]) != query[cursor++])
                return false;
        }

        GC.KeepAlive(pathRented);
        GC.KeepAlive(nameRented);
        return true;
    }

    private ulong HashSegment(ulong state, int encodedOffset)
    {
        var bytes = SegmentBytes(encodedOffset, out var rented);
        foreach (var b in bytes)
            state = HashByte(state, ToLowerAscii(b));

        GC.KeepAlive(rented);
        return state;
    }

    /// <summary>
    /// UTF-8 names are hashed and compared in place. UTF-16 names are rare in practice,
    /// so they are transcoded through a temporary array rather than complicating the
    /// fast path.
    /// </summary>
    private ReadOnlySpan<byte> SegmentBytes(int encodedOffset, out byte[]? rented)
    {
        if (NameTable.IsUtf8(encodedOffset))
        {
            rented = null;
            return _names.Utf8Bytes(encodedOffset);
        }

        var text = _names.Resolve(encodedOffset);
        rented = Encoding.UTF8.GetBytes(text);
        return rented;
    }

    private static ulong HashByte(ulong state, byte value) => (state ^ value) * FnvPrime;

    private static byte ToLowerAscii(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value == (byte)'\\' ? (byte)'/' : value;

    internal static string NormalizePath(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
            return string.Empty;

        var normalized = archivePath.Replace('\\', '/').Trim();
        var start = 0;
        while (start < normalized.Length)
        {
            if (normalized[start] == '/')
                start++;
            else if (normalized.AsSpan(start).StartsWith("./", StringComparison.Ordinal))
                start += 2;
            else
                break;
        }

        var end = normalized.Length;
        while (end > start && normalized[end - 1] == '/')
            end--;

        return normalized[start..end];
    }
}
