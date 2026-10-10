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
    if (args.Length != 2) throw new ArgumentException("Usage: PvfAvatarSocketTextPatch <source.pvf> <new.pvf>");
    var input = Path.GetFullPath(args[0]);
    var output = Path.GetFullPath(args[1]);
    if (input.Equals(output, StringComparison.OrdinalIgnoreCase) || File.Exists(output) || File.Exists(output + ".json"))
        throw new IOException("Refuse in-place editing or overwriting an existing output.");
    const string path = "stackable/event/emblem/emblem_drill.stk";
    const string description = "使用该道具后，可选择一件装扮并开启镶嵌栏。";
    var source = File.ReadAllBytes(input);
    using var archive = PvfArchive.Open(input);
    var index = archive.FindFile(path);
    if (index < 0 || !archive.GetEntry(index).IsScript) throw new InvalidDataException("Missing avatar drill script.");
    var reader = archive.OpenScript(index);
    var tokens = new List<PvfToken>();
    while (reader.Read(out var token)) tokens.Add(token);
    var explain = Enumerable.Range(0, tokens.Count).Single(i => tokens[i].Kind == PvfTokenKind.Identifier && archive.ResolveString(tokens[i].Value) == "[explain]") + 1;
    if (explain >= tokens.Count || tokens[explain].Kind != PvfTokenKind.QuotedString || archive.ResolveString(tokens[explain].Value) != "explain_7421")
        throw new InvalidDataException("Expected explain_7421 placeholder; already patched or unfamiliar input.");
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
    var appended = Encoding.Unicode.GetBytes(description + "\0");
    var newWide = new byte[checked(oldWide.Length + appended.Length)];
    oldWide.CopyTo(newWide, 0);
    appended.CopyTo(newWide, oldWide.Length);
    // Odd encoded offsets count UTF-16 characters. Appending preserves every existing offset.
    var descriptionOffset = checked(oldWide.Length | 1);
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
    BinaryPrimitives.WriteInt32LittleEndian(script.AsSpan(explain * PvfToken.Size + 1), descriptionOffset);
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
    if (patched.FileCount != archive.FileCount || patched.ChunkCount != archive.ChunkCount + 1 || patched.ResolveString(descriptionOffset) != description)
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
    // Only the explanation token's four-byte string reference may change in the script.
    var originalScript = archive.GetFileBytes(index);
    for (var i = 0; i < script.Length; i++)
        if ((i < explain * 5 + 1 || i >= explain * 5 + 5) && originalScript[i] != script[i])
            throw new InvalidDataException("Unrelated script byte changed.");
    var report = new
    {
        Source = input, Output = output,
        SourceSha256 = Convert.ToHexString(SHA256.HashData(source)), OutputSha256 = Convert.ToHexString(SHA256.HashData(written)),
        ItemId = 7421, Path = path, Field = "[explain]", Before = "explain_7421", After = description,
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
