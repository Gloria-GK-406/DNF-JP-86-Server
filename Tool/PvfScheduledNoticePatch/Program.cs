using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Dfo.Pvf;
using Dfo.Pvf.Codec;
using Dfo.Pvf.Container;
using Dfo.Pvf.Tokens;

try
{
    if (args.Length != 3) throw new ArgumentException("Usage: PvfScheduledNoticePatch <source.pvf> <new.pvf> <expected-source-sha256>");
    var input = Path.GetFullPath(args[0]);
    var output = Path.GetFullPath(args[1]);
    if (input.Equals(output, StringComparison.OrdinalIgnoreCase) || File.Exists(output) || File.Exists(output + ".json"))
        throw new IOException("Refuse in-place editing or overwriting an existing output.");
    var source = File.ReadAllBytes(input);
    var sourceHash = Convert.ToHexString(SHA256.HashData(source));
    if (!sourceHash.Equals(args[2], StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Source archive SHA-256 changed; audit it before patching.");
    using var archive = PvfArchive.Open(input);
    const string schedulePath = "etc/chn_schedule.etc";
    var targetIndex = archive.FindFile(schedulePath);
    if (targetIndex < 0 || !archive.GetEntry(targetIndex).IsScript) throw new InvalidDataException("Missing schedule script.");
    var reader = archive.OpenScript(targetIndex);
    var tokens = new List<PvfToken>();
    while (reader.Read(out var token)) tokens.Add(token);
    bool IsTag(PvfToken token, string name) => token.Kind == PvfTokenKind.Identifier && archive.ResolveString(token.Value) == name;
    var begin = Enumerable.Range(0, tokens.Count).Single(i => IsTag(tokens[i], "[dungeon]")) + 1;
    var end = Enumerable.Range(begin, tokens.Count - begin).First(i => IsTag(tokens[i], "[/dungeon]"));
    // A21 VA011C4FA7..011C514C: 17 integers per dungeon declaration.
    // Field six is the persistent notice display mode: >0 enables config+0x0F,
    // which VA011C34AD checks before drawing the top-right title/time text.
    // Keep this declaration: the entrance membership check uses its presence.
    if ((end - begin) % 17 != 0 || tokens.Skip(begin).Take(end - begin).Any(t => t.Kind != PvfTokenKind.Int32))
        throw new InvalidDataException("Unexpected dungeon schedule row shape.");
    var edits = new List<(int Dungeon, int Token)>();
    var seen = new HashSet<int>();
    for (var at = begin; at < end; at += 17)
    {
        var id = tokens[at].Value;
        if (!seen.Add(id)) throw new InvalidDataException("Duplicate dungeon schedule ID.");
        if (id is not (100 or 3700)) continue;
        if (tokens[at + 5].Value != 1) throw new InvalidDataException($"Dungeon {id}: expected notice mode 1.");
        edits.Add((id, at + 5));
    }
    if (edits.Count != 2) throw new InvalidDataException("Expected both Southern Dale and Tau Kingdom.");
    var originalScript = archive.GetFileBytes(targetIndex).ToArray();
    var patchedScript = originalScript.ToArray();
    foreach (var edit in edits)
    {
        var offset = edit.Token * PvfToken.Size;
        if (patchedScript[offset] != (byte)PvfTokenKind.Int32 || BinaryPrimitives.ReadInt32LittleEndian(patchedScript.AsSpan(offset + 1)) != 1)
            throw new InvalidDataException("Unexpected token byte encoding.");
        BinaryPrimitives.WriteInt32LittleEndian(patchedScript.AsSpan(offset + 1), 0);
    }
    var replacements = new Dictionary<int, byte[]> { [targetIndex] = patchedScript };
    var headerBytes=source.AsSpan(0,PvfHeader.Size).ToArray();PvfCipher.DecryptSection("HeaD",headerBytes);
    var header=PvfHeader.Read(headerBytes,source.Length);var layout=new SectionLayout(header);
    var table=source.AsSpan((int)layout.FileTableOffset,layout.FileTableLength).ToArray();
    var chunkTable=source.AsSpan((int)layout.ChunkTableOffset,layout.ChunkTableLength).ToArray();PvfCipher.DecryptSection("GRPI",chunkTable);
    if(BinaryPrimitives.ReadInt32LittleEndian(chunkTable.AsSpan(chunkTable.Length-8))!=header.BodySize)
        throw new InvalidDataException("Body has unindexed bytes; refuse to append a chunk.");
    using var raw=new MemoryStream();
    foreach(var (index,bytes) in replacements)
    {
        var row=table.AsSpan(index*PvfHeader.FileEntrySize,PvfHeader.FileEntrySize);
        BinaryPrimitives.WriteInt32LittleEndian(row[8..],header.ChunkCount);
        BinaryPrimitives.WriteInt32LittleEndian(row[12..],checked((int)raw.Length));
        BinaryPrimitives.WriteInt32LittleEndian(row[16..],bytes.Length);raw.Write(bytes);
    }
    using var compressed=new MemoryStream();
    using(var zlib=new ZLibStream(compressed,CompressionLevel.Optimal,leaveOpen:true))raw.WriteTo(zlib);
    var newChunk=compressed.ToArray();PvfCipher.DecryptSection("BodY",newChunk);
    var newChunkTable=new byte[chunkTable.Length+PvfHeader.ChunkEntrySize];chunkTable.CopyTo(newChunkTable,0);
    BinaryPrimitives.WriteInt32LittleEndian(newChunkTable.AsSpan(chunkTable.Length),checked(header.BodySize+newChunk.Length));
    BinaryPrimitives.WriteInt32LittleEndian(newChunkTable.AsSpan(chunkTable.Length+4),checked((int)raw.Length));
    BinaryPrimitives.WriteInt32LittleEndian(headerBytes.AsSpan(32),checked(header.BodySize+newChunk.Length));
    BinaryPrimitives.WriteInt32LittleEndian(headerBytes.AsSpan(36),checked(header.ChunkCount+1));
    PvfCipher.DecryptSection("HeaD",headerBytes);PvfCipher.DecryptSection("GRPI",newChunkTable);
    // Append one chunk and redirect only the selected entries. Existing hash/name pools and body chunks stay byte-identical.
    using(var target=new FileStream(output,FileMode.CreateNew,FileAccess.Write))
    {
        target.Write(headerBytes);target.Write(table);
        target.Write(source.AsSpan((int)layout.HashTableOffset,(int)(layout.ChunkTableOffset-layout.HashTableOffset)));
        target.Write(newChunkTable);target.Write(source.AsSpan((int)layout.BodyOffset,header.BodySize));target.Write(newChunk);
        target.Write(source.AsSpan((int)layout.BodyOffset+header.BodySize));
    }
    using var patched=PvfArchive.Open(output);
    if(patched.FileCount!=archive.FileCount || patched.ChunkCount!=archive.ChunkCount+1)
        throw new InvalidDataException("Archive counts changed unexpectedly.");
    for(var index=0;index<archive.FileCount;index++)
    {
        var before=archive.GetEntry(index);var after=patched.GetEntry(index);
        if(replacements.TryGetValue(index,out var expected))
        {
            if(after.NameOffset!=before.NameOffset || after.PathOffset!=before.PathOffset || after.DataType!=before.DataType
                || !patched.GetFileBytes(index).SequenceEqual(expected))throw new InvalidDataException("Patched entry mismatch.");
        }
        else if(before!=after)throw new InvalidDataException($"Unrelated file entry changed: {index}.");
    }
    var written=File.ReadAllBytes(output);var newBodyOffset=(int)layout.BodyOffset+PvfHeader.ChunkEntrySize;
    if(!written.AsSpan(newBodyOffset,header.BodySize).SequenceEqual(source.AsSpan((int)layout.BodyOffset,header.BodySize))
        || !written.AsSpan((int)layout.HashTableOffset,(int)(layout.ChunkTableOffset-layout.HashTableOffset))
            .SequenceEqual(source.AsSpan((int)layout.HashTableOffset,(int)(layout.ChunkTableOffset-layout.HashTableOffset))))
        throw new InvalidDataException("Existing content changed unexpectedly.");
    if (!written.AsSpan(newBodyOffset + header.BodySize + newChunk.Length)
        .SequenceEqual(source.AsSpan((int)layout.BodyOffset + header.BodySize)))
        throw new InvalidDataException("Archive trailer changed.");
    var expectedOffsets = edits.Select(edit => edit.Token * PvfToken.Size + 1).ToHashSet();
    var actualOffsets = Enumerable.Range(0, originalScript.Length).Where(i => originalScript[i] != patchedScript[i]).ToHashSet();
    if (!actualOffsets.SetEquals(expectedOffsets)) throw new InvalidDataException("Changes exceed the two notice modes.");
    var checkReader = patched.OpenScript(targetIndex);
    var readback = new List<PvfToken>();
    while (checkReader.Read(out var token)) readback.Add(token);
    if (readback.Count != tokens.Count) throw new InvalidDataException("Token count changed.");
    for (var i = 0; i < tokens.Count; i++)
        if (readback[i] != (edits.Any(e => e.Token == i) ? new PvfToken(PvfTokenKind.Int32, 0) : tokens[i]))
            throw new InvalidDataException($"Unrelated token changed: {i}.");
    var report = new
    {
        Source = input, Output = output, SourceSha256 = sourceHash,
        OutputSha256 = Convert.ToHexString(SHA256.HashData(written)),
        ModifiedPath = schedulePath, Field = "[dungeon] integer 6 (persistent notice mode)",
        Changes = edits.Select(e => new { DungeonId = e.Dungeon, TokenIndex = e.Token, Before = 1, After = 0 }).ToArray(),
        archive.FileCount, UnchangedFiles = archive.FileCount - 1,
        PreservedExistingChunks = archive.ChunkCount, PreservedOtherTokens = true,
        PreservedHashAndStringTables = true, PreservedExistingBodyBytes = true,
        PreservedTrailer = true, ActualChangedScriptBytes = actualOffsets.Count
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
