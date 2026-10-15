using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Dfo.Pvf;
using Dfo.Pvf.Codec;
using Dfo.Pvf.Container;
using Dfo.Pvf.Tokens;

try
{
    if (args.Length is not (2 or 3) || args.Length == 3 && args[2] is not ("--ticket" or "--sao-items" or "--sao-core"))
        throw new ArgumentException("Usage: PvfSaoTowerEntrancePatch <source.pvf> <new.pvf|--inspect|--audit-sao> [--ticket|--sao-items|--sao-core]");
    var ticketMode = args.Length == 3 && args[2] == "--ticket";
    var coreMode = args.Length == 3 && args[2] == "--sao-core";
    var itemsMode = args.Length == 3 && args[2] is "--sao-items" or "--sao-core";
    var input = Path.GetFullPath(args[0]);
    using var archive = PvfArchive.Open(input);
    Console.OutputEncoding = Encoding.UTF8;
    var audit = SaoContentAudit.Read(archive);
    if (args[1] == "--audit-sao")
    {
        Console.WriteLine(JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    if (args[1] == "--inspect")
    {
        var inspectIndex = archive.FindFile("worldmap/ui/Towers.xui");
        if (inspectIndex < 0) throw new InvalidDataException("Missing Towers.xui.");
        Console.WriteLine(archive.ReadUnicodeText(inspectIndex));
        Console.WriteLine("Raw prefix: " + Convert.ToHexString(archive.GetFileBytes(inspectIndex)[..32]));
        return 0;
    }
    var output = Path.GetFullPath(args[1]);
    if (input.Equals(output, StringComparison.OrdinalIgnoreCase) || File.Exists(output) || File.Exists(output + ".json"))
        throw new IOException("Refuse in-place editing or overwriting an existing output.");
    var source = File.ReadAllBytes(input);
    string[] targets;
    var replacements = new Dictionary<int, byte[]>();
    if (itemsMode)
    {
        targets = audit.Items.Where(x => x.Expirations.Length > 0).Select(x => x.Path).ToArray();
        foreach (var item in audit.Items.Where(x => x.Expirations.Length > 0))
        {
            foreach (var expiry in item.Expirations)
            {
                if (!DateTime.TryParseExact(expiry, "yyyy-MM-dd HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var expiryDate)
                    || expiryDate.Year >= 2026)
                    throw new InvalidDataException($"Unfamiliar or nonhistorical SAO expiration: {item.Path}: {expiry}");
            }
            var itemTokens = Tokens(item.Path);
            var expiryIndices = Enumerable.Range(0, itemTokens.Count).Where(i => IsTag(itemTokens[i], "[expiration date]")).ToArray();
            foreach (var expiryIndex in expiryIndices.Reverse())
            {
                if (expiryIndex + 1 >= itemTokens.Count || itemTokens[expiryIndex + 1].Kind != PvfTokenKind.QuotedString
                    || expiryIndex + 2 < itemTokens.Count && itemTokens[expiryIndex + 2].Kind != PvfTokenKind.Identifier)
                    throw new InvalidDataException($"Unexpected expiration shape: {item.Path}");
                itemTokens.RemoveRange(expiryIndex, 2);
            }
            replacements.Add(archive.FindFile(item.Path), Encode(itemTokens));
        }
        if (replacements.Count == 0 && !coreMode) throw new InvalidDataException("No historical SAO expirations remain; refuse an empty patch.");
    }
    else
    {
        targets = [ticketMode ? "stackable/event/chn_event_20141111_sao_event/10004322_material_enter.stk" : "worldmap/Towers.wdm"];
        var tokens = Tokens(targets[0]);
        if (ticketMode)
        {
            var expiry = Enumerable.Range(0, tokens.Count).Single(i => IsTag(tokens[i], "[expiration date]"));
            if (expiry + 2 != tokens.Count || !tokens[expiry + 1].IsString
                || archive.ResolveString(tokens[expiry + 1].Value) != "2014-12-30 06:00:00")
                throw new InvalidDataException("Unfamiliar SAO ticket expiration; re-audit before patching.");
            var name = Enumerable.Range(0, tokens.Count).Single(i => IsTag(tokens[i], "[name]"));
            if (!tokens[name + 1].IsString || archive.ResolveString(tokens[name + 1].Value) != "克雷娅的庇护")
                throw new InvalidDataException("Unexpected SAO ticket identity.");
            tokens.RemoveRange(expiry, 2);
        }
        else
        {
            var open = Enumerable.Range(0, tokens.Count).Single(i => IsTag(tokens[i], "[dungeon]"));
            var close = Enumerable.Range(0, tokens.Count).Single(i => IsTag(tokens[i], "[/dungeon]"));
            int[] expectedIds = [11000, -1, 11001, -1, 11006, -1, 11007, -1, 11008, -1, 3505, -1, 4001, -1];
            if (close - open - 1 != expectedIds.Length || !tokens.Skip(open + 1).Take(expectedIds.Length)
                .SequenceEqual(expectedIds.Select(x => new PvfToken(PvfTokenKind.Int32, x))))
                throw new InvalidDataException("Unfamiliar tower entrance list; re-audit before patching.");
            tokens.InsertRange(close, [new(PvfTokenKind.Int32, 3600), new(PvfTokenKind.Int32, -1)]);
            var sao = Tokens("dungeon/Towers/SAO/SAO_TowerOfDespair001.dgn");
            if (!sao.Any(token => IsTag(token, "[tower of despair sao]")))
                throw new InvalidDataException("Missing native SAO tower marker.");
            var uiIndex = archive.FindFile("worldmap/ui/Towers.xui");
            if (uiIndex < 0) throw new InvalidDataException("Missing native tower layout.");
            var ui = System.Xml.Linq.XDocument.Parse(archive.ReadUnicodeText(uiIndex).TrimEnd('\0'));
            var button = ui.Descendants("CNUIControlWorldmapBalloonForRDAR")
                .Single(x => (string?)x.Attribute("dungeonIndex") == "NUM:3600");
            if ((string?)button.Attribute("ID") != "STR:IDC_WORLDMAP_BUTTON6"
                || (string?)button.Attribute("Pos") != "VECTOR:280, 150")
                throw new InvalidDataException("Unfamiliar native SAO button; re-audit layout before patching.");
        }
        replacements[archive.FindFile(targets[0])] = Encode(tokens);
    }
    if (coreMode)
    {
        foreach (var (floor, width) in new[] { (25, 4), (55, 2) })
        {
            var path = $"dungeon/Towers/SAO/SAO_TowerOfDespair{floor:D3}.dgn";
            var dungeonTokens = Tokens(path);
            var size = Enumerable.Range(0, dungeonTokens.Count).Single(i => IsTag(dungeonTokens[i], "[size]"));
            var greed = Enumerable.Range(0, dungeonTokens.Count).Single(i => IsTag(dungeonTokens[i], "[greed]"));
            var start = Enumerable.Range(0, dungeonTokens.Count).Single(i => IsTag(dungeonTokens[i], "[start map]"));
            var boss = Enumerable.Range(0, dungeonTokens.Count).Single(i => IsTag(dungeonTokens[i], "[boss map]"));
            if (dungeonTokens[size + 1] != new PvfToken(PvfTokenKind.Int32, width)
                || dungeonTokens[size + 2] != new PvfToken(PvfTokenKind.Int32, 1)
                || greed != size + 3 || dungeonTokens[greed + 1].Kind != PvfTokenKind.QuotedString
                || archive.ResolveString(dungeonTokens[greed + 1].Value) != "BB" || start != greed + 2
                || !dungeonTokens.Skip(start + 1).Take(2).SequenceEqual(new[] { new PvfToken(PvfTokenKind.Int32, 0), new PvfToken(PvfTokenKind.Int32, 0) })
                || !dungeonTokens.Skip(boss + 1).Take(2).SequenceEqual(new[] { new PvfToken(PvfTokenKind.Int32, 0), new PvfToken(PvfTokenKind.Int32, 0) }))
                throw new InvalidDataException($"Unfamiliar SAO floor {floor} topology; re-audit before patching.");
            dungeonTokens[size + 1] = new(PvfTokenKind.Int32, 1);
            replacements.Add(archive.FindFile(path), Encode(dungeonTokens));
            targets = [.. targets, path];
        }
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
        ModifiedFiles=targets,Mode=coreMode ? "sao-core" : itemsMode ? "sao-items-permanent" : ticketMode ? "sao-ticket-permanent" : "sao-entrance",
        RemovedExpirations=itemsMode ? audit.Items.Where(x => x.Expirations.Length > 0).ToArray() : [],
        Audit=audit,
        AddedEntrance=ticketMode || itemsMode ? null : "3600 -1",NativeControl=ticketMode || itemsMode ? null : "IDC_WORLDMAP_BUTTON6",
        NativePosition=ticketMode || itemsMode ? null : "280,150",ModifiedFileCount=replacements.Count,
        InsertedTokenCount=ticketMode || itemsMode ? 0 : 2,RemovedTokenCount=itemsMode ? audit.Items.Sum(x => x.Expirations.Length) * 2 : ticketMode ? 2 : 0,ChangedTokenCount=coreMode ? 2 : 0,
        MazeSizeRepairs=coreMode ? new[] { "floor25:4x1->1x1", "floor55:2x1->1x1" } : [],
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
