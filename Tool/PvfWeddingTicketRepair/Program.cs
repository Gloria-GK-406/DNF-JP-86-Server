using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dfo.Pvf;
using Dfo.Pvf.Codec;
using Dfo.Pvf.Container;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;

// Strict content repair, not a tolerant reader. Redirect one file to an appended chunk;
// preserve every unrelated entry, name/hash pool and existing compressed body byte.
try
{
    if (args.Length != 2) throw new ArgumentException("Usage: PvfWeddingTicketRepair <source.pvf> <new.pvf>");
    Console.OutputEncoding = Encoding.UTF8;
    var input = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]);
    if (input.Equals(output, StringComparison.OrdinalIgnoreCase) || File.Exists(output) || File.Exists(output + ".json"))
        throw new IOException("Refuse in-place repair or overwriting an existing output.");
    const string path = "stackable/event/chn_wedding_system_2014/chn_stk_couple_surprise_ticket.stk";
    using var archive = PvfArchive.Open(input);
    var index = archive.FindFile(path);
    if (index < 0 || !archive.GetEntry(index).IsScript) throw new InvalidDataException("Missing wedding ticket script.");
    var tokens = new List<PvfToken>(); var reader = archive.OpenScript(index);
    while (reader.Read(out var token)) tokens.Add(token);
    bool Tag(PvfToken token, string value) => token.Kind == PvfTokenKind.Identifier && archive.ResolveString(token.Value) == value;
    var fields = Enumerable.Range(0, tokens.Count).Where(i => Tag(tokens[i], "[stackable type]")).ToArray();
    if (fields.Length != 2) throw new InvalidDataException("Expected exactly two erroneous stackable type declarations.");
    foreach (var i in fields)
        if (i + 3 >= tokens.Count || !tokens[i + 1].IsString || archive.ResolveString(tokens[i + 1].Value) != "[waste]"
            || tokens[i + 2] != new PvfToken(PvfTokenKind.Int32, 0) || tokens[i + 3].Kind != PvfTokenKind.Identifier)
            throw new InvalidDataException("Unexpected ticket type or operands; refuse repair.");
    if (!tokens.Skip(fields[0]).Take(3).SequenceEqual(tokens.Skip(fields[1]).Take(3)))
        throw new InvalidDataException("Conflicting duplicate declaration; refuse repair.");
    tokens.RemoveRange(fields[1], 3);
    var replacement = new byte[tokens.Count * PvfToken.Size];
    for (var i = 0; i < tokens.Count; i++)
    { replacement[i * 5] = (byte)tokens[i].Kind; BinaryPrimitives.WriteInt32LittleEndian(replacement.AsSpan(i * 5 + 1), tokens[i].Value); }

    var source = File.ReadAllBytes(input);
    var headerBytes = source.AsSpan(0, PvfHeader.Size).ToArray(); PvfCipher.DecryptSection("HeaD", headerBytes);
    var header = PvfHeader.Read(headerBytes, source.Length); var layout = new SectionLayout(header);
    var table = source.AsSpan((int)layout.FileTableOffset, layout.FileTableLength).ToArray();
    var chunks = source.AsSpan((int)layout.ChunkTableOffset, layout.ChunkTableLength).ToArray(); PvfCipher.DecryptSection("GRPI", chunks);
    if (BinaryPrimitives.ReadInt32LittleEndian(chunks.AsSpan(chunks.Length - 8)) != header.BodySize)
        throw new InvalidDataException("Refuse archive with unindexed body bytes.");
    var row = table.AsSpan(index * PvfHeader.FileEntrySize, PvfHeader.FileEntrySize);
    BinaryPrimitives.WriteInt32LittleEndian(row[8..], header.ChunkCount);
    BinaryPrimitives.WriteInt32LittleEndian(row[12..], 0);
    BinaryPrimitives.WriteInt32LittleEndian(row[16..], replacement.Length);
    using var compressed = new MemoryStream();
    using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(replacement);
    var chunk = compressed.ToArray(); PvfCipher.DecryptSection("BodY", chunk);
    var nextChunks = new byte[chunks.Length + PvfHeader.ChunkEntrySize]; chunks.CopyTo(nextChunks, 0);
    BinaryPrimitives.WriteInt32LittleEndian(nextChunks.AsSpan(chunks.Length), checked(header.BodySize + chunk.Length));
    BinaryPrimitives.WriteInt32LittleEndian(nextChunks.AsSpan(chunks.Length + 4), replacement.Length);
    BinaryPrimitives.WriteInt32LittleEndian(headerBytes.AsSpan(32), checked(header.BodySize + chunk.Length));
    BinaryPrimitives.WriteInt32LittleEndian(headerBytes.AsSpan(36), checked(header.ChunkCount + 1));
    PvfCipher.DecryptSection("HeaD", headerBytes); PvfCipher.DecryptSection("GRPI", nextChunks);
    using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
    {
        file.Write(headerBytes); file.Write(table);
        file.Write(source.AsSpan((int)layout.HashTableOffset, (int)(layout.ChunkTableOffset - layout.HashTableOffset)));
        file.Write(nextChunks); file.Write(source.AsSpan((int)layout.BodyOffset, header.BodySize)); file.Write(chunk);
        file.Write(source.AsSpan((int)layout.BodyOffset + header.BodySize));
    }
    using var patched = PvfArchive.Open(output);
    if (patched.FileCount != archive.FileCount || patched.ChunkCount != archive.ChunkCount + 1)
        throw new InvalidDataException("Unexpected archive counts.");
    for (var i = 0; i < archive.FileCount; i++)
    {
        var before = archive.GetEntry(i); var after = patched.GetEntry(i);
        if (i != index) { if (before != after) throw new InvalidDataException($"Unrelated entry changed: {i}."); }
        else if (before.NameOffset != after.NameOffset || before.PathOffset != after.PathOffset || before.DataType != after.DataType
            || !patched.GetFileBytes(i).SequenceEqual(replacement)) throw new InvalidDataException("Repaired entry mismatch.");
    }
    var written = File.ReadAllBytes(output);
    if (!written.AsSpan((int)layout.BodyOffset + PvfHeader.ChunkEntrySize, header.BodySize).SequenceEqual(source.AsSpan((int)layout.BodyOffset, header.BodySize))
        || !written.AsSpan((int)layout.HashTableOffset, (int)(layout.ChunkTableOffset - layout.HashTableOffset)).SequenceEqual(source.AsSpan((int)layout.HashTableOffset, (int)(layout.ChunkTableOffset - layout.HashTableOffset)))
        || !written.AsSpan((int)layout.BodyOffset + PvfHeader.ChunkEntrySize + header.BodySize + chunk.Length).SequenceEqual(source.AsSpan((int)layout.BodyOffset + header.BodySize)))
        throw new InvalidDataException("Existing body, hash/name pools or trailer changed.");
    // The ordinary strict schema reader must now accept exactly one type declaration.
    var values = PvfDocument.Open(patched, path).Read(PvfField.Tokens("[stackable type]"));
    if (values.Count != 2 || patched.ResolveString(values[0].Value) != "[waste]" || values[1].Value != 0)
        throw new InvalidDataException("Strict schema verification failed.");
    var report = new
    {
        Source = input, Output = output, ModifiedFile = path, ItemId = 10002915, RemovedTokens = 3,
        SourceSha256 = Convert.ToHexString(SHA256.HashData(source)), OutputSha256 = Convert.ToHexString(SHA256.HashData(written)),
        FileCount = archive.FileCount, UnchangedEntries = archive.FileCount - 1, ExistingChunksPreserved = archive.ChunkCount,
        StrictTypeDeclarations = 1,
    };
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(output + ".json", json); Console.WriteLine(json); return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
