using Dfo.Pvf.Schema;
using Dfo.Pvf.Tests.Support;

namespace Dfo.Pvf.Tests;

public sealed class PvfRootSectionTests
{
    private static readonly PvfField<int> Level = PvfField.Int32("[level]");
    private static readonly PvfField<IReadOnlyList<Dfo.Pvf.Tokens.PvfToken>> Unknown = PvfField.Tokens("[unknown]");

    [Fact]
    public void Root_reader_excludes_same_named_nested_fields_and_preserves_the_original_document()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("", "test.etc").Tag("[level]").Int(70).Tag("[unknown]")
            .Tag("[level]").Int(80).Tag("[inner]").Tag("[level]").Int(90)
            .Tag("[/inner]").Tag("[/unknown]").Tag("[after]").Int(5).End();
        using var file = new SyntheticArchive(builder);
        var document = PvfDocument.Open(file.Archive, "test.etc");
        Assert.Throws<InvalidDataException>(() => document.Read(Level));
        var roots = document.AtRoot();
        Assert.Equal(70, roots.Read(Level));
        Assert.Equal(new[] { "[level]", "[unknown]", "[after]" }, roots.Sections.Select(section => section.Name));
        Assert.Equal(8, document.Sections.Count);
        Assert.Equal(roots.Sections, roots.AtRoot().Sections);
    }

    [Fact]
    public void Unknown_repeated_blocks_remain_root_occurrences_while_their_payloads_are_excluded()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("", "test.etc").Tag("[unknown]").Int(1).Tag("[level]").Quoted("nested text")
            .Tag("[/unknown]").Tag("[unknown]").Int(2).Tag("[unknown]").Int(3)
            .Tag("[level]").Int(90).Tag("[/unknown]").Tag("[/unknown]").Tag("[level]").Int(50).End();
        using var file = new SyntheticArchive(builder);
        var roots = PvfDocument.Open(file.Archive, "test.etc").AtRoot();
        Assert.Equal(50, roots.Read(Level));
        Assert.Equal(new[] { 1, 2 }, roots.ReadAll(Unknown).Select(values => Assert.Single(values).Value));
    }

    [Fact]
    public void Repeated_root_scalars_still_fail_unique_read_instead_of_losing_one_occurrence()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("", "test.etc").Tag("[level]").Int(70).Tag("[unknown]")
            .Tag("[level]").Int(80).Tag("[/unknown]").Tag("[level]").Int(71).End();
        using var file = new SyntheticArchive(builder);
        var roots = PvfDocument.Open(file.Archive, "test.etc").AtRoot();
        Assert.Equal(new[] { 70, 71 }, roots.ReadAll(Level));
        Assert.Contains("duplicate field", Assert.Throws<InvalidDataException>(() => roots.Read(Level)).Message);
    }

    [Fact]
    public void Unpaired_headings_and_quoted_closing_words_do_not_hide_subsequent_root_fields()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("", "test.etc").Tag("[unknown]").Quoted("[/unknown]")
            .Tag("[other]").Int(1).Tag("[level]").Int(70).End();
        using var file = new SyntheticArchive(builder);
        var roots = PvfDocument.Open(file.Archive, "test.etc").AtRoot();
        Assert.Equal(70, roots.Read(Level));
        Assert.Equal(3, roots.Sections.Count);
    }

    [Fact]
    public void Block_opening_operands_are_kept_at_root_without_exposing_nested_fields()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("", "test.etc").Tag("[unknown]").Int(4).Tag("[level]").Int(80)
            .Tag("[/unknown]").Tag("[level]").Int(70).End();
        using var file = new SyntheticArchive(builder);
        var roots = PvfDocument.Open(file.Archive, "test.etc").AtRoot();
        Assert.Equal(4, roots.Read(PvfField.Int32("[unknown]")));
        Assert.Equal(70, roots.Read(Level));
    }

    [Fact]
    public void Malformed_root_scalar_is_not_replaced_with_a_valid_nested_value()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("", "test.etc").Tag("[level]").Quoted("invalid")
            .Tag("[unknown]").Tag("[level]").Int(70).Tag("[/unknown]").End();
        using var file = new SyntheticArchive(builder);
        var error = Assert.Throws<InvalidDataException>(() => PvfDocument.Open(file.Archive, "test.etc").AtRoot().Read(Level));
        Assert.Contains("[level]", error.Message);
        Assert.Contains("expected exactly one Int32", error.Message);
    }
}
