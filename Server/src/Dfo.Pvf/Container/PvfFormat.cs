using System.Buffers.Binary;

namespace Dfo.Pvf.Container;

/// <summary>Raised when archive bytes do not satisfy the PVF container format.</summary>
public sealed class PvfFormatException : Exception
{
    public PvfFormatException(string message) : base(message) { }

    public PvfFormatException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// The 0x30-byte archive header, already decrypted.
/// </summary>
public readonly struct PvfHeader
{
    /// <summary>Header length in bytes.</summary>
    public const int Size = 0x30;

    /// <summary>Little-endian 'nkpi'.</summary>
    public const uint Signature = 0x69706B6E;

    /// <summary>Bytes occupied by one file-table entry.</summary>
    public const int FileEntrySize = 0x18;

    /// <summary>Bytes occupied by one chunk-table entry.</summary>
    public const int ChunkEntrySize = 8;

    public int FileCount { get; }

    public int BodySize { get; }

    public int ChunkCount { get; }

    public int HashTableSize { get; }

    public int NameTableSize { get; }

    private PvfHeader(int fileCount, int bodySize, int chunkCount, int hashTableSize, int nameTableSize)
    {
        FileCount = fileCount;
        BodySize = bodySize;
        ChunkCount = chunkCount;
        HashTableSize = hashTableSize;
        NameTableSize = nameTableSize;
    }

    /// <summary>Reads the header from already-decrypted bytes and validates its declared layout.</summary>
    public static PvfHeader Read(ReadOnlySpan<byte> decrypted, long archiveLength)
    {
        if (decrypted.Length < Size)
            throw new PvfFormatException($"PVF header needs {Size} bytes, got {decrypted.Length}.");

        var signature = BinaryPrimitives.ReadUInt32LittleEndian(decrypted);
        if (signature != Signature)
            throw new PvfFormatException($"Invalid PVF signature 0x{signature:X8}.");

        // 4 signature + 20 guid, then six little-endian int32 fields.
        var fields = decrypted[24..];
        var fileCount = BinaryPrimitives.ReadInt32LittleEndian(fields);
        // fields[4..8] is padding that the format does not use.
        var bodySize = BinaryPrimitives.ReadInt32LittleEndian(fields[8..]);
        var chunkCount = BinaryPrimitives.ReadInt32LittleEndian(fields[12..]);
        var hashTableSize = BinaryPrimitives.ReadInt32LittleEndian(fields[16..]);
        var nameTableSize = BinaryPrimitives.ReadInt32LittleEndian(fields[20..]);

        if (fileCount < 0 || chunkCount < 0 || bodySize < 0 || hashTableSize < 0 || nameTableSize < 0)
            throw new PvfFormatException("PVF header declares a negative section size.");

        // Every field is a non-negative int, so the widest possible sum is about 7.1e10.
        // Accumulating in long cannot overflow; only the boundary check can fail.
        var declared =
            Size
            + ((long)fileCount * FileEntrySize)
            + hashTableSize
            + nameTableSize
            + ((long)chunkCount * ChunkEntrySize)
            + bodySize;

        if (declared > archiveLength)
            throw new PvfFormatException(
                $"PVF header declares {declared} bytes but the archive is {archiveLength} bytes.");

        return new PvfHeader(fileCount, bodySize, chunkCount, hashTableSize, nameTableSize);
    }
}

/// <summary>Absolute offsets of every archive section, derived from the header.</summary>
public readonly struct SectionLayout
{
    public long FileTableOffset { get; }

    public int FileTableLength { get; }

    public long HashTableOffset { get; }

    public long NameTableOffset { get; }

    public long ChunkTableOffset { get; }

    public int ChunkTableLength { get; }

    public long BodyOffset { get; }

    public SectionLayout(in PvfHeader header)
    {
        FileTableOffset = PvfHeader.Size;
        FileTableLength = header.FileCount * PvfHeader.FileEntrySize;

        HashTableOffset = FileTableOffset + FileTableLength;
        NameTableOffset = HashTableOffset + header.HashTableSize;
        ChunkTableOffset = NameTableOffset + header.NameTableSize;
        ChunkTableLength = header.ChunkCount * PvfHeader.ChunkEntrySize;
        BodyOffset = ChunkTableOffset + ChunkTableLength;
    }
}
