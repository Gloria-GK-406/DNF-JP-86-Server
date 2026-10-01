using Dfo.Pvf.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfSkillPointTypedReadingTests
{
    [Theory]
    [InlineData("[required level]", "string")]
    [InlineData("[required level]", "empty")]
    [InlineData("[required level]", "extra")]
    [InlineData("[purchase cost]", "string")]
    [InlineData("[maximum level]", "string")]
    public void A_later_valid_occurrence_cannot_hide_an_earlier_bad_known_field(string field, string damage)
    {
        var builder = BaseArchive();
        var script = builder.AddScript("skill", "test.skl").Tag(field);
        if (damage == "string") script.Quoted("bad");
        if (damage == "extra") script.Int(5).Int(6);
        script.Tag(field).Int(10).Tag("[name]").Quoted("test").End();
        using var file = new BuiltArchive(builder);
        var error = Assert.Throws<InvalidDataException>(() => new PvfSkillPointDefinitionSource(file.Provider).Get(0));
        Assert.Contains(field, error.Message);
        Assert.Contains("skill/test.skl", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_unselected_SP_or_TP_purchase_branch_is_still_validated(bool usesTp)
    {
        var builder = BaseArchive();
        var badField = usesTp ? "[purchase cost]" : "[special purchase cost]";
        var selectedField = usesTp ? "[special purchase cost]" : "[purchase cost]";
        builder.AddScript("skill", "test.skl").Tag("[feature skill type]").Int(usesTp ? 1 : 0)
            .Tag(selectedField).Int(10).Int(20).Tag(badField).Int(5).Quoted("bad").End();
        using var file = new BuiltArchive(builder);
        var error = Assert.Throws<InvalidDataException>(() => new PvfSkillPointDefinitionSource(file.Provider).Get(0));
        Assert.Contains(badField, error.Message);
        Assert.Contains("skill/test.skl", error.Message);
    }

    [Fact]
    public void A_later_valid_prerequisite_does_not_hide_an_earlier_incomplete_pair()
    {
        var builder = BaseArchive();
        builder.AddScript("skill", "test.skl").Tag("[pre required skill]").Int(10)
            .Tag("[pre required skill]").Int(11).Int(2).End();
        using var file = new BuiltArchive(builder);
        var error = Assert.Throws<InvalidDataException>(() => new PvfSkillPointDefinitionSource(file.Provider).Get(0));
        Assert.Contains("[pre required skill]", error.Message);
        Assert.Contains("skill/test.skl", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_odd_SP_table_tail_is_rejected_even_when_a_complete_table_follows(bool overridden)
    {
        var builder = BaseArchive(addSpTable: false);
        builder.AddScript("skill", "test.skl").Tag("[name]").Quoted("test").End();
        var sp = builder.AddScript("etc", "spTable.etc").Tag("[sp table]").Int(1).Int(10).Int(2);
        if (overridden) sp.Tag("[sp table]").Int(1).Int(10).Int(2).Int(20);
        sp.End();
        using var file = new BuiltArchive(builder);
        var error = Assert.Throws<InvalidDataException>(() => new PvfSkillPointDefinitionSource(file.Provider).Get(0));
        Assert.Contains("[sp table]", error.Message);
        Assert.Contains("Etc/spTable.etc", error.Message);
    }

    [Fact]
    public void Valid_repeated_numeric_declarations_keep_the_existing_last_value_projection()
    {
        var builder = BaseArchive(addSpTable: false);
        builder.AddScript("skill", "test.skl").Tag("[name]").Quoted("test")
            .Tag("[feature skill type]").Int(1).Tag("[feature skill type]").Int(0)
            .Tag("[purchase cost]").Int(1).Int(2).Tag("[purchase cost]").Int(10).Int(20)
            .Tag("[special purchase cost]").Int(30).Int(40)
            .Tag("[required level]").Int(1).Tag("[required level]").Int(15)
            .Tag("[pre required skill]").Int(11).Int(1).Tag("[pre required skill]").Int(12).Int(2).End();
        builder.AddScript("etc", "spTable.etc").Tag("[sp table]").Int(1).Int(5)
            .Tag("[sp table]").Int(1).Int(10).Int(2).Int(20).End();
        using var file = new BuiltArchive(builder);
        var catalog = new PvfSkillPointDefinitionSource(file.Provider).Get(0);
        var skill = Assert.Single(catalog.Skills).Value;
        Assert.False(skill.UsesTp);
        Assert.Equal(new[] { 10, 20 }, skill.Costs);
        Assert.Equal(15, skill.RequiredLevel);
        Assert.Equal(new SkillPrerequisite(12, 2), Assert.Single(skill.Prerequisites));
        Assert.Equal(new[] { (1, 10), (2, 20) }, catalog.SpTable);
    }

    [Fact]
    public void The_real_nested_fixed_level_form_keeps_a_zero_base_and_its_interval()
    {
        var builder = BaseArchive();
        builder.AddScript("skill", "test.skl").Tag("[fixed level skill]")
            .Tag("[interval level]").Int(5).Tag("[add level per interval]").Int(1)
            .Tag("[is apply contract skillmaster]").Int(1).Tag("[/fixed level skill]").End();
        using var file = new BuiltArchive(builder);
        var skill = Assert.Single(new PvfSkillPointDefinitionSource(file.Provider).Get(0).Skills).Value;
        Assert.True(skill.IsFixedLevel);
        Assert.Equal(0, skill.FixedLevelBase);
        Assert.Equal(5, skill.FixedLevelInterval);
        Assert.Equal(1, skill.FixedLevelAddPerInterval);
    }

    [Fact]
    public void A_legacy_scalar_fixed_level_base_keeps_its_existing_projection()
    {
        var builder = BaseArchive();
        builder.AddScript("skill", "test.skl").Tag("[fixed level skill]").Int(3)
            .Tag("[interval level]").Int(5).Tag("[add level per interval]").Int(2).End();
        using var file = new BuiltArchive(builder);
        var skill = Assert.Single(new PvfSkillPointDefinitionSource(file.Provider).Get(0).Skills).Value;
        Assert.True(skill.IsFixedLevel);
        Assert.Equal(3, skill.FixedLevelBase);
        Assert.Equal(5, skill.FixedLevelInterval);
        Assert.Equal(2, skill.FixedLevelAddPerInterval);
    }

    [Fact]
    public void Separate_balanced_fixed_level_blocks_keep_the_last_value_projection()
    {
        var builder = BaseArchive();
        builder.AddScript("skill", "test.skl").Tag("[fixed level skill]")
            .Tag("[interval level]").Int(3).Tag("[add level per interval]").Int(2)
            .Tag("[/fixed level skill]").Tag("[fixed level skill]")
            .Tag("[interval level]").Int(5).Tag("[add level per interval]").Int(1)
            .Tag("[/fixed level skill]").End();
        using var file = new BuiltArchive(builder);
        var skill = Assert.Single(new PvfSkillPointDefinitionSource(file.Provider).Get(0).Skills).Value;
        Assert.True(skill.IsFixedLevel);
        Assert.Equal(0, skill.FixedLevelBase);
        Assert.Equal(5, skill.FixedLevelInterval);
        Assert.Equal(1, skill.FixedLevelAddPerInterval);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("single")]
    [InlineData("extra")]
    public void A_fixed_level_opening_rejects_payloads_outside_the_scalar_or_empty_union(string damage)
    {
        var builder = BaseArchive();
        var script = builder.AddScript("skill", "test.skl").Tag("[fixed level skill]");
        if (damage == "string") script.Quoted("bad");
        if (damage == "single") script.Single(1);
        if (damage == "extra") script.Int(1).Int(2);
        script.Tag("[fixed level skill]").Int(3).End();
        using var file = new BuiltArchive(builder);
        var error = Assert.Throws<InvalidDataException>(() => new PvfSkillPointDefinitionSource(file.Provider).Get(0));
        Assert.Contains("[fixed level skill]", error.Message);
        Assert.Contains("skill/test.skl", error.Message);
    }

    [Theory]
    [InlineData("unclosed", "[fixed level skill]")]
    [InlineData("double marker", "[fixed level skill]")]
    [InlineData("nested scalar", "[fixed level skill]")]
    [InlineData("orphan close", "[/fixed level skill]")]
    [InlineData("close payload", "[/fixed level skill]")]
    public void Fixed_level_blocks_require_complete_matching_boundaries(string damage, string field)
    {
        var builder = BaseArchive();
        var script = builder.AddScript("skill", "test.skl");
        if (damage == "orphan close") script.Tag("[/fixed level skill]");
        else
        {
            script.Tag("[fixed level skill]").Tag("[interval level]").Int(5);
            if (damage == "double marker") script.Tag("[fixed level skill]").Tag("[/fixed level skill]");
            if (damage == "nested scalar") script.Tag("[fixed level skill]").Int(3).Tag("[/fixed level skill]");
            if (damage == "close payload") script.Tag("[/fixed level skill]").Int(1);
        }
        script.End();
        using var file = new BuiltArchive(builder);
        var error = Assert.Throws<InvalidDataException>(() => new PvfSkillPointDefinitionSource(file.Provider).Get(0));
        Assert.Contains(field, error.Message);
        Assert.Contains("skill/test.skl", error.Message);
    }

    [Theory]
    [InlineData("[interval level]")]
    [InlineData("[add level per interval]")]
    [InlineData("[is apply contract skillmaster]")]
    public void Each_known_nested_child_is_validated_before_a_later_valid_occurrence(string field)
    {
        var builder = BaseArchive();
        builder.AddScript("skill", "test.skl").Tag("[fixed level skill]")
            .Tag(field).Quoted("bad").Tag(field).Int(1).Tag("[/fixed level skill]").End();
        using var file = new BuiltArchive(builder);
        var error = Assert.Throws<InvalidDataException>(() => new PvfSkillPointDefinitionSource(file.Provider).Get(0));
        Assert.Contains(field, error.Message);
        Assert.Contains("skill/test.skl", error.Message);
    }

    private static PvfArchiveBuilder BaseArchive(bool addSpTable = true)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("skill", "skilllist.lst").Int(0).Quoted("job.lst").End();
        builder.AddScript("skill", "job.lst").Int(7).Quoted("test.skl").End();
        // This catalog deliberately has no job-zero character script, so the test isolates
        // skill/SP declarations rather than making a second reader part of its failure path.
        builder.AddScript("character", "character.lst").Int(1).Quoted("unused.chr").End();
        if (addSpTable) builder.AddScript("etc", "spTable.etc").Tag("[sp table]").Int(1).Int(10).End();
        return builder;
    }

    [Fact]
    public void Legal_repeated_skill_names_keep_the_original_first_string_projection()
    {
        var builder = BaseArchive();
        builder.AddScript("skill", "test.skl").Tag("[name]").Quoted("first")
            .Tag("[name]").Quoted("second").End();
        using var file = new BuiltArchive(builder);
        var skill = Assert.Single(new PvfSkillPointDefinitionSource(file.Provider).Get(0).Skills).Value;
        Assert.Equal("first", skill.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void First_string_projection_still_validates_every_repeated_declaration(bool badFirst)
    {
        var builder = BaseArchive();
        var script = builder.AddScript("skill", "test.skl").Tag("[name]");
        if (badFirst) script.Int(1); else script.Quoted("first");
        script.Tag("[name]");
        if (badFirst) script.Quoted("second"); else script.Int(2);
        script.End();
        using var file = new BuiltArchive(builder);
        var error = Assert.Throws<InvalidDataException>(() => new PvfSkillPointDefinitionSource(file.Provider).Get(0));
        Assert.Contains("[name]", error.Message);
        Assert.Contains("skill/test.skl", error.Message);
    }

    private sealed class BuiltArchive : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pvf-skill-typed-{Guid.NewGuid():N}.pvf");
        internal PvfArchiveProvider Provider { get; }
        internal BuiltArchive(PvfArchiveBuilder builder)
        {
            File.WriteAllBytes(path, builder.Build());
            Provider = new(new PvfContentOptions { ArchivePath = path });
        }
        public void Dispose() { Provider.Dispose(); File.Delete(path); }
    }
}
