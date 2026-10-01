using Dfo.Pvf.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfTypedProjectionTests
{
    [Fact]
    public async Task Character_stat_names_repeat_across_scopes_and_keep_initial_and_growth_values()
    {
        using var file = new BuiltArchive(CharacterArchive());
        var definition = Assert.Single(await new PvfCharacterStatDefinitionSource(file.Provider).GetAllAsync(default));
        Assert.Equal(180, definition.Initial.HpMax);
        Assert.Equal(45, definition.Growth[new CharacterCareer(0, 0)].HpMax);
        Assert.Equal(50, definition.Growth[new CharacterCareer(0, 0, 1)].HpMax);
    }

    [Theory]
    [InlineData("mixed")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("nonfinite")]
    public async Task Malformed_known_character_stat_cannot_become_a_missing_or_first_value(string defect)
    {
        using var file = new BuiltArchive(CharacterArchive(defect));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PvfCharacterStatDefinitionSource(file.Provider).GetAllAsync(default).AsTask());
        Assert.Contains("[HP MAX]", error.Message);
    }

    [Fact]
    public void Experience_repeated_tables_preserve_order_and_both_numeric_forms()
    {
        using var file = new BuiltArchive(ExperienceArchive());
        var rates = new PvfExperienceParameterSource(file.Provider).Load();
        Assert.Equal(new[] { 1.5, 2.0 }, rates.DifficultyRates);
        Assert.Equal(new[] { 1.0, 2.0 }, rates.PartyMemberRates);
        Assert.Equal(3.0, rates.MonsterOverallRate);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("scalar")]
    public void Wrong_tokens_in_known_experience_sections_are_rejected(string defect)
    {
        using var file = new BuiltArchive(ExperienceArchive(defect));
        var error = Assert.Throws<InvalidDataException>(() => new PvfExperienceParameterSource(file.Provider).Load());
        Assert.Contains(defect == "list" ? "[dungeon difficulty exp bonusrate]" : "[monster exp bonusrate]", error.Message);
    }

    [Fact]
    public async Task Inventory_sorting_projects_a_valid_mixed_type_and_optional_numbers()
    {
        using var file = new BuiltArchive(InventoryArchive());
        var keys = await new PvfInventorySortKeySource(file.Provider, new NoSlot()).GetAsync(7, default);
        Assert.Equal(2, keys.Rarity);
        Assert.Equal(10, keys.Level);
        Assert.Equal("[waste]", keys.StackableType);
        Assert.Equal(int.MaxValue, keys.Part);
    }

    [Theory]
    [InlineData("mixed")]
    [InlineData("duplicate")]
    [InlineData("type")]
    public async Task Invalid_sort_metadata_does_not_fall_back_to_zero_or_empty_type(string defect)
    {
        using var file = new BuiltArchive(InventoryArchive(defect));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PvfInventorySortKeySource(file.Provider, new NoSlot()).GetAsync(7, default).AsTask());
        Assert.Contains(defect == "type" ? "[stackable type]" : "[rarity]", error.Message);
    }

    [Theory]
    [InlineData("negative")]
    [InlineData("overflow")]
    [InlineData("mixed")]
    [InlineData("duplicate")]
    public void Invalid_durability_is_rejected_instead_of_clamped_or_skipped(string defect)
    {
        var builder = new PvfArchiveBuilder();
        var script = builder.AddScript("equipment", "test.equ").Tag("[durability]");
        script.Int(defect == "negative" ? -1 : defect == "overflow" ? 65536 : 35);
        if (defect == "mixed") script.Quoted("bad");
        if (defect == "duplicate") script.Tag("[durability]").Int(40);
        script.End();
        using var file = new BuiltArchive(builder);
        var error = Assert.Throws<InvalidDataException>(() =>
            PvfEquipmentDurability.Read(file.Provider.Archive, new Dictionary<int, string> { [7] = "test.equ" }, 7));
        Assert.Contains("[durability]", error.Message);
    }

    [Fact]
    public void Missing_durability_remains_zero_and_valid_value_is_preserved()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("equipment", "missing.equ").Tag("[name]").Quoted("missing").End();
        builder.AddScript("equipment", "valid.equ").Tag("[durability]").Int(35).End();
        using var file = new BuiltArchive(builder);
        var paths = new Dictionary<int, string> { [7] = "missing.equ", [8] = "valid.equ" };
        Assert.Equal((ushort)0, PvfEquipmentDurability.Read(file.Provider.Archive, paths, 7));
        Assert.Equal((ushort)35, PvfEquipmentDurability.Read(file.Provider.Archive, paths, 8));
    }

    [Fact]
    public void Repeated_honor_grade_blocks_keep_header_and_level_experience_projection()
    {
        using var file = new BuiltArchive(DungeonArchive());
        var policy = new PvfDungeonExperienceProgressionPolicySource(file.Provider).Load();
        Assert.Equal(85, policy.LevelThresholds.Count);
        Assert.Equal((ulong)600, policy.MaximumHonorExperience);
        Assert.Equal((uint)900, policy.GrowthCapsuleMaximum);
        Assert.Equal(25, policy.GrowthCapsuleRatePercent);
    }

    [Theory]
    [InlineData("grade")]
    [InlineData("capsule")]
    [InlineData("duplicate")]
    [InlineData("maximum")]
    public void Invalid_known_progression_fields_are_rejected_before_projection(string defect)
    {
        using var file = new BuiltArchive(DungeonArchive(defect));
        var error = Assert.Throws<InvalidDataException>(() => new PvfDungeonExperienceProgressionPolicySource(file.Provider).Load());
        Assert.Contains(defect switch
        {
            "grade" => "[grade]",
            "maximum" => "[maxexp on maxlevel]",
            _ => "[max gage exp]",
        }, error.Message);
    }

    private static PvfArchiveBuilder CharacterArchive(string? defect = null)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("character", "character.lst").Int(0).Quoted("test.chr").End();
        var script = builder.AddScript("character", "test.chr").Tag("[initial value]").Tag("[HP MAX]");
        if (defect != "empty") script.Single(defect == "nonfinite" ? float.NaN : 180);
        if (defect == "mixed") script.Quoted("bad");
        if (defect == "duplicate") script.Tag("[HP MAX]").Int(190);
        script.Tag("[growtype 1]").Tag("[HP MAX]").Int(45)
            .Tag("[awakening 1]").Tag("[HP MAX]").Int(50).End();
        return builder;
    }

    private static PvfArchiveBuilder ExperienceArchive(string? defect = null)
    {
        var builder = new PvfArchiveBuilder();
        var script = builder.AddScript("etc", "serverparameter.etc")
            .Tag("[dungeon difficulty exp bonusrate]").Single(1.5f);
        if (defect == "list") script.Quoted("bad");
        script.Tag("[dungeon difficulty exp bonusrate]").Int(2)
            .Tag("[party user number exp bonusrate]").Int(1).Int(2)
            .Tag("[monster exp bonusrate]").Int(3);
        if (defect == "scalar") script.Quoted("bad");
        script.Tag("[unknown]").Quoted("ignored").End();
        return builder;
    }

    private static PvfArchiveBuilder InventoryArchive(string? defect = null)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("equipment", "equipment.lst").Int(8).Quoted("unused.equ").End();
        builder.AddScript("stackable", "stackable.lst").Int(7).Quoted("test.stk").End();
        var script = builder.AddScript("stackable", "test.stk").Tag("[rarity]").Int(2);
        if (defect == "mixed") script.Quoted("bad");
        if (defect == "duplicate") script.Tag("[rarity]").Int(3);
        script.Tag("[minimum level]").Int(10).Tag("[stackable type]");
        if (defect == "type") script.Int(99);
        script.Quoted("[waste]").Int(1).End();
        return builder;
    }

    private static PvfArchiveBuilder DungeonArchive(string? defect = null)
    {
        var builder = new PvfArchiveBuilder();
        var thresholds = builder.AddScript("character", "ExpTable.tbl");
        for (var index = 0; index < 85; index++) thresholds.Int(index);
        thresholds.End();
        var honor = builder.AddScript("etc", "honorlevel.etc").Tag("[grade]").Int(1)
            .Quoted("animation").Quoted("medal").Int(0).Quoted("icon").Int(0).Int(1).Int(100);
        if (defect == "grade") honor.Quoted("bad");
        honor.Tag("[/grade]").Tag("[grade]").Int(2).Quoted("animation").Quoted("medal")
            .Int(1).Quoted("icon").Int(1).Int(2).Int(200).Tag("[/grade]")
            .Tag("[maxexp on maxlevel]").Int(300);
        if (defect == "maximum") honor.Quoted("bad");
        honor.End();
        var capsule = builder.AddScript("etc", "expandexpgage.etc").Tag("[max gage exp]").Int(900);
        if (defect == "capsule") capsule.Quoted("bad");
        if (defect == "duplicate") capsule.Tag("[max gage exp]").Int(901);
        capsule.Tag("[exp gain rate]").Int(25).End();
        return builder;
    }

    private sealed class NoSlot : IEquipmentSlotDefinitionSource
    {
        public ValueTask<BusinessSlot?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<BusinessSlot?>(null);
    }

    private sealed class BuiltArchive : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pvf-projection-{Guid.NewGuid():N}.pvf");
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
