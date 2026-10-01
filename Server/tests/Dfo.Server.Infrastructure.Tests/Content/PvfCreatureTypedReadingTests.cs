using Dfo.Pvf.Tests.Support;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfCreatureTypedReadingTests
{
    [Fact]
    public async Task Repeated_scoped_definitions_keep_last_scalars_and_append_lists_without_town_lines()
    {
        using var file = new BuiltArchive(Build());
        var source = new PvfCreatureDefinitionSource(file.Provider);
        var creature = await source.GetAsync(1, default);
        Assert.NotNull(creature);
        Assert.Equal("second", creature.Name);
        Assert.Equal(2, creature.StartLevel);
        Assert.Equal(new[] { "red", "blue" }, creature.ArtifactSlots);
        Assert.Equal(new[] { 20, 30 }, creature.Evolutions.Select(value => value.RequiredLevel!.Value));
        Assert.Equal(new[] { 2, 3 }, creature.Evolutions.Select(value => value.EvolvesIntoCreatureId));
        Assert.Equal(new[] { "general" }, creature.AmbientLines);
        Assert.Equal(1, (await source.FindByEquipmentAsync(7, default))?.CreatureId);
    }

    [Fact]
    public async Task Rules_keep_selective_growth_hunger_and_finite_integer_or_single_artifact_rate()
    {
        using var file = new BuiltArchive(Build());
        var source = new PvfCreatureRuleSource(file.Provider, new PvfCreatureDefinitionSource(file.Provider));
        var rule = await source.GetAsync(7, default);
        Assert.NotNull(rule);
        Assert.False(rule.HungerEnabled);
        Assert.Equal(50, rule.MaxLevel);
        Assert.Equal(new[] { 2, 5, 9 }, rule.ExperienceThresholds);
        var artifact = await source.GetArtifactExperienceAsync(9, default);
        Assert.NotNull(artifact);
        Assert.Equal(10, artifact.RequiredCreatureLevel);
        Assert.Equal(12.5f, artifact.BonusPercent);
    }

    [Theory]
    [InlineData("[name]")]
    [InlineData("[start level]")]
    [InlineData("[permission level]")]
    [InlineData("[max level]")]
    [InlineData("[artifact slot]")]
    [InlineData("[evolution level]")]
    [InlineData("[evolution quest]")]
    [InlineData("[evolution creature id]")]
    [InlineData("[room list]")]
    [InlineData("[ambient]")]
    [InlineData("[equipment type]")]
    [InlineData("[output index]")]
    [InlineData("[creature species]")]
    public async Task Definition_rejects_bad_complete_known_sections_instead_of_filtering_tokens(string field)
    {
        using var file = new BuiltArchive(Build(field));
        var source = new PvfCreatureDefinitionSource(file.Provider);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => source.GetAllAsync(default).AsTask());
        Assert.Contains(field, error.Message);
    }

    [Theory]
    [InlineData("[creature minimum level]")]
    [InlineData("[creature experience amount rate]")]
    [InlineData("[sub type]")]
    [InlineData("[using exp item]")]
    [InlineData("[grow Atype item index]")]
    [InlineData("[grow Btype item index]")]
    [InlineData("[selective grow creature]")]
    [InlineData("nan")]
    public async Task Rule_rejects_wrong_types_or_nonfinite_values_instead_of_treating_them_as_missing(string field)
    {
        using var file = new BuiltArchive(Build(field));
        var source = new PvfCreatureRuleSource(file.Provider, new PvfCreatureDefinitionSource(file.Provider));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => source.GetAsync(7, default).AsTask());
        Assert.Contains(field == "nan" ? "[creature experience amount rate]" : field, error.Message);
    }

    private static PvfArchiveBuilder Build(string? defect = null)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("creature", "ExpTable.tbl").Int(2).Int(5).Int(9).End();
        builder.AddScript("creature", "creature.lst").Int(1).Quoted("test.cre").End();
        builder.AddScript("equipment", "equipment.lst").Int(7).Quoted("creature/test.equ")
            .Int(9).Quoted("creature/artifact.equ").End();
        var creature = builder.AddScript("creature", "test.cre").Tag("[name]").Quoted("first");
        if (defect == "[name]") creature.Int(1);
        creature.Tag("[start level]").Int(1);
        if (defect == "[start level]") creature.Quoted("bad");
        creature.Tag("[permission level]").Int(1);
        if (defect == "[permission level]") creature.Quoted("bad");
        creature.Tag("[max level]").Int(50);
        if (defect == "[max level]") creature.Quoted("bad");
        creature.Tag("[artifact slot]").Quoted("red");
        if (defect == "[artifact slot]") creature.Int(1);
        creature.Tag("[/artifact slot]").Tag("[evolution level]").Int(20);
        if (defect == "[evolution level]") creature.Quoted("bad");
        creature.Tag("[evolution quest]").Int(100);
        if (defect == "[evolution quest]") creature.Quoted("bad");
        creature.Tag("[evolution creature id]").Int(2);
        if (defect == "[evolution creature id]") creature.Quoted("bad");
        creature.Tag("[alternate]").Tag("[name]").Quoted("second").Tag("[start level]").Int(2)
            .Tag("[artifact slot]").Quoted("blue").Tag("[/artifact slot]")
            .Tag("[evolution level]").Int(30).Tag("[evolution quest]").Int(101)
            .Tag("[evolution creature id]").Int(3).Tag("[/alternate]")
            .Tag("[selective grow creature]");
        if (defect == "[selective grow creature]") creature.Int(1);
        creature.Tag("[using exp item]").Int(1);
        if (defect == "[using exp item]") creature.Quoted("bad");
        creature.Tag("[grow Atype item index]").Int(77);
        if (defect == "[grow Atype item index]") creature.Quoted("bad");
        creature.Tag("[grow Btype item index]").Int(78);
        if (defect == "[grow Btype item index]") creature.Quoted("bad");
        creature.Tag("[/selective grow creature]").End();

        var equipment = builder.AddScript("equipment/creature", "test.equ").Tag("[equipment type]").Quoted("[creature]").Int(0);
        if (defect == "[equipment type]") equipment.Quoted("bad");
        equipment.Tag("[sub type]").Int(0);
        if (defect == "[sub type]") equipment.Quoted("bad");
        equipment.Tag("[creature species]").Int(1);
        if (defect == "[creature species]") equipment.Quoted("bad");
        equipment.Tag("[output index]").Int(8);
        if (defect == "[output index]") equipment.Quoted("bad");
        equipment.Tag("[creature minimum level]");
        if (defect == "[creature minimum level]") equipment.Quoted("bad");
        else equipment.Int(10);
        equipment.Tag("[creature experience amount rate]");
        if (defect == "nan") equipment.Single(float.NaN);
        else equipment.Int(10);
        if (defect == "[creature experience amount rate]") equipment.Quoted("bad");
        equipment.End();
        builder.AddScript("equipment/creature", "artifact.equ").Tag("[equipment type]").Quoted("[artifact red]").Int(10)
            .Tag("[creature minimum level]").Int(10).Tag("[creature experience amount rate]").Single(12.5f).End();

        var ambient = builder.AddScript("creature/script", "test.wrd").Tag("[ambient]");
        if (defect == "[ambient]") ambient.Int(1);
        ambient.Tag("[room list]").Quoted("general");
        if (defect == "[room list]") ambient.Int(1);
        ambient.Tag("[in town]").Quoted("town only").Tag("[/in town]").Tag("[/room list]")
            .Tag("[/ambient]").Tag("[trigger]").Tag("[room list]")
            .Tag("[renamed]").Quoted("trigger only").Tag("[/renamed]")
            .Tag("[/room list]").Tag("[/trigger]").End();
        return builder;
    }

    private sealed class BuiltArchive : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pvf-creature-typed-{Guid.NewGuid():N}.pvf");
        internal PvfArchiveProvider Provider { get; }
        internal BuiltArchive(PvfArchiveBuilder builder)
        {
            File.WriteAllBytes(path, builder.Build());
            Provider = new(new PvfContentOptions { ArchivePath = path });
        }
        public void Dispose()
        {
            Provider.Dispose();
            File.Delete(path);
        }
    }
}
