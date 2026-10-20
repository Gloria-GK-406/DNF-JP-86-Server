using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dfo.Pvf;
using Dfo.Pvf.Codec;
using Dfo.Pvf.Container;
using Dfo.Pvf.Tokens;

try
{
    if (args.Length != 2) throw new ArgumentException("Usage: PvfCollectionBoxTextPatch <source.pvf> <new.pvf>");
    var input = Path.GetFullPath(args[0]);
    var output = Path.GetFullPath(args[1]);
    if (input.Equals(output, StringComparison.OrdinalIgnoreCase) || File.Exists(output) || File.Exists(output + ".json"))
        throw new IOException("Refuse in-place editing or overwriting an existing output.");
    const string path = "localization/collectbox/collectboxinfo.etc";
    var source = File.ReadAllBytes(input);
    using var archive = PvfArchive.Open(input);
    var index = archive.FindFile(path);
    if (index < 0 || !archive.GetEntry(index).IsScript) throw new InvalidDataException("Missing collection box script.");
    var reader = archive.OpenScript(index);
    var tokens = new List<PvfToken>();
    while (reader.Read(out var token)) tokens.Add(token);
    var expected = new Dictionary<int, (string Before, string After)>
    {
        [1] = ("SAO收集箱的属性有效期至2014年12月30日。 每个稀有宝珠额外增加6点力量、 智力、 体力和精神。 每个神器宝珠额外增加12点力量、 智力、 体力和精神。\n使用[收集箱延期券 (1天)]，可以使收集箱的期限延长至2015年2月1日。 2月1日以后， 将无法使用延期券。",
            "SAO收集箱常驻启用，属性永久有效。 每个稀有宝珠额外增加6点力量、 智力、 体力和精神。 每个神器宝珠额外增加12点力量、 智力、 体力和精神。"),
        [2] = ("生肖宝珠收集箱的属性有效期至2015年9月1日维护前。 每个生肖宝珠额外增加8点物理/魔法攻击力， 额外增加12点独立攻击力。 (收集箱的效果不适用于决斗场)",
            "生肖之灵收集箱常驻启用，属性永久有效。 每个生肖宝珠额外增加8点物理/魔法攻击力， 额外增加12点独立攻击力。 (收集箱的效果不适用于决斗场)"),
        [3] = ("三国志卡片收集箱的属性有效期至2017年1月5日维护前。 每个三国志卡片额外增加8点物理/魔法攻击力， 额外增加12点独立攻击力。 (收集箱的效果不适用于决斗场)",
            "三国志卡片收集箱常驻启用，属性永久有效。 每个三国志卡片额外增加8点物理/魔法攻击力， 额外增加12点独立攻击力。 (收集箱的效果不适用于决斗场)"),
    };
    var edits = new List<(int Box, int Token, string Before, string After)>();
    var box = 0;
    for (var i = 0; i < tokens.Count; i++)
    {
        if (tokens[i].Kind != PvfTokenKind.Identifier) continue;
        var tag = archive.ResolveString(tokens[i].Value);
        if (tag == "[Index]")
        {
            if (i + 1 >= tokens.Count || tokens[i + 1].Kind != PvfTokenKind.Int32) throw new InvalidDataException("Invalid series ID.");
            box = tokens[i + 1].Value;
        }
        else if (tag == "[Box Explain]")
        {
            if (!expected.TryGetValue(box, out var text) || edits.Any(edit => edit.Box == box)
                || i + 2 >= tokens.Count || tokens[i + 1].Kind != PvfTokenKind.QuotedString
                || tokens[i + 2].Kind != PvfTokenKind.Identifier || archive.ResolveString(tokens[i + 1].Value) != text.Before)
                throw new InvalidDataException("Unexpected or already patched box explanation; refuse mutation.");
            edits.Add((box, i + 1, text.Before, text.After));
        }
    }
    if (edits.Count != 3) throw new InvalidDataException("Expected exactly three explanations.");
    var script = archive.GetFileBytes(index).ToArray();
    var headerBytes = source.AsSpan(0, PvfHeader.Size).ToArray();
    PvfCipher.DecryptSection("HeaD", headerBytes);
    var header = PvfHeader.Read(headerBytes, source.Length);
    var layout = new SectionLayout(header);
    var names = source.AsSpan((int)layout.NameTableOffset, header.NameTableSize).ToArray();
    if (names.Length < 24) throw new InvalidDataException("Missing name pools.");
    var narrowLength = checked((int)(BinaryPrimitives.ReadUInt32LittleEndian(names.AsSpan(8)) ^ 0xAA74472Eu));
    var wideHeader = checked(16 + narrowLength);
    if (narrowLength <= 0 || wideHeader + 8 > names.Length) throw new InvalidDataException("Invalid narrow pool.");
    var wideLength = checked((int)(BinaryPrimitives.ReadUInt32LittleEndian(names.AsSpan(wideHeader)) ^ 0x9A82F037u));
    var rawLength = BinaryPrimitives.ReadInt32LittleEndian(names.AsSpan(wideHeader + 4)) ^ wideLength;
    if (wideLength <= 0 || rawLength <= 0 || wideHeader + 8 + wideLength > names.Length) throw new InvalidDataException("Invalid wide pool.");
    var encryptedWide = names.AsSpan(wideHeader + 8, wideLength).ToArray();
    PvfCipher.DecryptStringTable("sTrW", encryptedWide);
    using var inflated = new MemoryStream();
    using (var zlib = new ZLibStream(new MemoryStream(encryptedWide), CompressionMode.Decompress)) zlib.CopyTo(inflated);
    var oldWide = inflated.ToArray();
    if (oldWide.Length != rawLength || (rawLength & 1) != 0 || oldWide[^1] != 0 || oldWide[^2] != 0)
        throw new InvalidDataException("Wide pool length or terminator mismatch.");
    using var wideStream = new MemoryStream();
    wideStream.Write(oldWide);
    var offsets = new Dictionary<int, int>();
    foreach (var edit in edits)
    {
        // Append UTF-16 strings, preserving every pre-existing encoded offset.
        offsets.Add(edit.Token, checked((int)wideStream.Length | 1));
        wideStream.Write(Encoding.Unicode.GetBytes(edit.After + "\0"));
    }
    var newWide = wideStream.ToArray();
    var compressedWide = Compress(newWide);
    PvfCipher.DecryptStringTable("sTrW", compressedWide);
    using var namesStream = new MemoryStream();
    namesStream.Write(names.AsSpan(0, wideHeader));
    var poolHeader = new byte[8];
    BinaryPrimitives.WriteUInt32LittleEndian(poolHeader, (uint)compressedWide.Length ^ 0x9A82F037u);
    BinaryPrimitives.WriteInt32LittleEndian(poolHeader.AsSpan(4), newWide.Length ^ compressedWide.Length);
    namesStream.Write(poolHeader);
    namesStream.Write(compressedWide);
    namesStream.Write(names.AsSpan(wideHeader + 8 + wideLength));
    var newNames = namesStream.ToArray();
    foreach (var edit in edits)
        BinaryPrimitives.WriteInt32LittleEndian(script.AsSpan(edit.Token * PvfToken.Size + 1), offsets[edit.Token]);
    var fileTable = source.AsSpan((int)layout.FileTableOffset, layout.FileTableLength).ToArray();
    var row = fileTable.AsSpan(index * PvfHeader.FileEntrySize, PvfHeader.FileEntrySize);
    BinaryPrimitives.WriteInt32LittleEndian(row[8..], header.ChunkCount);
    BinaryPrimitives.WriteInt32LittleEndian(row[12..], 0);
    BinaryPrimitives.WriteInt32LittleEndian(row[16..], script.Length);
    var chunkTable = source.AsSpan((int)layout.ChunkTableOffset, layout.ChunkTableLength).ToArray();
    PvfCipher.DecryptSection("GRPI", chunkTable);
    if (chunkTable.Length < 8 || BinaryPrimitives.ReadInt32LittleEndian(chunkTable.AsSpan(chunkTable.Length - 8)) != header.BodySize)
        throw new InvalidDataException("Body has unindexed bytes.");
    var newChunk = Compress(script);
    PvfCipher.DecryptSection("BodY", newChunk);
    var newChunkTable = new byte[checked(chunkTable.Length + PvfHeader.ChunkEntrySize)];
    chunkTable.CopyTo(newChunkTable, 0);
    BinaryPrimitives.WriteInt32LittleEndian(newChunkTable.AsSpan(chunkTable.Length), checked(header.BodySize + newChunk.Length));
    BinaryPrimitives.WriteInt32LittleEndian(newChunkTable.AsSpan(chunkTable.Length + 4), script.Length);
    BinaryPrimitives.WriteInt32LittleEndian(headerBytes.AsSpan(32), checked(header.BodySize + newChunk.Length));
    BinaryPrimitives.WriteInt32LittleEndian(headerBytes.AsSpan(36), checked(header.ChunkCount + 1));
    BinaryPrimitives.WriteInt32LittleEndian(headerBytes.AsSpan(44), newNames.Length);
    PvfCipher.DecryptSection("HeaD", headerBytes);
    PvfCipher.DecryptSection("GRPI", newChunkTable);
    using (var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
    {
        target.Write(headerBytes);
        target.Write(fileTable);
        target.Write(source.AsSpan((int)layout.HashTableOffset, header.HashTableSize));
        target.Write(newNames);
        target.Write(newChunkTable);
        target.Write(source.AsSpan((int)layout.BodyOffset, header.BodySize));
        target.Write(newChunk);
        target.Write(source.AsSpan((int)layout.BodyOffset + header.BodySize));
    }
    using var patched = PvfArchive.Open(output);
    if (patched.FileCount != archive.FileCount || patched.ChunkCount != archive.ChunkCount + 1 || edits.Any(edit => patched.ResolveString(offsets[edit.Token]) != edit.After))
        throw new InvalidDataException("Archive readback mismatch.");
    for (var i = 0; i < archive.FileCount; i++)
    {
        var before = archive.GetEntry(i);
        var after = patched.GetEntry(i);
        if (i == index)
        {
            if (before.NameOffset != after.NameOffset || before.PathOffset != after.PathOffset || before.DataType != after.DataType || !patched.GetFileBytes(i).SequenceEqual(script))
                throw new InvalidDataException("Patched entry mismatch.");
        }
        else if (before != after) throw new InvalidDataException($"Unrelated file entry changed: {i}.");
    }
    var written = File.ReadAllBytes(output);
    var newBodyOffset = checked((int)layout.BodyOffset + newNames.Length - names.Length + PvfHeader.ChunkEntrySize);
    if (!written.AsSpan(newBodyOffset, header.BodySize).SequenceEqual(source.AsSpan((int)layout.BodyOffset, header.BodySize)))
        throw new InvalidDataException("Existing body chunks changed.");
    // Exactly three four-byte references may differ; every rule and other field stays intact.
    var originalScript = archive.GetFileBytes(index);
    for (var i = 0; i < script.Length; i++)
        if (!edits.Any(edit => i >= edit.Token * 5 + 1 && i < edit.Token * 5 + 5) && originalScript[i] != script[i])
            throw new InvalidDataException("Unrelated script byte changed.");
    var readback = new List<string>();
    var patchedReader = patched.OpenScript(index);
    while (patchedReader.Read(out var token))
        if (token.IsString) readback.Add(patched.ResolveString(token.Value));
    foreach (var edit in edits)
        if (readback.Count(text => text == edit.After) != 1 || readback.Contains(edit.Before))
            throw new InvalidDataException("Explanation readback mismatch.");
    var newHeaderBytes = written.AsSpan(0, PvfHeader.Size).ToArray();
    PvfCipher.DecryptSection("HeaD", newHeaderBytes);
    var newHeader = PvfHeader.Read(newHeaderBytes, written.Length);
    var newLayout = new SectionLayout(newHeader);
    if (!written.AsSpan((int)newLayout.HashTableOffset, header.HashTableSize).SequenceEqual(source.AsSpan((int)layout.HashTableOffset, header.HashTableSize))
        || !written.AsSpan(newBodyOffset + header.BodySize + newChunk.Length).SequenceEqual(source.AsSpan((int)layout.BodyOffset + header.BodySize)))
        throw new InvalidDataException("Hash table or trailer changed.");
    var report = new
    {
        Source = input, Output = output,
        SourceSha256 = Convert.ToHexString(SHA256.HashData(source)), OutputSha256 = Convert.ToHexString(SHA256.HashData(written)),
        Path = path, Field = "[Box Explain]", Changes = edits.Select(edit => new { BoxId = edit.Box, edit.Before, edit.After }).ToArray(),
        archive.FileCount, UnchangedFiles = archive.FileCount - 1, PreservedExistingChunks = archive.ChunkCount,
        PreservedExistingStringOffsets = true, PreservedExistingBodyBytes = true
    };
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(output + ".json", json);
    Console.WriteLine(json);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static byte[] Compress(byte[] bytes)
{
    using var stream = new MemoryStream();
    using (var zlib = new ZLibStream(stream, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(bytes);
    return stream.ToArray();
}
