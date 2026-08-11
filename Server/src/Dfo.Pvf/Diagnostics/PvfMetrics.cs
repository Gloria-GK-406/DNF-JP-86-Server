namespace Dfo.Pvf.Diagnostics;

/// <summary>
/// Chunk-cache counters. The cache is the only part of the read path whose cost
/// depends on runtime access order, so its hit rate is what sizing decisions need.
/// </summary>
public sealed class PvfMetrics
{
    private long _hits;
    private long _misses;
    private long _evictions;
    private long _decodeTicks;
    private long _residentBytes;

    public long ChunkHits => Interlocked.Read(ref _hits);

    public long ChunkMisses => Interlocked.Read(ref _misses);

    public long ChunkEvictions => Interlocked.Read(ref _evictions);

    /// <summary>Bytes of decoded chunk data currently held by the cache.</summary>
    public long ResidentBytes => Interlocked.Read(ref _residentBytes);

    public TimeSpan TotalDecodeTime => TimeSpan.FromTicks(Interlocked.Read(ref _decodeTicks));

    /// <summary>Share of chunk lookups served without decoding, or 1 when nothing was requested.</summary>
    public double HitRate
    {
        get
        {
            var hits = ChunkHits;
            var total = hits + ChunkMisses;
            return total == 0 ? 1d : (double)hits / total;
        }
    }

    internal void RecordHit() => Interlocked.Increment(ref _hits);

    internal void RecordMiss(TimeSpan decodeTime)
    {
        Interlocked.Increment(ref _misses);
        Interlocked.Add(ref _decodeTicks, decodeTime.Ticks);
    }

    internal void RecordEviction(int bytes)
    {
        Interlocked.Increment(ref _evictions);
        Interlocked.Add(ref _residentBytes, -bytes);
    }

    internal void RecordAdmission(int bytes) => Interlocked.Add(ref _residentBytes, bytes);

    public override string ToString() =>
        $"hits={ChunkHits} misses={ChunkMisses} evictions={ChunkEvictions} " +
        $"hitRate={HitRate:P1} resident={ResidentBytes / 1024d / 1024d:F1}MB " +
        $"decode={TotalDecodeTime.TotalMilliseconds:F0}ms";
}
