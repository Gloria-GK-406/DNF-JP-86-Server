using Dfo.Pvf;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Owns the single opened archive that every content source reads from.
/// </summary>
/// <remarks>
/// The archive is read-only and safe for concurrent readers, so one instance is shared.
/// It is opened on first use rather than at construction, so a server that never touches
/// static content does not pay for it and does not fail to start without the file.
/// </remarks>
internal sealed class PvfArchiveProvider : IDisposable
{
    private readonly PvfContentOptions _options;
    private readonly Lock _gate = new();
    private PvfArchive? _archive;
    private bool _disposed;

    public PvfArchiveProvider(PvfContentOptions options) => _options = options;

    public PvfArchive Archive
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_archive is not null)
                return _archive;

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _archive ??= Open();
            }
        }
    }

    private PvfArchive Open()
    {
        var path = ResolvePath()
            ?? throw new FileNotFoundException(
                $"Script.pvf was not found. Set {PvfContentOptions.PathVariable}, configure " +
                $"{nameof(PvfContentOptions)}.{nameof(PvfContentOptions.ArchivePath)}, or place the " +
                "archive at Data/Pvf/Script.pvf.");

        var archiveOptions = _options.ChunkCacheBytes > 0
            ? new PvfArchiveOptions { ChunkCacheBytes = _options.ChunkCacheBytes }
            : new PvfArchiveOptions();

        return PvfArchive.Open(path, archiveOptions);
    }

    private string? ResolvePath() => _options.ResolveArchivePath();

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_gate)
        {
            _disposed = true;
            _archive?.Dispose();
            _archive = null;
        }
    }
}
