using System.Buffers;
using System.Text;
using Dfo.Pvf.Chunks;
using Dfo.Pvf.Codec;
using Dfo.Pvf.Container;
using Dfo.Pvf.Diagnostics;
using Dfo.Pvf.Index;
using Dfo.Pvf.Strings;
using Dfo.Pvf.Tokens;

namespace Dfo.Pvf;

/// <summary>
/// Read-only access to a PVF archive.
/// </summary>
/// <remarks>
/// <para>
/// The archive knows about containers, chunks, string pools and script records. It has no
/// knowledge of game concepts: mapping a script into an item, a quest or a dungeon belongs
/// to whoever consumes it.
/// </para>
/// <para>
/// Instances are thread-safe for concurrent reads. Content is immutable, so the only shared
/// mutable state is the chunk cache, which synchronises internally.
/// </para>
/// </remarks>
public sealed class PvfArchive : IDisposable
{
    private readonly PvfFileMap _map;
    private readonly FileIndex _files;
    private readonly ChunkStore _chunks;
    private bool _disposed;

    public int FileCount => _files.Count;

    public int ChunkCount => _chunks.ChunkCount;

    /// <summary>The archive's string pools.</summary>
    public NameTable Names { get; }

    /// <summary>Chunk-cache counters, for sizing <see cref="PvfArchiveOptions.ChunkCacheBytes"/>.</summary>
    public PvfMetrics Metrics { get; }

    private PvfArchive(PvfFileMap map, FileIndex files, ChunkStore chunks, NameTable names, PvfMetrics metrics)
    {
        _map = map;
        _files = files;
        _chunks = chunks;
        Names = names;
        Metrics = metrics;
    }

    /// <summary>Maps and indexes an archive. The file is not read into the managed heap.</summary>
    public static PvfArchive Open(string archivePath, PvfArchiveOptions? options = null)
    {
        options ??= PvfArchiveOptions.Default;
        var map = new PvfFileMap(archivePath);

        try
        {
            var headerBytes = ArrayPool<byte>.Shared.Rent(PvfHeader.Size);
            PvfHeader header;
            try
            {
                map.Slice(0, PvfHeader.Size).CopyTo(headerBytes);
                PvfCipher.DecryptSection("HeaD", headerBytes.AsSpan(0, PvfHeader.Size));
                header = PvfHeader.Read(headerBytes.AsSpan(0, PvfHeader.Size), map.Length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(headerBytes);
            }

            var layout = new SectionLayout(header);
            var metrics = new PvfMetrics();
            var names = NameTable.Read(map, layout, header.NameTableSize);
            var chunks = new ChunkStore(map, header, layout, options.ChunkCacheBytes, metrics);
            var files = new FileIndex(map, header, layout, names);

            return new PvfArchive(map, files, chunks, names, metrics);
        }
        catch
        {
            map.Dispose();
            throw;
        }
    }

    /// <summary>Resolves an archive path to a file index, or -1 when absent. Case-insensitive.</summary>
    public int FindFile(string archivePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _files.Find(archivePath);
    }

    /// <summary>Reads one file-table row.</summary>
    public PvfFileEntry GetEntry(int fileIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _files.GetEntry(fileIndex);
    }

    /// <summary>
    /// Returns the file's stored bytes. The span points into a cached chunk; it stays
    /// readable and unchanged for as long as the caller holds it, because archive content
    /// is immutable and cached chunks are never recycled into a pool.
    /// </summary>
    public ReadOnlySpan<byte> GetFileBytes(int fileIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var entry = _files.GetEntry(fileIndex);
        if (entry.DataSize <= 0 || entry.ChunkIndex < 0)
            return default;

        var chunk = _chunks.Get(entry.ChunkIndex);
        if (entry.DataOffset < 0 || entry.DataOffset + entry.DataSize > chunk.Length)
            throw new PvfFormatException(
                $"File {fileIndex} claims bytes [{entry.DataOffset}, {entry.DataOffset + entry.DataSize}) " +
                $"of a {chunk.Length}-byte chunk.");

        return chunk.AsSpan(entry.DataOffset, entry.DataSize);
    }

    /// <summary>Opens a token reader over a script file. Returns an empty reader for other types.</summary>
    public PvfTokenReader OpenScript(int fileIndex)
    {
        var entry = GetEntry(fileIndex);
        return entry.IsScript ? new PvfTokenReader(GetFileBytes(fileIndex)) : new PvfTokenReader(default);
    }

    /// <summary>Opens a token reader by path. Returns an empty reader when the path is absent.</summary>
    public PvfTokenReader OpenScript(string archivePath)
    {
        var index = FindFile(archivePath);
        return index < 0 ? new PvfTokenReader(default) : OpenScript(index);
    }

    /// <summary>Reads a UTF-16 text file. Returns empty for other types.</summary>
    public string ReadUnicodeText(int fileIndex)
    {
        var entry = GetEntry(fileIndex);
        if (!entry.IsUnicodeText)
            return string.Empty;

        var bytes = GetFileBytes(fileIndex);
        return bytes.IsEmpty ? string.Empty : Encoding.Unicode.GetString(bytes);
    }

    /// <summary>
    /// Resolves a tag such as <c>[FRAME]</c> to its interned offset, so scans can match it
    /// by integer comparison. Returns <see cref="TagRef.Unresolved"/> when the archive
    /// never uses that tag.
    /// </summary>
    public TagRef GetTag(string tagText)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(tagText);

        Span<byte> buffer = stackalloc byte[Math.Min(256, Encoding.UTF8.GetMaxByteCount(tagText.Length))];
        if (Encoding.UTF8.GetMaxByteCount(tagText.Length) > buffer.Length)
        {
            var heap = Encoding.UTF8.GetBytes(tagText);
            return Names.TryFindUtf8(heap, out var heapOffset) ? new TagRef(heapOffset) : TagRef.Unresolved;
        }

        var written = Encoding.UTF8.GetBytes(tagText, buffer);
        return Names.TryFindUtf8(buffer[..written], out var offset) ? new TagRef(offset) : TagRef.Unresolved;
    }

    /// <summary>Materialises the string behind an encoded name-table offset.</summary>
    public string ResolveString(int encodedOffset) => Names.Resolve(encodedOffset);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _map.Dispose();
    }
}
