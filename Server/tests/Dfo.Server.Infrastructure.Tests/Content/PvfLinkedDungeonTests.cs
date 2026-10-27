using Dfo.Server.Infrastructure.Content;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfLinkedDungeonTests
{
    [Fact, Trait("Category", "Unit")]
    public void Unrelated_next_and_prev_sections_do_not_create_an_entry_authorization()
    {
        Assert.Null(Parse(Section("[next]", 4114, 100, -1), Section("[prev]", 4103)));
        var definition = Parse(Section("[next]", 123, 1, -1), Section("[linked dungeon]"),
            Section("[next]", 4114, 50, -1, -1, 50, -1), Section("[/next]"), Section("[/linked dungeon]"),
            Section("[prev]", 999));
        Assert.NotNull(definition);
        Assert.Equal(4114, Assert.Single(definition.Next).DungeonId);
        Assert.Equal(50, definition.NoSelectionWeight);
        Assert.Empty(definition.PreviousDungeonIds);
    }

    [Fact, Trait("Category", "Unit")]
    public void Multiple_candidates_and_conditions_are_preserved_without_interpreting_them()
    {
        var definition = Parse(Section("[linked dungeon]"), Section("[next]", 300, 2, -1, 301, 2, 42, -1, 96, -1),
            Section("[/next]"), Section("[prev]", 2014, 2007), Section("[/prev]"),
            Section("[on clear add passive object]", 18961, -20, 290), Section("[/linked dungeon]"))!;
        Assert.Equal(new[] { 300, 301 }, definition.Next.Select(value => value.DungeonId));
        Assert.Equal(42, definition.Next[1].Condition);
        Assert.Equal(96, definition.NoSelectionWeight);
        Assert.Equal(new[] { 2014, 2007 }, definition.PreviousDungeonIds);
        Assert.Equal(-20, definition.ClearObject!.X);
    }

    public static TheoryData<PvfSection[]> InvalidDeclarations => new()
    {
        new[] { Section("[linked dungeon]") },
        new[] { Section("[/linked dungeon]") },
        new[] { Section("[linked dungeon]"), Section("[linked dungeon]"), Section("[/linked dungeon]") },
        Block(Section("[next]", 4114, 50)),
        Block(Section("[next]", 4114, -1, -1)),
        Block(Section("[next]", 0, 50, -1)),
        Block(Section("[next]", -2, 50, -1)),
        Block(Section("[next]", 4114, 50, -1, 4114, 50, -1)),
        Block(Section("[next]", -1, 50, 1)),
        Block(Section("[next]", -1, 50, -1, -1, 50, -1)),
        Block(Section("[next]", 4114, int.MaxValue, -1, -1, 1, -1)),
        Block(Section("[prev]", 0)),
        Block(Section("[prev]", 4103, 4103)),
        Block(Section("[on clear add passive object]", 54645, 543)),
        Block(Section("[on clear add passive object]", 54645, 543, 125), Section("[on clear add passive object]", 54645, 543, 125)),
        Block(Section("[unknown linked rule]", 1)),
        Block(new PvfSection("[next]", new[] { new PvfToken(PvfTokenKind.Single, 4114) })),
        Block(Section("[next]", 4114, 50, -1)),
        Block(Section("[/next]")),
        Block(Section("[prev]", 4103), Section("[/next]")),
        Block(Section("[next]", 4114, 50, -1), Section("[prev]", 4103), Section("[/prev]"), Section("[/next]")),
    };

    [Theory, Trait("Category", "Unit")]
    [MemberData(nameof(InvalidDeclarations))]
    public void Invalid_continuations_are_reported_with_the_source_path(PvfSection[] sections)
    {
        var error = Assert.Throws<InvalidDataException>(() => Parse(sections));
        Assert.Contains("dungeon/test.dgn", error.Message);
        Assert.Contains("[linked dungeon]", error.Message);
    }

    [SkippableFact]
    public void Every_linked_block_in_the_current_archive_is_readable()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var archive = provider.Archive;
        var linkedCount = 0;
        foreach (var (_, relative) in PvfListFile.Read(archive, "dungeon/dungeon.lst"))
        {
            var path = "dungeon/" + relative;
            if (archive.FindFile(path) < 0) continue;
            var definition = PvfLinkedDungeonParser.Parse(PvfDocument.Open(archive, path).Sections, path);
            if (definition is not null) linkedCount++;
        }
        Assert.True(linkedCount >= 2, "The real archive must contain both circus linked declarations.");
    }

    [SkippableFact]
    public async Task Circus_preserves_the_hidden_continuation_and_its_predecessor()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDungeonDefinitionSource(provider);
        var circus = (await source.GetAsync(4103, default))!;
        var theatre = (await source.GetAsync(4114, default))!;
        Assert.NotNull(circus.LinkedDungeon);
        var next = Assert.Single(circus.LinkedDungeon.Next);
        Assert.Equal(4114, next.DungeonId);
        Assert.Equal(50, next.Weight);
        Assert.Equal(-1, next.Condition);
        Assert.Equal(50, circus.LinkedDungeon.NoSelectionWeight);
        Assert.Equal(new(54645, 543, 125), circus.LinkedDungeon.ClearObject);
        Assert.NotNull(theatre.LinkedDungeon);
        Assert.Equal(new[] { 4103 }, theatre.LinkedDungeon.PreviousDungeonIds);
        Assert.Empty(theatre.LinkedDungeon.Next);
        Assert.Equal(0, theatre.LinkedDungeon.NoSelectionWeight);
        Assert.Null(theatre.LinkedDungeon.ClearObject);
    }

    private static Dfo.Server.Abstractions.Content.LinkedDungeonDefinition? Parse(params PvfSection[] sections) =>
        PvfLinkedDungeonParser.Parse(sections, "dungeon/test.dgn");
    private static PvfSection Section(string name, params int[] values) =>
        new(name, values.Select(value => new PvfToken(PvfTokenKind.Int32, value)).ToArray());
    private static PvfSection[] Block(params PvfSection[] sections) =>
        [Section("[linked dungeon]"), .. sections, Section("[/linked dungeon]")];
}
