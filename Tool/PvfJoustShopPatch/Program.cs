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
    if(args.Length!=2)throw new ArgumentException("Usage: PvfJoustShopPatch <source.pvf> <new.pvf>");
    var input=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[1]);
    if(input.Equals(output,StringComparison.OrdinalIgnoreCase) || File.Exists(output) || File.Exists(output+".json"))
        throw new IOException("Refuse in-place editing or overwriting an existing output.");
    var source=File.ReadAllBytes(input);
    using var archive=PvfArchive.Open(input);
    const string ordinaryPath="stackable/chn_490002916.stk";
    const string simplePath="stackable/cash/chn_490700609.stk";
    const string shopPath="itemshop/(r)EquipmentShop7.shp";
    var ordinary=Tokens(ordinaryPath);var simple=Tokens(simplePath);var shop=Tokens(shopPath);
    var priceTag=ordinary.Single(t=>IsTag(t,"[price]"));
    RequireInt(ordinary,"[price]",10000);RequireInt(ordinary,"[value]",45000);RequireInt(simple,"[value]",45000);
    RemoveExpiry(ordinary);RemoveExpiry(simple);
    if(simple.Any(t=>IsTag(t,"[price]")))throw new InvalidDataException("Simple coin already declares a price; review input first.");
    simple.InsertRange(simple.FindIndex(t=>IsTag(t,"[value]")),[priceTag,new(PvfTokenKind.Int32,10000)]);
    RequireInt(shop,"[NPC]",2);
    var tab=Enumerable.Range(0,shop.Count-1).Single(i=>IsTag(shop[i],"[tab]")
        && shop[i+1].IsString && archive.ResolveString(shop[i+1].Value)=="活动");
    var list=tab+2;
    if(!IsTag(shop[list],"[item list]"))throw new InvalidDataException("Unexpected activity tab layout.");
    var listEnd=shop.FindIndex(list+1,t=>IsTag(t,"[/item list]"));
    if(listEnd<0 || shop.Skip(list+1).Take(listEnd-list-1).Any(t=>t.Kind!=PvfTokenKind.Int32))
        throw new InvalidDataException("Unreadable activity item list.");
    if(shop.Any(t=>t.Kind==PvfTokenKind.Int32 && t.Value is 490002916 or 490700609))
        throw new InvalidDataException("Knight coins already occur in the shop; review input first.");
    shop.InsertRange(list+1,[new(PvfTokenKind.Int32,490002916),new(PvfTokenKind.Int32,490700609)]);
    var replacements=new Dictionary<int,byte[]>
    {
        [archive.FindFile(ordinaryPath)]=Encode(ordinary),
        [archive.FindFile(simplePath)]=Encode(simple),
        [archive.FindFile(shopPath)]=Encode(shop),
    };
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
    // Append one chunk and redirect only these three entries. Existing hash/name pools and body chunks stay byte-identical.
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
        UnchangedFiles=archive.FileCount-3,PreservedExistingChunks=archive.ChunkCount,
        ModifiedFiles=new[]{ordinaryPath,simplePath,shopPath},NpcId=2,ShopId=15,Tab="活动",
        Coins=new[]{new{ItemId=490002916,BuyGold=10000,SellGold=9000},new{ItemId=490700609,BuyGold=10000,SellGold=9000}},
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
    void RemoveExpiry(List<PvfToken> tokens)
    {
        var at=Enumerable.Range(0,tokens.Count).Single(i=>IsTag(tokens[i],"[expiration date]"));
        if(at+1>=tokens.Count || !tokens[at+1].IsString || archive.ResolveString(tokens[at+1].Value)!="2016-11-17 06:00:00")
            throw new InvalidDataException("Unexpected expiry; review input first.");
        tokens.RemoveRange(at,2);
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
