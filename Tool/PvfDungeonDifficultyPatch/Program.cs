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
    if(args.Length!=2)throw new ArgumentException("Usage: PvfDungeonDifficultyPatch <source.pvf> <new.pvf>");
    var input=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[1]);
    if(input.Equals(output,StringComparison.OrdinalIgnoreCase) || File.Exists(output) || File.Exists(output+".json"))
        throw new IOException("Refuse in-place editing or overwriting an existing output.");
    var source=File.ReadAllBytes(input);
    using var archive=PvfArchive.Open(input);
    const string expectedSourceHash="03154D18DE43F5A0BCB11B2935AE8BB40BABD22495700657D682E491A24A3543";
    if(Convert.ToHexString(SHA256.HashData(source))!=expectedSourceHash)
        throw new InvalidDataException("Unexpected source archive SHA-256; audit this archive before changing it.");
    string[] targets=[
        "dungeon/Act1/Lorien.dgn", "dungeon/Act1/LorienInside.dgn",
        "dungeon/Act1/MirkWood.dgn", "dungeon/Act1/MirkWoodDeep.dgn",
        "dungeon/Act1/SunderLand.dgn", "dungeon/SaintHorn/RollingStone.dgn",
        "dungeon/SaintHorn/BlackStone.dgn", "dungeon/SaintHorn/RedStone.dgn",
        "dungeon/SaintHorn/ElvengardSF.dgn", "dungeon/SaintHorn/HendonMyreSF.dgn",
        "dungeon/SaintHorn/EmpireRampart.dgn"
    ];
    var replacements=new Dictionary<int,byte[]>();
    foreach(var path in targets)
    {
        var tokens=Tokens(path);
        var occurrences=Enumerable.Range(0,tokens.Count)
            .Where(i=>IsTag(tokens[i],"[designate dungeon difficulty]")).ToArray();
        if(occurrences.Length!=2 || !ScalarAt(occurrences[0],0) || !ScalarAt(occurrences[1],1))
            throw new InvalidDataException($"{path}: expected exactly two scalar designations, 0 then 1.");
        RequireInt(tokens,"[difficulty]",1);
        // Remove exactly the old heading and its scalar zero. Every other token retains its order and value.
        tokens.RemoveRange(occurrences[0],2);
        RequireInt(tokens,"[designate dungeon difficulty]",1);
        replacements.Add(archive.FindFile(path),Encode(tokens));
        bool ScalarAt(int offset,int value)=>offset+1<tokens.Count
            && tokens[offset+1]==new PvfToken(PvfTokenKind.Int32,value)
            && (offset+2==tokens.Count || tokens[offset+2].Kind==PvfTokenKind.Identifier
                && archive.ResolveString(tokens[offset+2].Value).StartsWith('['));
    }
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
    var report=new
    {
        Source=input,Output=output,SourceSha256=Convert.ToHexString(SHA256.HashData(source)),
        OutputSha256=Convert.ToHexString(SHA256.HashData(written)),FileCount=archive.FileCount,
        UnchangedFiles=archive.FileCount-replacements.Count,PreservedExistingChunks=archive.ChunkCount,
        ModifiedFiles=targets,RemovedField="[designate dungeon difficulty] 0",KeptField="[designate dungeon difficulty] 1",
        ModifiedFileCount=replacements.Count,RemovedTokenCount=replacements.Count*2,
    };
    var json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});
    File.WriteAllText(output+".json",json);Console.WriteLine(json);
    return 0;

    bool IsTag(PvfToken token,string name)=>token.Kind==PvfTokenKind.Identifier && archive.ResolveString(token.Value)==name;
    List<PvfToken> Tokens(string path)
    {
        var index=archive.FindFile(path);if(index<0 || !archive.GetEntry(index).IsScript)throw new InvalidDataException($"Missing script: {path}");
        var reader=archive.OpenScript(index);var tokens=new List<PvfToken>();while(reader.Read(out var token))tokens.Add(token);return tokens;
    }
    void RequireInt(List<PvfToken> tokens,string field,int expected)
    {
        var at=Enumerable.Range(0,tokens.Count).Single(i=>IsTag(tokens[i],field));
        if(at+1>=tokens.Count || tokens[at+1]!=new PvfToken(PvfTokenKind.Int32,expected))throw new InvalidDataException($"Unexpected {field} value.");
    }
    static byte[] Encode(List<PvfToken> tokens)
    {
        var bytes=new byte[tokens.Count*PvfToken.Size];
        for(var i=0;i<tokens.Count;i++){bytes[i*5]=(byte)tokens[i].Kind;BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i*5+1),tokens[i].Value);}
        return bytes;
    }
}
catch(Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
