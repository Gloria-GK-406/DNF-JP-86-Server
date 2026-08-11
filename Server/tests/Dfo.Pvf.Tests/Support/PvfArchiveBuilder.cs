using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Dfo.Pvf.Codec;
using Dfo.Pvf.Container;
using Dfo.Pvf.Tokens;

namespace Dfo.Pvf.Tests.Support;

/// <summary>
/// Builds a valid PVF archive in memory so format tests need no external file.
/// </summary>
/// <remarks>
/// <para>
/// Content is fabricated rather than copied from a client archive: the tests assert
/// against values they chose, and nothing here redistributes game data.
/// </para>
/// <para>
/// Encryption reuses <see cref="PvfCipher"/> because the cipher xors against a
/// key-derived stream and is therefore its own inverse. That property is asserted
/// independently in <c>CipherTests</c>, so the builder does not silently depend on it.
/// </para>
/// </remarks>
internal sealed class PvfArchiveBuilder
{
    private const uint Utf8Xor = 0xAA74472E;
    private const uint Utf16Xor = 0x9A82F037;

    // Byte 0 is a lone terminator, so encoded offset 0 always reads as the empty string.
    private readonly List<byte> _utf8Pool = [0];
    private readonly List<byte> _utf16Pool = [0, 0];
    private readonly Dictionary<string, int> _utf8Offsets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _utf16Offsets = new(StringComparer.Ordinal);
    private readonly List<FileDraft> _files = [];
    private readonly SortedDictionary<int, List<int>> _chunkMembers = [];

    /// <summary>Interns a UTF-8 string and returns its encoded name-table offset.</summary>
    public int InternUtf8(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        if (_utf8Offsets.TryGetValue(value, out var existing))
            return existing;

        var offset = _utf8Pool.Count << 1;
        _utf8Pool.AddRange(Encoding.UTF8.GetBytes(value));
        _utf8Pool.Add(0);
        _utf8Offsets[value] = offset;
        return offset;
    }

    /// <summary>Interns a UTF-16 string and returns its encoded name-table offset.</summary>
    public int InternUtf16(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 1;

        if (_utf16Offsets.TryGetValue(value, out var existing))
            return existing;

        var offset = ((_utf16Pool.Count / 2) << 1) | 1;
        _utf16Pool.AddRange(Encoding.Unicode.GetBytes(value));
        _utf16Pool.AddRange([0, 0]);
        _utf16Offsets[value] = offset;
        return offset;
    }

    /// <summary>Starts a script file whose records are written through the returned writer.</summary>
    public ScriptWriter AddScript(string path, string name, int chunk = 0) =>
        new(this, path, name, chunk);

    /// <summary>Adds a UTF-16 text file.</summary>
    public PvfArchiveBuilder AddText(string path, string name, string text, int chunk = 0) =>
        AddRaw(path, name, Encoding.Unicode.GetBytes(text), UnicodeTextDataType, chunk);

    private const int UnicodeTextDataType = 3;

    /// <summary>Adds a file with caller-supplied stored bytes and data type.</summary>
    public PvfArchiveBuilder AddRaw(string path, string name, byte[] data, int dataType, int chunk = 0)
    {
        var index = _files.Count;
        _files.Add(new FileDraft(InternUtf8(path), InternUtf8(name), chunk, data, dataType));

        if (!_chunkMembers.TryGetValue(chunk, out var members))
            _chunkMembers[chunk] = members = [];
        members.Add(index);
        return this;
    }

    /// <summary>Adds a file whose name lives in the UTF-16 pool.</summary>
    public PvfArchiveBuilder AddRawWithUnicodeName(string path, string name, byte[] data, int dataType, int chunk = 0)
    {
        var index = _files.Count;
        _files.Add(new FileDraft(InternUtf8(path), InternUtf16(name), chunk, data, dataType));

        if (!_chunkMembers.TryGetValue(chunk, out var members))
            _chunkMembers[chunk] = members = [];
        members.Add(index);
        return this;
    }

    public byte[] Build()
    {
        // Chunk indices must be dense: the reader addresses them positionally.
        var chunkCount = _chunkMembers.Count == 0 ? 0 : _chunkMembers.Keys.Max() + 1;

        var entries = new FileEntryLayout[_files.Count];
        var body = new List<byte>();
        var chunkTable = new byte[chunkCount * PvfHeader.ChunkEntrySize];
        var cumulativeCompressed = 0;

        for (var chunk = 0; chunk < chunkCount; chunk++)
        {
            var raw = new List<byte>();
            if (_chunkMembers.TryGetValue(chunk, out var members))
            {
                foreach (var fileIndex in members)
                {
                    var draft = _files[fileIndex];
                    entries[fileIndex] = new FileEntryLayout(
                        draft.NameOffset, draft.PathOffset, chunk, raw.Count, draft.Data.Length, draft.DataType);
                    raw.AddRange(draft.Data);
                }
            }

            var encoded = Encrypt(Compress(raw.ToArray()), "BodY");
            body.AddRange(encoded);
            cumulativeCompressed += encoded.Length;

            var row = chunkTable.AsSpan(chunk * PvfHeader.ChunkEntrySize);
            BinaryPrimitives.WriteInt32LittleEndian(row, cumulativeCompressed);
            BinaryPrimitives.WriteInt32LittleEndian(row[4..], raw.Count);
        }

        PvfCipher.DecryptSection("GRPI", chunkTable);

        var fileTable = new byte[_files.Count * PvfHeader.FileEntrySize];
        for (var i = 0; i < entries.Length; i++)
        {
            var row = fileTable.AsSpan(i * PvfHeader.FileEntrySize);
            BinaryPrimitives.WriteInt32LittleEndian(row, entries[i].NameOffset);
            BinaryPrimitives.WriteInt32LittleEndian(row[4..], entries[i].PathOffset);
            BinaryPrimitives.WriteInt32LittleEndian(row[8..], entries[i].ChunkIndex);
            BinaryPrimitives.WriteInt32LittleEndian(row[12..], entries[i].DataOffset);
            BinaryPrimitives.WriteInt32LittleEndian(row[16..], entries[i].DataSize);
            BinaryPrimitives.WriteInt32LittleEndian(row[20..], entries[i].DataType);
        }

        // The reader skips the hash table, but a plausible empty one keeps the layout honest.
        var hashTable = new byte[8];
        PvfCipher.DecryptSection("HASH", hashTable);

        var nameTable = BuildNameTable();

        var header = new byte[PvfHeader.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(header, PvfHeader.Signature);
        var fields = header.AsSpan(24);
        BinaryPrimitives.WriteInt32LittleEndian(fields, _files.Count);
        BinaryPrimitives.WriteInt32LittleEndian(fields[8..], body.Count);
        BinaryPrimitives.WriteInt32LittleEndian(fields[12..], chunkCount);
        BinaryPrimitives.WriteInt32LittleEndian(fields[16..], hashTable.Length);
        BinaryPrimitives.WriteInt32LittleEndian(fields[20..], nameTable.Length);
        PvfCipher.DecryptSection("HeaD", header);

        return [.. header, .. fileTable, .. hashTable, .. nameTable, .. chunkTable, .. body];
    }

    private byte[] BuildNameTable()
    {
        var table = new List<byte>(new byte[8]);
        AppendPool(table, [.. _utf8Pool], "sTrA", Utf8Xor);
        AppendPool(table, [.. _utf16Pool], "sTrW", Utf16Xor);
        return [.. table];
    }

    private static void AppendPool(List<byte> destination, byte[] pool, string key, uint xor)
    {
        var encoded = Encrypt(Compress(pool), key, stringTable: true);

        Span<byte> lengths = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(lengths, (uint)encoded.Length ^ xor);
        BinaryPrimitives.WriteInt32LittleEndian(lengths[4..], pool.Length ^ encoded.Length);

        destination.AddRange(lengths);
        destination.AddRange(encoded);
    }

    private static byte[] Compress(byte[] payload)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(payload);
        return output.ToArray();
    }

    private static byte[] Encrypt(byte[] payload, string key, bool stringTable = false)
    {
        var copy = payload.ToArray();
        if (stringTable)
            PvfCipher.DecryptStringTable(key, copy);
        else
            PvfCipher.DecryptSection(key, copy);
        return copy;
    }

    private readonly record struct FileDraft(
        int PathOffset, int NameOffset, int Chunk, byte[] Data, int DataType);

    private readonly record struct FileEntryLayout(
        int NameOffset, int PathOffset, int ChunkIndex, int DataOffset, int DataSize, int DataType);

    /// <summary>Accumulates script records, interning any strings they reference.</summary>
    internal sealed class ScriptWriter
    {
        private readonly PvfArchiveBuilder _builder;
        private readonly string _path;
        private readonly string _name;
        private readonly int _chunk;
        private readonly List<byte> _records = [];

        internal ScriptWriter(PvfArchiveBuilder builder, string path, string name, int chunk)
        {
            _builder = builder;
            _path = path;
            _name = name;
            _chunk = chunk;
        }

        public ScriptWriter Int(int value) => Write(PvfTokenKind.Int32, value);

        public ScriptWriter Single(float value) =>
            Write(PvfTokenKind.Single, BitConverter.SingleToInt32Bits(value));

        /// <summary>Writes an identifier. Section tags are identifiers whose text includes brackets.</summary>
        public ScriptWriter Tag(string text) =>
            Write(PvfTokenKind.Identifier, _builder.InternUtf8(text));

        public ScriptWriter Quoted(string text) =>
            Write(PvfTokenKind.QuotedString, _builder.InternUtf8(text));

        /// <summary>Completes the file and returns the owning builder.</summary>
        public PvfArchiveBuilder End() =>
            _builder.AddRaw(_path, _name, [.. _records], 1, _chunk);

        private ScriptWriter Write(PvfTokenKind kind, int value)
        {
            _records.Add((byte)kind);
            Span<byte> payload = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(payload, value);
            _records.AddRange(payload);
            return this;
        }
    }
}

/// <summary>Writes a built archive to a temporary file and opens it.</summary>
internal sealed class SyntheticArchive : IDisposable
{
    private readonly string _path;

    public PvfArchive Archive { get; }

    public SyntheticArchive(PvfArchiveBuilder builder, PvfArchiveOptions? options = null)
    {
        _path = Path.Combine(Path.GetTempPath(), $"dfo-pvf-{Guid.NewGuid():N}.pvf");
        File.WriteAllBytes(_path, builder.Build());
        Archive = PvfArchive.Open(_path, options);
    }

    public void Dispose()
    {
        Archive.Dispose();
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test over.
        }
    }
}
