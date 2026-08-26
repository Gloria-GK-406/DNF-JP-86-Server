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

    /// <summary>
    /// The archive that will be opened, or null when none of the locations holds one.
    /// </summary>
    /// <remarks>
    /// Answerable without opening the archive, because the question is asked at startup and the
    /// archive is opened lazily on purpose. It is worth asking there: two clients live side by
    /// side in this workspace and their archives disagree about what a job starts with, so a
    /// server reading the wrong one still runs, still answers, and dresses every character it
    /// makes in another client's content. Which file was chosen belongs in the log rather than in
    /// whoever last set the environment variable.
    /// </remarks>
    public string? ResolveArchivePath()
    {
        if (!string.IsNullOrWhiteSpace(ArchivePath) && File.Exists(ArchivePath))
            return ArchivePath;

        var configured = Environment.GetEnvironmentVariable(PathVariable);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(directory.FullName, "Data", "Pvf", "Script.pvf"),
                         Path.Combine(directory.FullName, "Script.pvf"),
                     })
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
