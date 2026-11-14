using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Dfo.Pvf;
using Dfo.Pvf.Codec;
using Dfo.Pvf.Container;
using Dfo.Pvf.Tokens;

namespace PvfPackageContentRepair;

/// <summary>
/// A token with its text materialised, so it can move between archives whose string pools differ.
/// Strings remember which pool they came from (narrow bytes or wide text) and go back to the same kind.
/// </summary>
public sealed record Sym(PvfTokenKind Kind, int Value, bool Wide = false, byte[]? Narrow = null, string? WideText = null)
{
    public bool IsString => Kind is PvfTokenKind.Identifier or PvfTokenKind.QuotedString or PvfTokenKind.BlockMarker5 or PvfTokenKind.BlockMarker7;

    public static Sym Int(int v) => new(PvfTokenKind.Int32, v);

    public string Text => !IsString ? (Kind == PvfTokenKind.Single ? BitConverter.Int32BitsToSingle(Value).ToString("0.0#####") : Value.ToString())
        : Wide ? WideText! : Decode(Narrow!);

    public static string Decode(byte[] b)
    {
        try { return new UTF8Encoding(false, true).GetString(b); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(936).GetString(b); }
    }

    public bool SameAs(Sym o) => Kind == o.Kind && (IsString
        ? Wide == o.Wide && (Wide ? WideText == o.WideText : Narrow!.AsSpan().SequenceEqual(o.Narrow))
        : Value == o.Value);

    public override string ToString() => IsString ? (Kind == PvfTokenKind.Identifier ? Text : "`" + Text + "`") : Text;
}

public sealed class Engine : IDisposable
{
    private const uint Utf8Xor = 0xAA74472E;
    private const uint Utf16Xor = 0x9A82F037;

    public PvfArchive Archive { get; }
    private readonly byte[] _source;
    private readonly List<byte> _narrow;
    private readonly int _narrowOriginal;
    private readonly StringBuilder _wide = new();
    private readonly int _wideOriginalChars;
    private readonly Dictionary<string, int> _narrowIndex = new();
    private readonly Dictionary<string, int> _wideIndex = new();
    private readonly byte[] _names;
    private readonly int _wideHeader, _wideEncodedLength;
    private readonly Dictionary<int, byte[]> _replacements = new();
    public List<string> Log { get; } = [];
    public int AppendedNarrow => _narrow.Count - _narrowOriginal;
    public int AppendedWideChars => _wide.Length - _wideOriginalChars;

    public Engine(string path)
    {
        Archive = PvfArchive.Open(path);
        _source = File.ReadAllBytes(path);
        var (header, layout) = Header(_source);
        _names = _source.AsSpan((int)layout.NameTableOffset, header.NameTableSize).ToArray();
        var narrowEncoded = checked((int)(BinaryPrimitives.ReadUInt32LittleEndian(_names.AsSpan(8)) ^ Utf8Xor));
        var narrowRaw = BinaryPrimitives.ReadInt32LittleEndian(_names.AsSpan(12)) ^ narrowEncoded;
        var narrowBytes = Inflate(_names.AsSpan(16, narrowEncoded).ToArray(), "sTrA");
        if (narrowBytes.Length != narrowRaw) throw new InvalidDataException("narrow pool length");
        _narrow = [.. narrowBytes];
        _narrowOriginal = _narrow.Count;
        _wideHeader = 16 + narrowEncoded;
        _wideEncodedLength = checked((int)(BinaryPrimitives.ReadUInt32LittleEndian(_names.AsSpan(_wideHeader)) ^ Utf16Xor));
        var wideRaw = BinaryPrimitives.ReadInt32LittleEndian(_names.AsSpan(_wideHeader + 4)) ^ _wideEncodedLength;
        var wideBytes = Inflate(_names.AsSpan(_wideHeader + 8, _wideEncodedLength).ToArray(), "sTrW");
        if (wideBytes.Length != wideRaw || (wideRaw & 1) != 0) throw new InvalidDataException("wide pool length");
        _wide.Append(Encoding.Unicode.GetString(wideBytes));
        _wideOriginalChars = _wide.Length;
        // Reverse indexes: first occurrence wins, which is what the packer would have reused.
        var p = 0;
        while (p < narrowBytes.Length)
        {
            var end = Array.IndexOf(narrowBytes, (byte)0, p);
            if (end < 0) end = narrowBytes.Length;
            _narrowIndex.TryAdd(Convert.ToHexString(narrowBytes, p, end - p), p << 1);
            p = end + 1;
        }
        var w = _wide.ToString();
        var q = 0;
        while (q < w.Length)
        {
            var end = w.IndexOf('\0', q);
            if (end < 0) end = w.Length;
            _wideIndex.TryAdd(w[q..end], (q << 1) | 1);
            q = end + 1;
        }
    }

    public void Dispose() => Archive.Dispose();

    private static (PvfHeader, SectionLayout) Header(byte[] source)
    {
        var h = source.AsSpan(0, PvfHeader.Size).ToArray();
        PvfCipher.DecryptSection("HeaD", h);
        var header = PvfHeader.Read(h, source.Length);
        return (header, new SectionLayout(header));
    }

    private static byte[] Inflate(byte[] encrypted, string key)
    {
        PvfCipher.DecryptStringTable(key, encrypted);
        using var o = new MemoryStream();
        using (var z = new ZLibStream(new MemoryStream(encrypted), CompressionMode.Decompress)) z.CopyTo(o);
        return o.ToArray();
    }

    private static byte[] Compress(byte[] bytes)
    {
        using var s = new MemoryStream();
        using (var z = new ZLibStream(s, CompressionLevel.Optimal, leaveOpen: true)) z.Write(bytes);
        return s.ToArray();
    }

    // ---------------- reading ----------------

    public static List<Sym> Read(PvfArchive archive, string path)
    {
        var index = archive.FindFile(path);
        if (index < 0) throw new InvalidDataException($"missing {path}");
        if (!archive.GetEntry(index).IsScript) throw new InvalidDataException($"not a script: {path}");
        var list = new List<Sym>();
        var r = archive.OpenScript(index);
        while (r.Read(out var t))
        {
            if (!t.IsString) { list.Add(new Sym(t.Kind, t.Value)); continue; }
            if ((t.Value & 1) != 0) list.Add(new Sym(t.Kind, 0, true, null, archive.ResolveString(t.Value)));
            else list.Add(new Sym(t.Kind, 0, false, archive.Names.Utf8Bytes(t.Value).ToArray()));
        }
        return list;
    }

    public List<Sym> Read(string path) => Read(Archive, path);

    /// <summary>A string token built from text, reusing the pool the base archive already uses for that text.</summary>
    public Sym Str(PvfTokenKind kind, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (_narrowIndex.ContainsKey(Convert.ToHexString(bytes))) return new Sym(kind, 0, false, bytes);
        var gbk = Encoding.GetEncoding(936).GetBytes(text);
        if (_narrowIndex.ContainsKey(Convert.ToHexString(gbk))) return new Sym(kind, 0, false, gbk);
        if (_wideIndex.ContainsKey(text)) return new Sym(kind, 0, true, null, text);
        throw new InvalidDataException($"text not in any pool, choose a pool explicitly: {text}");
    }

    public Sym Tag(string text) => Str(PvfTokenKind.Identifier, text);

    // ---------------- writing ----------------

    private int Offset(Sym s)
    {
        if (s.Wide)
        {
            if (_wideIndex.TryGetValue(s.WideText!, out var o)) return o;
            var off = (_wide.Length << 1) | 1;
            _wide.Append(s.WideText).Append('\0');
            _wideIndex[s.WideText!] = off;
            return off;
        }
        var key = Convert.ToHexString(s.Narrow!);
        if (_narrowIndex.TryGetValue(key, out var n)) return n;
        var noff = _narrow.Count << 1;
        _narrow.AddRange(s.Narrow!);
        _narrow.Add(0);
        _narrowIndex[key] = noff;
        return noff;
    }

    public byte[] Encode(IReadOnlyList<Sym> tokens)
    {
        var bytes = new byte[tokens.Count * PvfToken.Size];
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            bytes[i * 5] = (byte)t.Kind;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 5 + 1), t.IsString ? Offset(t) : t.Value);
        }
        return bytes;
    }

    public void Replace(string path, IReadOnlyList<Sym> tokens, string why)
    {
        var index = Archive.FindFile(path);
        if (index < 0) throw new InvalidDataException($"missing {path}");
        if (_replacements.ContainsKey(index)) throw new InvalidDataException($"replaced twice: {path}");
        var before = Read(path);
        _replacements[index] = Encode(tokens);
        _intended[index] = tokens.ToList();
        Log.Add($"{path}\t{before.Count}->{tokens.Count} tokens\t{why}");
    }

    private readonly Dictionary<int, List<Sym>> _intended = new();
    private readonly Dictionary<int, byte[]> _intendedRaw = new();

    /// <summary>Replaces a UTF-16 text file (type 3) by editing its decoded text.</summary>
    public void ReplaceText(string path, Func<string, string> edit, string why)
    {
        var index = Archive.FindFile(path);
        if (index < 0 || !Archive.GetEntry(index).IsUnicodeText) throw new InvalidDataException($"not a text file: {path}");
        if (_replacements.ContainsKey(index)) throw new InvalidDataException($"replaced twice: {path}");
        var raw = Archive.GetFileBytes(index).ToArray();
        var text = Encoding.Unicode.GetString(raw);
        if (!Encoding.Unicode.GetBytes(text).AsSpan().SequenceEqual(raw)) throw new InvalidDataException($"text does not round-trip: {path}");
        var edited = edit(text);
        if (edited == text) throw new InvalidDataException($"edit changed nothing: {path}");
        var bytes = Encoding.Unicode.GetBytes(edited);
        _replacements[index] = bytes;
        _intendedRaw[index] = bytes;
        Log.Add($"{path}\t{raw.Length}->{bytes.Length} bytes\t{why}");
    }

    public void Write(string output)
    {
        if (File.Exists(output)) throw new IOException("refuse to overwrite " + output);
        var (header, layout) = Header(_source);
        var headerBytes = _source.AsSpan(0, PvfHeader.Size).ToArray();
        PvfCipher.DecryptSection("HeaD", headerBytes);

        // Name table: rebuild only the pools that grew; everything after the wide pool is copied verbatim.
        byte[] names;
        if (AppendedNarrow == 0 && AppendedWideChars == 0) names = _names;
        else
        {
            using var ns = new MemoryStream();
            ns.Write(_names.AsSpan(0, 8));
            if (AppendedNarrow == 0) ns.Write(_names.AsSpan(8, _wideHeader - 8));
            else WritePool(ns, [.. _narrow], "sTrA", Utf8Xor);
            var wideEnd = _wideHeader + 8 + _wideEncodedLength;
            if (AppendedWideChars == 0) ns.Write(_names.AsSpan(_wideHeader, 8 + _wideEncodedLength));
            else WritePool(ns, Encoding.Unicode.GetBytes(_wide.ToString()), "sTrW", Utf16Xor);
            ns.Write(_names.AsSpan(wideEnd));
            names = ns.ToArray();
        }

        var table = _source.AsSpan((int)layout.FileTableOffset, layout.FileTableLength).ToArray();
        var chunkTable = _source.AsSpan((int)layout.ChunkTableOffset, layout.ChunkTableLength).ToArray();
        PvfCipher.DecryptSection("GRPI", chunkTable);
        if (BinaryPrimitives.ReadInt32LittleEndian(chunkTable.AsSpan(chunkTable.Length - 8)) != header.BodySize)
            throw new InvalidDataException("body has unindexed bytes");

        // One appended chunk per replaced file, as every client-verified patch so far did.
        using var body = new MemoryStream();
        var newEntries = new List<byte>();
        var chunk = header.ChunkCount;
        foreach (var (index, raw) in _replacements.OrderBy(x => x.Key))
        {
            var row = table.AsSpan(index * PvfHeader.FileEntrySize, PvfHeader.FileEntrySize);
            BinaryPrimitives.WriteInt32LittleEndian(row[8..], chunk++);
            BinaryPrimitives.WriteInt32LittleEndian(row[12..], 0);
            BinaryPrimitives.WriteInt32LittleEndian(row[16..], raw.Length);
            var c = Compress(raw);
            PvfCipher.DecryptSection("BodY", c);
            body.Write(c);
            var e = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(e, checked(header.BodySize + (int)body.Length));
            BinaryPrimitives.WriteInt32LittleEndian(e.AsSpan(4), raw.Length);
            newEntries.AddRange(e);
        }
        var newChunkTable = new byte[chunkTable.Length + newEntries.Count];
        chunkTable.CopyTo(newChunkTable, 0);
        newEntries.CopyTo(newChunkTable, chunkTable.Length);
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes.AsSpan(32), checked(header.BodySize + (int)body.Length));
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes.AsSpan(36), chunk);
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes.AsSpan(44), names.Length);
        PvfCipher.DecryptSection("HeaD", headerBytes);
        PvfCipher.DecryptSection("GRPI", newChunkTable);

        using (var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
        {
            target.Write(headerBytes);
            target.Write(table);
            target.Write(_source.AsSpan((int)layout.HashTableOffset, header.HashTableSize));
            target.Write(names);
            target.Write(newChunkTable);
            target.Write(_source.AsSpan((int)layout.BodyOffset, header.BodySize));
            body.WriteTo(target);
            target.Write(_source.AsSpan((int)layout.BodyOffset + header.BodySize));
        }
        Verify(output);
    }

    private static void WritePool(Stream s, byte[] raw, string key, uint xor)
    {
        var c = Compress(raw);
        PvfCipher.DecryptStringTable(key, c);
        var h = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(h, (uint)c.Length ^ xor);
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(4), raw.Length ^ c.Length);
        s.Write(h);
        s.Write(c);
    }

    private void Verify(string output)
    {
        using var patched = PvfArchive.Open(output);
        if (patched.FileCount != Archive.FileCount) throw new InvalidDataException("file count changed");
        for (var i = 0; i < Archive.FileCount; i++)
        {
            var a = Archive.GetEntry(i);
            var b = patched.GetEntry(i);
            if (a.NameOffset != b.NameOffset || a.PathOffset != b.PathOffset || a.DataType != b.DataType)
                throw new InvalidDataException($"entry identity changed: {i}");
            if (_intended.TryGetValue(i, out var want))
            {
                var path = Archive.ResolveString(a.PathOffset) + "/" + Archive.ResolveString(a.NameOffset);
                var got = Read(patched, path);
                if (got.Count != want.Count || got.Zip(want).Any(x => !x.First.SameAs(x.Second)))
                    throw new InvalidDataException($"readback mismatch: {path}");
            }
            else if (_intendedRaw.TryGetValue(i, out var raw))
            {
                if (!patched.GetFileBytes(i).SequenceEqual(raw)) throw new InvalidDataException($"text readback mismatch: {i}");
            }
            else if (a != b) throw new InvalidDataException($"unrelated entry changed: {i}");
        }
        // Every pre-existing string offset must still resolve to the same text.
        var sample = 0;
        for (var i = 0; i < Archive.FileCount; i += 97)
        {
            var e = Archive.GetEntry(i);
            if (Archive.ResolveString(e.NameOffset) != patched.ResolveString(e.NameOffset)) throw new InvalidDataException("pool offset drift");
            sample++;
        }
        Log.Add($"verify: {Archive.FileCount} entries, {_intended.Count + _intendedRaw.Count} replaced, {sample} name samples stable");
    }
}
