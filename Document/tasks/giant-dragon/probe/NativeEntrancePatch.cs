using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Dfo.Pvf;
using Dfo.Pvf.Codec;
using Dfo.Pvf.Container;
using Dfo.Pvf.Tokens;

// Restore existing resources without modifying any shared client or archive in place.
internal static class NativeEntrancePatch
{
    public static void Write(string sourcePath, string outputPath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        outputPath = Path.GetFullPath(outputPath);
        if (sourcePath.Equals(outputPath, StringComparison.OrdinalIgnoreCase) || File.Exists(outputPath) || File.Exists(outputPath + ".json"))
            throw new IOException("Refuse in-place editing or overwriting output.");
        using var input = File.OpenRead(sourcePath);
        var hash = Convert.ToHexString(SHA256.HashData(input));
        // The active 3822 archive was separately audited against D45: all 31 native
        // restoration inputs match. Preserve its unrelated existing patches.
        if (hash is not ("D45CF58B1A2B3D16D6B35ABE0D4E302E052F0B6BC43E9D3E98D815FED54AEF84"
            or "3822A9ED183CD17F74DFEE9C11B8F2E84AF596B23972A737846022148FB05C6B"))
            throw new InvalidDataException("Source identity changed; re-audit before applying native entrance changes.");
        using var archive = PvfArchive.Open(sourcePath);
        var edits = new Dictionary<int, byte[]>();
        var list = Tokens("town/town.lst");
        if (list.Any(t => t.Kind == PvfTokenKind.Int32 && t.Value == 100)) throw new InvalidDataException("Town100 already registered.");
        var town = archive.FindFile("town/chn_shonan_holy.twn");
        if (town < 0) throw new InvalidDataException("Missing native town file.");
        list.Add(new(PvfTokenKind.Int32, 100));
        list.Add(new(PvfTokenKind.QuotedString, archive.GetEntry(town).NameOffset));
        Add("town/town.lst", list);
        // Keep each existing rectangle. The new loop is Shonan -> restored town100 ->
        // original town18 -> Shonan; every original area remains reachable.
        Redirect("map/town/shonan/shonan_workshop.map", [54,208,30,135,18,2], 100, 3);
        Redirect("map/town/chn_dragon_shonan_holyland/gate_shonan_holy.map", [46,212,34,52,101,0], 18, 2);

        input.Position = 0;
        var headerBytes = new byte[PvfHeader.Size]; input.ReadExactly(headerBytes);
        PvfCipher.DecryptSection("HeaD", headerBytes);
        var header = PvfHeader.Read(headerBytes, input.Length);
        var layout = new SectionLayout(header);
        // Restore the quest list as well as the town. The native files were present but
        // unregistered; append relative paths to the wide pool without moving old offsets.
        var names = Read(layout.NameTableOffset, header.NameTableSize);
        var narrowLength = checked((int)(BinaryPrimitives.ReadUInt32LittleEndian(names.AsSpan(8)) ^ 0xAA74472Eu));
        var wideHeader = checked(16+narrowLength);
        var wideLength = checked((int)(BinaryPrimitives.ReadUInt32LittleEndian(names.AsSpan(wideHeader)) ^ 0x9A82F037u));
        var wideRawLength = BinaryPrimitives.ReadInt32LittleEndian(names.AsSpan(wideHeader+4)) ^ wideLength;
        var encryptedWide = names.AsSpan(wideHeader+8,wideLength).ToArray();
        PvfCipher.DecryptStringTable("sTrW",encryptedWide);
        using var wide = new MemoryStream();
        using(var zlib = new ZLibStream(new MemoryStream(encryptedWide),CompressionMode.Decompress)) zlib.CopyTo(wide);
        if(wide.Length!=wideRawLength || (wide.Length&1)!=0)throw new InvalidDataException("Invalid source wide pool.");
        var questList=Tokens("n_quest/quest.lst");
        var questPaths=new Dictionary<int,string>();
        for(var id=20720;id<=20735;id++)
        {
            if(questList.Any(t=>t.Kind==PvfTokenKind.Int32&&t.Value==id))throw new InvalidDataException($"Quest {id} already registered.");
            var kind=id is >=20729 and <=20731?"daily":"achievement";
            var path=$"event/chn_20140121_dragondungeon/chn_dragon_dungeon_{kind}_{id}.qst";
            if(archive.FindFile("n_quest/"+path)<0)throw new InvalidDataException($"Missing native quest {id}.");
            questList.Add(new(PvfTokenKind.Int32,id));
            questList.Add(new(PvfTokenKind.QuotedString,checked((int)wide.Length|1)));
            wide.Write(Encoding.Unicode.GetBytes(path+"\0"));questPaths.Add(id,path);
        }
        Add("n_quest/quest.lst",questList);
        using var wideCompressed=new MemoryStream();
        using(var zlib=new ZLibStream(wideCompressed,CompressionLevel.Optimal,true))wide.WriteTo(zlib);
        var newWide=wideCompressed.ToArray();PvfCipher.DecryptStringTable("sTrW",newWide);
        using var newNamesStream=new MemoryStream();newNamesStream.Write(names.AsSpan(0,wideHeader));
        var poolHeader=new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(poolHeader,(uint)newWide.Length^0x9A82F037u);
        BinaryPrimitives.WriteInt32LittleEndian(poolHeader.AsSpan(4),checked((int)wide.Length)^newWide.Length);
        newNamesStream.Write(poolHeader);newNamesStream.Write(newWide);
        newNamesStream.Write(names.AsSpan(wideHeader+8+wideLength));
        var newNames=newNamesStream.ToArray();
        var table = Read(layout.FileTableOffset, layout.FileTableLength);
        var chunks = Read(layout.ChunkTableOffset, layout.ChunkTableLength);
        PvfCipher.DecryptSection("GRPI", chunks);
        if (BinaryPrimitives.ReadInt32LittleEndian(chunks.AsSpan(chunks.Length-8)) != header.BodySize)
            throw new InvalidDataException("Unindexed body bytes; refuse append.");
        using var raw = new MemoryStream();
        foreach (var (id, bytes) in edits)
        {
            var row = table.AsSpan(id * PvfHeader.FileEntrySize, PvfHeader.FileEntrySize);
            BinaryPrimitives.WriteInt32LittleEndian(row[8..], header.ChunkCount);
            BinaryPrimitives.WriteInt32LittleEndian(row[12..], checked((int)raw.Length));
            BinaryPrimitives.WriteInt32LittleEndian(row[16..], bytes.Length);
            raw.Write(bytes);
        }
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true)) raw.WriteTo(zlib);
        var chunk = compressed.ToArray(); PvfCipher.DecryptSection("BodY", chunk);
        var newChunks = new byte[chunks.Length + PvfHeader.ChunkEntrySize]; chunks.CopyTo(newChunks, 0);
        BinaryPrimitives.WriteInt32LittleEndian(newChunks.AsSpan(chunks.Length), checked(header.BodySize + chunk.Length));
        BinaryPrimitives.WriteInt32LittleEndian(newChunks.AsSpan(chunks.Length+4), checked((int)raw.Length));
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes.AsSpan(32), checked(header.BodySize + chunk.Length));
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes.AsSpan(36), checked(header.ChunkCount+1));
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes.AsSpan(44), newNames.Length);
        PvfCipher.DecryptSection("HeaD", headerBytes); PvfCipher.DecryptSection("GRPI", newChunks);
        using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write))
        {
            output.Write(headerBytes); output.Write(table);
            Copy(output, layout.HashTableOffset, header.HashTableSize); output.Write(newNames);
            output.Write(newChunks);
            Copy(output, layout.BodyOffset, header.BodySize); output.Write(chunk);
            Copy(output, layout.BodyOffset + header.BodySize, input.Length - layout.BodyOffset - header.BodySize);
        }
        using var patched = PvfArchive.Open(outputPath);
        var patchedQuests=patched.OpenScript("n_quest/quest.lst");
        var pending=-1;var verified=0;
        while(patchedQuests.Read(out var token))
        {
            if(token.Kind==PvfTokenKind.Int32)pending=token.Value;
            else if(token.IsString&&questPaths.TryGetValue(pending,out var expected))
            {
                if(patched.ResolveString(token.Value)!=expected||patched.FindFile("n_quest/"+expected)<0)throw new InvalidDataException("Quest list readback mismatch.");
                verified++;
            }
        }
        if(verified!=16)throw new InvalidDataException("Native quest count mismatch.");
        for (var i=0; i<archive.FileCount; i++)
        {
            if (edits.TryGetValue(i, out var bytes))
            {
                if (!patched.GetFileBytes(i).SequenceEqual(bytes)) throw new InvalidDataException("Patched bytes differ.");
            }
            else if (archive.GetEntry(i) != patched.GetEntry(i)) throw new InvalidDataException("Unrelated entry changed.");
        }
        using var written = File.OpenRead(outputPath);
        var result = new { Source = sourcePath, Output = outputPath, SourceSha256 = hash,
            OutputSha256 = Convert.ToHexString(SHA256.HashData(written)), ModifiedFiles = edits.Keys.Select(i =>
                archive.ResolveString(archive.GetEntry(i).PathOffset) + "/" + archive.ResolveString(archive.GetEntry(i).NameOffset)).ToArray(),
            Rule = "User-authorized local restoration: town100/worldmap98 and native quests20720-20735; no JP live-entrance claim",
            RestoredQuests=questPaths.Keys.ToArray(), UnchangedEntries = archive.FileCount - edits.Count };
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath + ".json", json); Console.WriteLine(json);

        byte[] Read(long offset, int length) { input.Position=offset; var b=new byte[length]; input.ReadExactly(b); return b; }
        void Copy(Stream output, long offset, long length)
        {
            input.Position=offset; var buffer=new byte[65536];
            while(length>0) { var count=input.Read(buffer,0,(int)Math.Min(buffer.Length,length)); if(count==0)throw new EndOfStreamException(); output.Write(buffer,0,count); length-=count; }
        }
        List<PvfToken> Tokens(string path)
        {
            var id=archive.FindFile(path); if(id<0)throw new InvalidDataException(path);
            var reader=archive.OpenScript(id); var result=new List<PvfToken>(); while(reader.Read(out var t))result.Add(t); return result;
        }
        void Add(string path,List<PvfToken> tokens)
        {
            var bytes=new byte[tokens.Count*5]; for(var i=0;i<tokens.Count;i++){bytes[i*5]=(byte)tokens[i].Kind;BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i*5+1),tokens[i].Value);}
            edits.Add(archive.FindFile(path),bytes);
        }
        void Redirect(string path,int[] expected,int destinationTown,int destinationArea)
        {
            var tokens=Tokens(path); var matches=new List<int>(); var inTransit=false;
            for(var i=0;i<tokens.Count;i++)
            {
                if(tokens[i].Kind==PvfTokenKind.Identifier){inTransit=archive.ResolveString(tokens[i].Value)=="[town movable area]";continue;}
                if(inTransit && i+6<=tokens.Count && expected.Select((value,j)=>tokens[i+j]==new PvfToken(PvfTokenKind.Int32,value)).All(x=>x)) matches.Add(i);
            }
            var at=matches.Single(); tokens[at+4]=new(PvfTokenKind.Int32,destinationTown);tokens[at+5]=new(PvfTokenKind.Int32,destinationArea); Add(path,tokens);
        }
    }
}
