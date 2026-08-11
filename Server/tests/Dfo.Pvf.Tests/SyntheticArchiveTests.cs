using System.Text;
using Dfo.Pvf.Container;
using Dfo.Pvf.Tests.Support;
using Dfo.Pvf.Tokens;

namespace Dfo.Pvf.Tests;

/// <summary>
/// Format-level coverage against archives the test builds itself, so it runs anywhere
/// and can exercise shapes the shipping archive happens not to contain.
/// </summary>
public sealed class SyntheticArchiveTests
{
    private static PvfArchiveBuilder Sample()
    {
        var builder = new PvfArchiveBuilder();

        builder.AddScript("equipment", "sword.equ")
            .Tag("[name]").Quoted("Practice Sword")
            .Tag("[rarity]").Int(2)
            .Tag("[weight]").Int(35)
            .Tag("[impossible contents]").Quoted("upgrade").Quoted("disjoint")
            .End();

        builder.AddScript("equipment", "shield.equ")
            .Tag("[name]").Quoted("Practice Shield")
            .Tag("[rarity]").Int(1)
            .Tag("[weight]").Int(70)
            .End();

        // A second chunk proves chunk addressing, and a root-level file proves empty paths.
        builder.AddScript(string.Empty, "index.lst", chunk: 1)
            .Int(101).Quoted("sword.equ")
            .Int(102).Quoted("shield.equ")
            .End();

        builder.AddText("localization", "greeting.str", "hello", chunk: 1);

        return builder;
    }

    [Fact]
    public void Round_trips_every_file_it_was_given()
    {
        using var fixture = new SyntheticArchive(Sample());
        var archive = fixture.Archive;

        Assert.Equal(4, archive.FileCount);
        Assert.Equal(2, archive.ChunkCount);
        Assert.True(archive.FindFile("equipment/sword.equ") >= 0);
        Assert.True(archive.FindFile("equipment/shield.equ") >= 0);
        Assert.True(archive.FindFile("index.lst") >= 0);
        Assert.True(archive.FindFile("localization/greeting.str") >= 0);
    }

    [Fact]
    public void Reads_back_the_exact_records_that_were_written()
    {
        using var fixture = new SyntheticArchive(Sample());
        var archive = fixture.Archive;

        var reader = archive.OpenScript(archive.FindFile("equipment/sword.equ"));
        var name = archive.GetTag("[name]");
        Assert.True(reader.TryMovePast(name));
        Assert.True(reader.TryReadSectionValue(out var value));
        Assert.Equal(PvfTokenKind.QuotedString, value.Kind);
        Assert.Equal("Practice Sword", archive.ResolveString(value.Value));

        reader.Reset();
        Assert.True(reader.TryMovePast(archive.GetTag("[weight]")));
        Assert.True(reader.TryReadInt32(out var weight));
        Assert.Equal(35, weight);
    }

    [Fact]
    public void A_section_yields_all_of_its_values_and_stops_at_the_next_tag()
    {
        using var fixture = new SyntheticArchive(Sample());
        var archive = fixture.Archive;

        var reader = archive.OpenScript(archive.FindFile("equipment/sword.equ"));
        Assert.True(reader.TryMovePast(archive.GetTag("[impossible contents]")));

        var forbidden = new List<string>();
        while (reader.TryReadSectionValue(out var token))
            forbidden.Add(archive.ResolveString(token.Value));

        Assert.Equal(["upgrade", "disjoint"], forbidden);
    }

    [Fact]
    public void The_same_tag_in_two_files_shares_one_offset()
    {
        using var fixture = new SyntheticArchive(Sample());
        var archive = fixture.Archive;

        var tag = archive.GetTag("[rarity]");
        Assert.True(tag.IsResolved);

        foreach (var path in new[] { "equipment/sword.equ", "equipment/shield.equ" })
        {
            var reader = archive.OpenScript(archive.FindFile(path));
            Assert.True(reader.TryMovePast(tag), $"{path} did not match the interned tag offset");
        }
    }

    [Fact]
    public void Files_in_a_later_chunk_decode_correctly()
    {
        using var fixture = new SyntheticArchive(Sample());
        var archive = fixture.Archive;

        var index = archive.FindFile("index.lst");
        Assert.Equal(1, archive.GetEntry(index).ChunkIndex);

        var reader = archive.OpenScript(index);
        var pairs = new List<(int Id, string File)>();
        var pending = int.MinValue;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Int32)
                pending = token.Value;
            else if (token.IsString && pending != int.MinValue)
            {
                pairs.Add((pending, archive.ResolveString(token.Value)));
                pending = int.MinValue;
            }
        }

        Assert.Equal([(101, "sword.equ"), (102, "shield.equ")], pairs);
    }

    [Fact]
    public void Unicode_text_files_decode_as_text_not_records()
    {
        using var fixture = new SyntheticArchive(Sample());
        var archive = fixture.Archive;

        var index = archive.FindFile("localization/greeting.str");
        Assert.True(archive.GetEntry(index).IsUnicodeText);
        Assert.Equal("hello", archive.ReadUnicodeText(index));

        // A text file is not a script; opening it as one yields nothing rather than garbage.
        var reader = archive.OpenScript(index);
        Assert.False(reader.Read(out _));
    }

    [Fact]
    public void Script_files_return_empty_text_rather_than_mojibake()
    {
        using var fixture = new SyntheticArchive(Sample());
        var archive = fixture.Archive;

        Assert.Equal(string.Empty, archive.ReadUnicodeText(archive.FindFile("equipment/sword.equ")));
    }

    [Fact]
    public void A_file_with_no_content_is_readable_and_empty()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddRaw("etc", "empty.etc", [], dataType: 1);

        using var fixture = new SyntheticArchive(builder);
        var index = fixture.Archive.FindFile("etc/empty.etc");

        Assert.True(index >= 0);
        Assert.Equal(0, fixture.Archive.GetEntry(index).DataSize);
        Assert.True(fixture.Archive.GetFileBytes(index).IsEmpty);
    }

    [Fact]
    public void Utf16_names_resolve_through_the_wide_pool()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddRawWithUnicodeName("localization", "名前.str", Encoding.Unicode.GetBytes("値"), dataType: 3);

        using var fixture = new SyntheticArchive(builder);
        var index = fixture.Archive.FindFile("localization/名前.str");

        Assert.True(index >= 0);
        Assert.Equal("値", fixture.Archive.ReadUnicodeText(index));
        Assert.Equal("名前.str", fixture.Archive.ResolveString(fixture.Archive.GetEntry(index).NameOffset));
    }

    [Fact]
    public void An_empty_archive_opens_without_special_casing()
    {
        using var fixture = new SyntheticArchive(new PvfArchiveBuilder());

        Assert.Equal(0, fixture.Archive.FileCount);
        Assert.Equal(0, fixture.Archive.ChunkCount);
        Assert.Equal(-1, fixture.Archive.FindFile("anything"));
    }

    [Fact]
    public void Two_files_sharing_a_name_in_different_folders_stay_distinct()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("equipment", "a.equ").Int(1).End();
        builder.AddScript("stackable", "a.equ").Int(2).End();

        using var fixture = new SyntheticArchive(builder);
        var archive = fixture.Archive;

        var first = archive.FindFile("equipment/a.equ");
        var second = archive.FindFile("stackable/a.equ");

        Assert.NotEqual(first, second);
        Assert.Equal(1, ReadFirstInt(archive, first));
        Assert.Equal(2, ReadFirstInt(archive, second));
    }

    [Fact]
    public void Truncating_the_archive_is_reported_as_a_format_error()
    {
        var bytes = Sample().Build();
        var path = Path.Combine(Path.GetTempPath(), $"dfo-pvf-{Guid.NewGuid():N}.pvf");
        File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);

        try
        {
            Assert.Throws<PvfFormatException>(() => PvfArchive.Open(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Corrupting_the_body_is_reported_rather_than_returning_wrong_bytes()
    {
        var bytes = Sample().Build();
        // The body is the last section; flipping bits there breaks the zlib stream.
        for (var i = bytes.Length - 32; i < bytes.Length; i++)
            bytes[i] ^= 0xFF;

        var path = Path.Combine(Path.GetTempPath(), $"dfo-pvf-{Guid.NewGuid():N}.pvf");
        File.WriteAllBytes(path, bytes);

        try
        {
            using var archive = PvfArchive.Open(path);
            var index = archive.FindFile("index.lst");
            Assert.Throws<PvfFormatException>(() => archive.GetFileBytes(index).Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Eviction_does_not_change_bytes_a_caller_already_holds()
    {
        using var fixture = new SyntheticArchive(Sample(), new PvfArchiveOptions { ChunkCacheBytes = 1 });
        var archive = fixture.Archive;

        var held = archive.GetFileBytes(archive.FindFile("equipment/sword.equ"));
        var expected = held.ToArray();

        // Touch the other chunk repeatedly to force the first one out.
        for (var i = 0; i < 20; i++)
        {
            _ = archive.GetFileBytes(archive.FindFile("index.lst"));
            _ = archive.GetFileBytes(archive.FindFile("equipment/shield.equ"));
        }

        Assert.True(archive.Metrics.ChunkEvictions > 0, "the budget should have forced eviction");
        Assert.True(held.SequenceEqual(expected));
    }

    private static int ReadFirstInt(PvfArchive archive, int fileIndex)
    {
        var reader = archive.OpenScript(fileIndex);
        Assert.True(reader.TryReadInt32(out var value));
        return value;
    }
}
