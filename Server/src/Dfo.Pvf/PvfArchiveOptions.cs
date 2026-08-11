namespace Dfo.Pvf;

/// <summary>Tunables for opening an archive.</summary>
public sealed class PvfArchiveOptions
{
    /// <summary>
    /// Upper bound on decoded chunk bytes held in memory.
    /// </summary>
    /// <remarks>
    /// Measured against the shipping archive, every catalog the server builds together
    /// touches 1,225 of 4,558 chunks, which decode to 86.7 MB. The default leaves headroom
    /// above that so a warmed server stops evicting entirely; lower it when memory matters
    /// more than avoiding re-decodes, and read <see cref="Diagnostics.PvfMetrics.HitRate"/>
    /// to see the cost of doing so.
    /// </remarks>
    public long ChunkCacheBytes { get; init; } = 96L * 1024 * 1024;

    internal static PvfArchiveOptions Default { get; } = new();
}
