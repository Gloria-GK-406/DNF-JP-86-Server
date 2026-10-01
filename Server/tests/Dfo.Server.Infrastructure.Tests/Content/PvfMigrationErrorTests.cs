using Dfo.Pvf.Tests.Support;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfMigrationErrorTests
{
    [Theory]
    [InlineData("rank-type")]
    [InlineData("tutorial-type")]
    [InlineData("tutorial-tail")]
    [InlineData("skill-type")]
    [InlineData("skill-tail")]
    [InlineData("equipment-type")]
    [InlineData("equipment-tail")]
    [InlineData("quest-pair")]
    [InlineData("quest-penalty")]
    public async Task Valid_prefix_with_bad_value_or_partial_row_never_becomes_partial_business_data(string defect)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("character", "character.lst").Int(0).Quoted("test.chr").End();
        builder.AddScript("equipment", "equipment.lst").Int(100).Quoted("test.equ").End();
        builder.AddScript("equipment", "test.equ").Tag("[durability]").Int(10).End();
        if (defect.StartsWith("rank")) builder.AddScript("Etc", "RankSystemInfo.etc")
            .Tag("[rank grade]").Int(10).Quoted("twenty").End();
        if (defect.StartsWith("tutorial"))
        {
            var table = builder.AddScript("etc", "serverparameter.etc")
                .Tag("[escalade tutorial reward]").Int(38).Int(100).Int(1);
            if (defect.EndsWith("type")) table.Quoted("38"); else table.Int(38);
            table.End();
        }
        if (defect.StartsWith("skill"))
        {
            var character = builder.AddScript("character", "test.chr").Tag("[initial value]")
                .Tag("[skill]").Int(1).Int(1);
            if (defect.EndsWith("type")) character.Quoted("2"); else character.Int(2);
            character.Tag("[/skill]").Tag("[/initial value]").End();
        }
        if (defect.StartsWith("equipment"))
        {
            var character = builder.AddScript("character", "test.chr")
                .Tag("[create equipment list]").Quoted("[weapon]").Int(100).Quoted("[coat]");
            if (defect.EndsWith("type")) character.Quoted("100");
            character.Tag("[/create equipment list]").End();
        }
        if (defect.StartsWith("quest"))
        {
            var table = builder.AddScript("n_Quest", "questParameter.etc")
                .Tag("[exp reward table]").Int(100).Int(-1);
            if (defect == "quest-pair") table.Tag("[difficulty]").Quoted("M");
            else table.Tag("[green level penalty]").Quoted("80");
            table.End();
        }
        using var file = new BuiltArchive(builder);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            if (defect.StartsWith("rank")) _ = new PvfDungeonRankGradeSource(file.Provider).Load();
            else if (defect.StartsWith("tutorial")) _ = await new PvfTutorialRewardDefinitionSource(file.Provider).GetAsync(38, default);
            else if (defect.StartsWith("skill")) _ = await new PvfInitialSkillDefinitionSource(file.Provider).GetAsync(0, default);
            else if (defect.StartsWith("equipment")) _ = await new PvfInitialEquipmentDefinitionSource(file.Provider).GetAsync(0, default);
            else _ = new PvfQuestParameterSource(file.Provider).Load();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bead_rejects_mixed_string_data_instead_of_accepting_its_valid_target_prefix(bool corrupt)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("stackable", "stackable.lst").Int(100).Quoted("bead.stk").Int(200).Quoted("card.stk").End();
        builder.AddScript("equipment", "equipment.lst").Int(300).Quoted("target.equ").End();
        builder.AddScript("stackable", "bead.stk").Tag("[stackable type]").Quoted("[enchant waste]").Int(0)
            .Tag("[monster card id]").Int(200).End();
        var card = builder.AddScript("stackable", "card.stk").Tag("[item category]").Quoted("monster card")
            .Tag("[enchant index]").Int(0).Tag("[string data]").Quoted("target").Quoted("weapon");
        if (corrupt) card.Int(99);
        card.End();
        builder.AddScript("equipment", "target.equ").Tag("[equipment type]").Quoted("[weapon]").Int(1).End();
        using var file = new BuiltArchive(builder);
        var definition = await new PvfEquipmentEnchantContentSource(file.Provider).ResolveAsync(100, 300, default);
        Assert.Equal(!corrupt, definition is not null);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("string")]
    [InlineData("nan")]
    [InlineData("cube-extra")]
    public async Task System_disjoint_does_not_filter_corrupt_constants_or_cube_operands(string shape)
    {
        var builder = new PvfArchiveBuilder();
        var script = builder.AddScript("etc", "disjoint.etc").Tag("[cube index]").Quoted("no element").Int(3037);
        if (shape == "cube-extra") script.Quoted("ignored");
        script.Tag("[cube creation const]").Int(150).Single(1.2f);
        if (shape == "string") script.Quoted("ignored");
        if (shape == "nan") script.Single(float.NaN);
        script.Tag("[additional result]").Int(0).Tag("[additional result const]").Int(0)
            .Tag("[additional result expand]").Int(0).Int(0).Tag("[additional result expand const]").Int(0).Int(0).End();
        using var file = new BuiltArchive(builder);
        var source = new PvfEquipmentDisjointDefinitionSource(file.Provider);
        if (shape == "valid") Assert.NotNull(await source.GetSystemTableAsync(default));
        else await Assert.ThrowsAsync<InvalidDataException>(() => source.GetSystemTableAsync(default).AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repeated_root_drop_declarations_are_validated_before_using_the_first_value(bool corrupt)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("stackable", "stackable.lst").Int(100).Quoted("test.stk").End();
        var item = builder.AddScript("stackable", "test.stk").Tag("[grade]").Int(1).Tag("[rarity]").Int(0)
            .Tag("[creation rate]").Int(10).Tag("[grade]");
        if (corrupt) item.Quoted("one"); else item.Int(1);
        item.End();
        using var file = new BuiltArchive(builder);
        var source = new PvfDropItemPoolSource(file.Provider);
        if (corrupt) Assert.Throws<InvalidDataException>(() => source.Load(Dfo.Server.Abstractions.Content.DropItemFamily.Stackable));
        else Assert.NotNull(source.Load(Dfo.Server.Abstractions.Content.DropItemFamily.Stackable));
    }

    [Fact]
    public void Explicit_empty_monster_item_table_is_validated_as_an_empty_list()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("monster", "monster.lst").Int(1).Quoted("test.mob").End();
        builder.AddScript("monster", "test.mob").Tag("[item]").Tag("[/item]").End();
        builder.AddScript("equipment", "equipment.lst").Int(100).Quoted("test.equ").End();
        builder.AddScript("stackable", "stackable.lst").Int(200).Quoted("test.stk").End();
        using var file = new BuiltArchive(builder);
        Assert.Empty(new PvfMonsterDeclaredDropTableSource(file.Provider).Load());
    }

    [Theory]
    [InlineData("empty-prefix")]
    [InlineData("bad-prefix")]
    [InlineData("two-active")]
    public void Repeated_monster_tables_validate_every_declaration_before_selecting_one_active_table(string shape)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("monster", "monster.lst").Int(1).Quoted("test.mob").End();
        var monster = builder.AddScript("monster", "test.mob").Tag("[item]");
        if (shape == "bad-prefix") monster.Quoted("invalid");
        if (shape == "two-active") monster.Int(200).Int(100);
        monster.Tag("[/item]").Tag("[item]").Int(200).Int(100).Tag("[/item]").End();
        builder.AddScript("equipment", "equipment.lst").Int(100).Quoted("test.equ").End();
        builder.AddScript("equipment", "test.equ").Tag("[rarity]").Int(0).End();
        builder.AddScript("stackable", "stackable.lst").Int(200).Quoted("test.stk").End();
        builder.AddScript("stackable", "test.stk").Tag("[rarity]").Int(0).End();
        using var file = new BuiltArchive(builder);
        var source = new PvfMonsterDeclaredDropTableSource(file.Provider);
        if (shape == "empty-prefix") Assert.Single(Assert.Single(source.Load()).Entries);
        else Assert.Throws<InvalidDataException>(() => source.Load());
    }

    private sealed class BuiltArchive : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pvf-migration-errors-{Guid.NewGuid():N}.pvf");
        public PvfArchiveProvider Provider { get; }
        public BuiltArchive(PvfArchiveBuilder builder)
        {
            File.WriteAllBytes(path, builder.Build());
            Provider = new(new PvfContentOptions { ArchivePath = path });
        }
        public void Dispose() { Provider.Dispose(); File.Delete(path); }
    }
}
