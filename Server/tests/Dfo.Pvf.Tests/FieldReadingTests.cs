using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Pvf.Tests.Support;

namespace Dfo.Pvf.Tests;

public sealed class FieldReadingTests
{
    private static readonly PvfField<int> Level = PvfField.Int32("[level]", minimum: 0, maximum: 86);
    private static readonly PvfField<ushort?> Durability = PvfField.OptionalUInt16("[durability]");
    private static readonly PvfField<IReadOnlyList<int>> Items = PvfField.Int32List("[items]");
    private static readonly PvfField<IReadOnlyList<double>> Rates = PvfField.Numbers("[rates]");
    private static readonly PvfField<IReadOnlyList<string>> Names = PvfField.Strings("[names]");

    [Fact]
    public void Static_fields_read_typed_values_and_optional_absence()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("", "test.equ").Tag("[unknown]").Quoted("ignored")
            .Tag("[level]").Int(70).Tag("[items]").Int(1).Int(2)
            .Tag("[rates]").Int(1).Single(0.5f).Tag("[names]").Quoted("[all]").Quoted("second").End();
        using var file = new SyntheticArchive(builder);
        var document = PvfDocument.Open(file.Archive, "test.equ");
        Assert.Equal(70, document.Read(Level));
        Assert.Null(document.Read(Durability));
        Assert.False(document.TryRead(Durability, out _));
        Assert.Equal(new[] { 1, 2 }, document.Read(Items));
        Assert.Equal(new[] { 1d, 0.5d }, document.Read(Rates));
        Assert.Equal(new[] { "[all]", "second" }, document.Read(Names));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("range")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    public void Malformed_required_values_report_source_and_field(string error)
    {
        var builder = new PvfArchiveBuilder();
        var script = builder.AddScript("", "bad.equ");
        if (error != "missing") script.Tag("[level]");
        if (error == "type") script.Quoted("seventy");
        if (error == "range") script.Int(87);
        if (error == "duplicate") script.Int(70).Tag("[level]").Int(71);
        script.End();
        using var file = new SyntheticArchive(builder);
        var document = PvfDocument.Open(file.Archive, "bad.equ");
        var exception = Assert.Throws<InvalidDataException>(() => document.Read(Level));
        Assert.Contains("bad.equ", exception.Message);
        Assert.Contains("[level]", exception.Message);
    }

    [Fact]
    public void Malformed_optional_value_is_not_absence()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("", "bad.equ").Tag("[durability]").Int(-1).End();
        using var file = new SyntheticArchive(builder);
        Assert.Throws<InvalidDataException>(() => PvfDocument.Open(file.Archive, "bad.equ").Read(Durability));
    }

    [Fact]
    public void Static_field_is_resolved_again_for_each_archive()
    {
        var first = new PvfArchiveBuilder();
        first.AddScript("", "test.equ").Tag("[level]").Int(10).End();
        var second = new PvfArchiveBuilder();
        second.InternUtf8("padding to change all offsets");
        second.AddScript("", "test.equ").Tag("[level]").Int(20).End();
        using var a = new SyntheticArchive(first);
        using var b = new SyntheticArchive(second);
        Assert.NotEqual(Level.Resolve(a.Archive).Offset, Level.Resolve(b.Archive).Offset);
        Assert.Equal(10, PvfDocument.Open(a.Archive, "test.equ").Read(Level));
        Assert.Equal(20, PvfDocument.Open(b.Archive, "test.equ").Read(Level));
    }

    [Fact]
    public void Repeated_rows_and_nested_markers_remain_ordered()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("", "test.equ").Tag("[block]").Tag("[items]").Int(1)
            .Tag("[block]").Tag("[items]").Int(2).Tag("[/block]").Tag("[/block]").End();
        using var file = new SyntheticArchive(builder);
        var document = PvfDocument.Open(file.Archive, "test.equ");
        Assert.Equal(new[] { 1, 2 }, document.ReadAll(Items).SelectMany(x => x));
        Assert.Equal(new[] { "[block]", "[items]", "[block]", "[items]", "[/block]", "[/block]" },
            document.Sections.Select(section => section.Name));
        Assert.Throws<InvalidDataException>(() => document.Read(Items));
    }

    [Fact]
    public void Truncated_record_is_rejected_before_any_field_read()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddRaw("", "bad.equ", [0, 1], 1);
        using var file = new SyntheticArchive(builder);
        Assert.Throws<InvalidDataException>(() => PvfDocument.Open(file.Archive, "bad.equ"));
    }

    [Fact]
    public void Numbered_field_family_parses_only_well_formed_names()
    {
        var family = new PvfFieldFamily("[growtype ");
        Assert.True(family.TryGetIndex("[growtype 2]", out var index));
        Assert.Equal(2, index);
        Assert.False(family.TryGetIndex("[growtype x]", out _));
    }
    [Fact]
    public void Public_tag_lookup_requires_a_field_declaration()
    {
        var lookups = typeof(PvfArchive).GetMethods().Where(method => method.Name == nameof(PvfArchive.GetTag));
        var lookup = Assert.Single(lookups);
        Assert.Equal(typeof(PvfField), Assert.Single(lookup.GetParameters()).ParameterType);
    }

    [Fact]
    public void Quoted_structure_marker_cannot_match_an_identifier_with_the_same_name()
    {
        var field = PvfField.Marker("[rectangle]", tokenKind: PvfTokenKind.QuotedString);
        var builder = new PvfArchiveBuilder();
        builder.AddScript("", "test.equ").Tag("[rectangle]").Quoted("[rectangle]").End();
        using var file = new SyntheticArchive(builder);
        var reader = file.Archive.OpenScript("test.equ");
        Assert.True(reader.Read(out var identifier));
        Assert.True(reader.Read(out var quoted));
        Assert.False(field.Matches(file.Archive, identifier));
        Assert.True(field.Matches(file.Archive, quoted));
        Assert.Throws<ArgumentException>(() => field.Resolve(file.Archive));
        Assert.Throws<ArgumentException>(() => PvfDocument.Open(file.Archive, "test.equ").Read(field));
    }
    [Fact]
    public void Streaming_script_entry_rejects_a_complete_prefix_followed_by_partial_record()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddRaw("", "bad.equ", [0, 1, 0, 0, 0, 0, 1], 1);
        using var file = new SyntheticArchive(builder);
        var error = Assert.Throws<InvalidDataException>(() => file.Archive.OpenScript("bad.equ"));
        Assert.Contains("bad.equ", error.Message);
    }

    [Fact]
    public void Bare_identifier_payload_is_not_mistaken_for_an_unrelated_section()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("", "test.equ").Tag("[names]").Tag("map").Quoted("boss").End();
        using var file = new SyntheticArchive(builder);
        var document = PvfDocument.Open(file.Archive, "test.equ");
        Assert.Equal(new[] { "map", "boss" }, document.Read(Names));
        document.ValidateFields(Names);
    }
}
