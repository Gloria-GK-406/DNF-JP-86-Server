using System.IO.MemoryMappedFiles;

namespace Dfo.Pvf.Container;

/// <summary>
/// Memory-maps the archive so section and chunk reads cost no managed heap.
/// Residency of hot pages is left to the operating system page cache.
/// </summary>
public sealed unsafe class PvfFileMap : IDisposable
{
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private byte* _origin;
    private bool _disposed;

    public long Length { get; }

    public PvfFileMap(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        var info = new FileInfo(archivePath);
        if (!info.Exists)
            throw new FileNotFoundException("PVF archive not found.", archivePath);
        if (info.Length < PvfHeader.Size)
            throw new PvfFormatException($"PVF archive is only {info.Length} bytes.");

        Length = info.Length;
        _mapping = MemoryMappedFile.CreateFromFile(
            archivePath,
            FileMode.Open,
            mapName: null,
            capacity: 0,
            MemoryMappedFileAccess.Read);

        try
        {
            _view = _mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            byte* pointer = null;
            _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            _origin = pointer + _view.PointerOffset;
        }
        catch
        {
            _mapping.Dispose();
            throw;
        }
    }

    /// <summary>Returns a read-only view of the mapped bytes. The span is valid until disposal.</summary>
    public ReadOnlySpan<byte> Slice(long offset, int length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (offset < 0 || length < 0 || offset + length > Length)
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                $"Range [{offset}, {offset + length}) falls outside the {Length}-byte archive.");

        return new ReadOnlySpan<byte>(_origin + offset, length);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_origin is not null)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _origin = null;
        }

        _view.Dispose();
        _mapping.Dispose();
    }
}
