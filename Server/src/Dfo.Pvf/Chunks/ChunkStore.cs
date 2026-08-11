using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using Dfo.Pvf.Codec;
using Dfo.Pvf.Container;
using Dfo.Pvf.Diagnostics;

namespace Dfo.Pvf.Chunks;

/// <summary>
/// Decodes body chunks on demand and keeps recently used ones in a byte-capped cache.
/// </summary>
/// <remarks>
/// <para>
/// Decoded chunks are plain allocations that are never returned to a pool. Archive
/// content is immutable, so a span handed to a caller keeps reading the same bytes
/// even after its chunk is evicted; the buffer simply becomes unreachable and is
/// collected. That removes the use-after-recycle hazard a pooled cache would
/// introduce, and with it the need for leases or reference counting.
/// </para>
/// <para>
/// Only the transient decrypt scratch buffer is pooled. It never escapes this class.
/// </para>
/// </remarks>
internal sealed class ChunkStore
{
    private const string BodyKey = "BodY";

    private readonly PvfFileMap _map;
    private readonly SectionLayout _layout;
    private readonly PvfMetrics _metrics;
    private readonly long _capacityBytes;

    /// <summary>Cumulative compressed offsets and per-chunk decompressed sizes.</summary>
    private readonly int[] _cumulativeCompressed;
    private readonly int[] _decompressedSize;

    private readonly Lock _gate = new();
    private readonly Dictionary<int, LinkedListNode<Entry>> _entries;
    private readonly LinkedList<Entry> _recency = new();
    private long _residentBytes;

    public int ChunkCount => _decompressedSize.Length;

    public ChunkStore(
        PvfFileMap map,
        in PvfHeader header,
        in SectionLayout layout,
        long capacityBytes,
        PvfMetrics metrics)
    {
        _map = map;
        _layout = layout;
        _metrics = metrics;
        _capacityBytes = capacityBytes;

        var count = header.ChunkCount;
        _cumulativeCompressed = new int[count];
        _decompressedSize = new int[count];
        _entries = new Dictionary<int, LinkedListNode<Entry>>(Math.Min(count, 1024));

        var table = ArrayPool<byte>.Shared.Rent(layout.ChunkTableLength);
        try
        {
            _map.Slice(layout.ChunkTableOffset, layout.ChunkTableLength).CopyTo(table);
            PvfCipher.DecryptSection("GRPI", table.AsSpan(0, layout.ChunkTableLength));

            for (var i = 0; i < count; i++)
            {
                var entry = table.AsSpan(i * PvfHeader.ChunkEntrySize, PvfHeader.ChunkEntrySize);
                _cumulativeCompressed[i] = BinaryPrimitives.ReadInt32LittleEndian(entry);
                _decompressedSize[i] = BinaryPrimitives.ReadInt32LittleEndian(entry[4..]);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(table);
        }
    }

    /// <summary>Returns the decoded bytes of one chunk, decoding and admitting it on a miss.</summary>
    public byte[] Get(int chunkIndex)
    {
        if ((uint)chunkIndex >= (uint)_decompressedSize.Length)
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));

        lock (_gate)
        {
            if (_entries.TryGetValue(chunkIndex, out var cached))
            {
                _recency.Remove(cached);
                _recency.AddFirst(cached);
                _metrics.RecordHit();
                return cached.Value.Data;
            }
        }

        // Decode outside the lock. A concurrent miss on the same chunk decodes twice
        // and one result is discarded; that is cheaper than serialising every miss.
        var timestamp = Stopwatch.GetTimestamp();
        var decoded = Decode(chunkIndex);
        _metrics.RecordMiss(Stopwatch.GetElapsedTime(timestamp));

        lock (_gate)
        {
            if (_entries.TryGetValue(chunkIndex, out var raced))
            {
                _recency.Remove(raced);
                _recency.AddFirst(raced);
                return raced.Value.Data;
            }

            var node = _recency.AddFirst(new Entry(chunkIndex, decoded));
            _entries[chunkIndex] = node;
            _residentBytes += decoded.Length;
            _metrics.RecordAdmission(decoded.Length);

            EvictWhileOverCapacity();
            return decoded;
        }
    }

    private void EvictWhileOverCapacity()
    {
        while (_residentBytes > _capacityBytes && _recency.Count > 1)
        {
            var oldest = _recency.Last;
            if (oldest is null)
                return;

            _recency.RemoveLast();
            _entries.Remove(oldest.Value.ChunkIndex);
            _residentBytes -= oldest.Value.Data.Length;
            _metrics.RecordEviction(oldest.Value.Data.Length);
        }
    }

    private byte[] Decode(int chunkIndex)
    {
        var start = chunkIndex == 0 ? 0 : _cumulativeCompressed[chunkIndex - 1];
        var end = _cumulativeCompressed[chunkIndex];
        var compressedLength = end - start;

        if (compressedLength <= 0)
            return [];

        var scratch = ArrayPool<byte>.Shared.Rent(compressedLength);
        try
        {
            _map.Slice(_layout.BodyOffset + start, compressedLength).CopyTo(scratch);
            PvfCipher.DecryptSection(BodyKey, scratch.AsSpan(0, compressedLength));
            return ZlibCodec.Inflate(scratch, 0, compressedLength, _decompressedSize[chunkIndex]);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            throw new PvfFormatException($"Chunk {chunkIndex} could not be decoded.", ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    private readonly record struct Entry(int ChunkIndex, byte[] Data);
}
