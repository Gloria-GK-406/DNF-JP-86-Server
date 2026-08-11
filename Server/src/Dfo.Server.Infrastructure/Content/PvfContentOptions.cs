namespace Dfo.Server.Infrastructure.Content;

/// <summary>Where the client archive lives, and how much of it to keep decoded.</summary>
public sealed class PvfContentOptions
{
    /// <summary>Environment variable consulted before the conventional locations.</summary>
    public const string PathVariable = "PVF_ARCHIVE_PATH";

    /// <summary>
    /// Explicit archive path. When empty, the conventional locations are searched:
    /// <c>Data/Pvf/Script.pvf</c> beside the executable, then the repository root.
    /// </summary>
    public string? ArchivePath { get; init; }

    /// <summary>Upper bound on decoded chunk bytes. Zero keeps the library default.</summary>
    public long ChunkCacheBytes { get; init; }
}
