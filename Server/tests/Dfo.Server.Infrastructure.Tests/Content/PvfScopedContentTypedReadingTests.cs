using Dfo.Pvf.Tests.Support;
using Dfo.Pvf.Tokens;
using Dfo.Server.Infrastructure.Content;
using Section = Dfo.Server.Infrastructure.Content.PvfNpcShopDefinitionSource.Section;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfScopedContentTypedReadingTests
{
    [Theory]
    [InlineData("expiration-kind")]
    [InlineData("expiration-extra")]
    [InlineData("expiration-date")]
    [InlineData("period-kind")]
    [InlineData("period-negative")]
    [InlineData("later-expiration")]
    public async Task Lifetime_rejects_invalid_present_metadata_even_after_a_valid_first_occurrence(string defect)
    {
        using var file = new BuiltArchive(Lifetime(defect));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new PvfCreatureEquipmentLifetimeSource(file.Provider)
            .GetAsync(7, DateTimeOffset.FromUnixTimeSeconds(1700000000), default).AsTask());
        Assert.Contains(defect.StartsWith("period", StringComparison.Ordinal) ? "[usable period]" : "[expiration date]", error.Message);
    }

    [Fact]
    public async Task Lifetime_integer_string_variants_keep_direct_and_policy_values()
    {
        using var file = new BuiltArchive(Lifetime());
        var value = await new PvfCreatureEquipmentLifetimeSource(file.Provider)
            .GetAsync(7, DateTimeOffset.FromUnixTimeSeconds(1700000000), default);
        Assert.Equal(0u, value.DirectRemainingSeconds);
        Assert.Equal(259200u, value.DetailFallbackRemainingSeconds);
    }

    [Theory]
    [InlineData("attach-kind")]
    [InlineData("attach-duplicate")]
    [InlineData("limit-kind")]
    [InlineData("limit-extra")]
    public async Task Mail_invalid_attach_or_limit_cannot_become_unknown_or_unlimited(string defect)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("equipment", "equipment.lst").Int(8).Quoted("unused.equ").End();
        builder.AddScript("stackable", "stackable.lst").Int(7).Quoted("test.stk").End();
        var script = builder.AddScript("stackable", "test.stk").Tag("[attach type]");
        if (defect == "attach-kind") script.Int(1); else script.Quoted("[free]");
        if (defect == "attach-duplicate") script.Tag("[attach type]").Quoted("[trade]");
        script.Tag("[trade limit max]");
        if (defect == "limit-kind") script.Quoted("bad"); else script.Int(5);
        if (defect == "limit-extra") script.Int(6);
        script.End();
        using var file = new BuiltArchive(builder);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new PvfMailItemDefinitionSource(file.Provider).GetAsync(7, default).AsTask());
        Assert.Contains(defect.StartsWith("attach", StringComparison.Ordinal) ? "[attach type]" : "[trade limit max]", error.Message);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("over")]
    public void Expert_contract_invalid_scalars_are_rejected(string defect)
    {
        var builder = new PvfArchiveBuilder();
        var script = builder.AddScript("etc", "premiumlist_new.etc").Tag("[type]");
        if (defect == "type") script.Quoted("27"); else script.Int(27);
        script.Tag("[over skill]").Int(5);
        if (defect == "over") script.Quoted("bad");
        script.End();
        using var file = new BuiltArchive(builder);
        var error = Assert.Throws<InvalidDataException>(() => new PvfExpertSkillContractDefinitionSource(file.Provider).GetOverSkillPremiumTypes());
        Assert.Contains(defect == "type" ? "[type]" : "[over skill]", error.Message);
    }

    [Fact]
    public void Expert_contract_repeated_type_blocks_keep_their_own_over_skill_state()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("etc", "premiumlist_new.etc").Tag("[type]").Int(27).Tag("[over skill]").Int(5)
            .Tag("[type]").Int(56).Tag("[unknown]").Int(10).Tag("[type]").Int(28).Tag("[over skill]").Int(1).End();
        using var file = new BuiltArchive(builder);
        Assert.Equal(new[] { 27, 28 }, new PvfExpertSkillContractDefinitionSource(file.Provider).GetOverSkillPremiumTypes().Order().ToArray());
    }

    [Theory]
    [InlineData("index")]
    [InlineData("date-kind")]
    [InlineData("date-value")]
    [InlineData("extend-kind")]
    [InlineData("extend-tail")]
    [InlineData("positions-kind")]
    [InlineData("positions-tail")]
    [InlineData("unclosed")]
    public async Task Collection_invalid_dates_types_or_incomplete_sequences_are_rejected(string defect)
    {
        using var file = new BuiltArchive(Collection(defect));
        await Assert.ThrowsAsync<InvalidDataException>(() => new PvfCollectionBoxDefinitionSource(file.Provider).GetAllAsync(default).AsTask());
    }

    [Fact]
    public async Task Collection_closed_blocks_keep_repeated_fields_local_and_complete_grids()
    {
        using var file = new BuiltArchive(Collection());
        var boxes = await new PvfCollectionBoxDefinitionSource(file.Provider).GetAllAsync(default);
        Assert.Equal(new[] { 1, 2 }, boxes.Select(box => box.BoxIndex));
        Assert.All(boxes, box => Assert.Single(box.Slots));
        Assert.All(boxes, box => Assert.Equal(2020, box.BaseExpiresAt!.Value.Year));
    }

    [Theory]
    [InlineData("group")]
    [InlineData("level")]
    [InlineData("allowance-kind")]
    [InlineData("allowance-tail")]
    [InlineData("slot")]
    [InlineData("reward-kind")]
    [InlineData("reward-tail")]
    [InlineData("unclosed")]
    public async Task Daily_wrong_types_or_truncated_tables_are_rejected(string defect)
    {
        using var file = new BuiltArchive(Daily(defect));
        await Assert.ThrowsAsync<InvalidDataException>(() => new PvfDailyActivityDefinitionSource(file.Provider).GetAllAsync(default).AsTask());
    }

    [Fact]
    public async Task Daily_repeated_groups_and_slots_keep_ids_and_rewards_local()
    {
        using var file = new BuiltArchive(Daily());
        var groups = await new PvfDailyActivityDefinitionSource(file.Provider).GetAllAsync(default);
        Assert.Equal(new[] { 1, 2 }, groups.Select(group => group.GroupId));
        Assert.All(groups, group => Assert.Equal(new[] { 1, 2 }, group.Slots.Select(slot => slot.SlotIndex)));
        Assert.All(groups, group => Assert.Single(group.Rewards));
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("duplicate")]
    [InlineData("extra")]
    public void Npc_wrong_or_repeated_attach_declaration_refuses_the_item(string defect)
    {
        var builder = new PvfArchiveBuilder();
        var offset = builder.InternUtf8("[sealing]");
        builder.AddScript("test", "test.etc").Tag("[dummy]").End();
        using var file = new BuiltArchive(builder);
        var values = new List<PvfToken> { defect == "kind" ? I(1) : new(PvfTokenKind.QuotedString, offset) };
        if (defect == "extra") values.Add(I(2));
        var sections = new List<Section> { new("[price]", [I(100)]), new("[attach type]", values) };
        if (defect == "duplicate") sections.Add(new("[attach type]", [new(PvfTokenKind.QuotedString, offset)]));
        Assert.False(PvfNpcShopDefinitionSource.ParseItem(file.Provider.Archive, 7, sections).IsReadable);
    }

    [Theory]
    [InlineData("[buy only cera]", false)]
    [InlineData("[buy restrict item]", false)]
    [InlineData("[buy restrict item]", true)]
    [InlineData("[upgrade info]", false)]
    [InlineData("[upgrade info]", true)]
    public async Task Cera_classification_refuses_bad_tokens_or_truncated_rows(string field, bool truncated)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("equipment", "equipment.lst").Int(8).Quoted("unused.equ").End();
        builder.AddScript("stackable", "stackable.lst").Int(7).Quoted("test.stk").End();
        builder.AddScript("creature", "creature.lst").Int(1).Quoted("test.cre").End();
        builder.AddScript("etc", "premiumlist_new.etc").Tag("[type]").Int(1).End();
        var catalog = builder.AddScript("etc", "cerashop.etc").Tag(field == "[upgrade info]" ? "[buy only cera]" : field).Int(7);
        if (field != "[upgrade info]" && !truncated) catalog.Quoted("bad");
        catalog.End();
        var account = builder.AddScript("etc", "accountcargo.etc").Tag("[upgrade info]").Int(1);
        if (field != "[upgrade info]") account.Int(2).Int(3).Int(4).Int(5).Int(6);
        else if (!truncated) account.Quoted("bad");
        account.End();
        using var file = new BuiltArchive(builder);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new PvfCeraShopDefinitionSource(file.Provider).GetProductAsync(7, default).AsTask());
        Assert.Contains(field, error.Message);
    }

    [Theory]
    [InlineData("experience")]
    [InlineData("boss")]
    [InlineData("contract")]
    public void Premium_type_kind_is_validated_even_after_the_selected_profile(string method)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("etc", "premiumlist_new.etc").Tag("[type]").Int(84).Tag("[bonus exp]").Int(20)
            .Tag("[independent drop rate]").Int(1).Int(2).Int(3).Int(4)
            .Tag("[type]").Int(27).Tag("[over skill]").Int(5).Tag("[type]").Int(22)
            .Tag("[over equipinfo]").Int(1).Int(0).Int(10).Tag("[type]").Quoted("84").End();
        using var file = new BuiltArchive(builder);
        var source = new PvfPremiumServiceDefinitionSource(file.Provider);
        var error = Assert.Throws<InvalidDataException>(() =>
        {
            if (method == "experience") source.ReadGrowthExperienceBonusRate();
            else if (method == "boss") source.ReadGrowthBossEquipmentBonusPercents();
            else source.ReadContractLevels();
        });
        Assert.Contains("[type]", error.Message);
    }

    [Theory]
    [InlineData("type-kind")]
    [InlineData("term-kind")]
    [InlineData("term-duplicate")]
    [InlineData("unit-duplicate")]
    public void Premium_duration_parser_rejects_bad_and_duplicate_local_scalars(string defect)
    {
        var sections = new List<Section> { new("[type]", [defect == "type-kind" ? new(PvfTokenKind.QuotedString, 1) : I(27)]),
            new("[item]", [I(7)]), new("[term]", [defect == "term-kind" ? new(PvfTokenKind.Single, 1) : I(3)]) };
        if (defect == "term-duplicate") sections.Add(new("[term]", [I(4)]));
        if (defect == "unit-duplicate") { sections.Add(new("[is term unit minute]", [I(1)])); sections.Add(new("[is term unit minute]", [I(0)])); }
        sections.Add(new("[/item]", []));
        Assert.Throws<InvalidDataException>(() => PvfPackageOpeningDefinitionSource.ParsePremiums(sections));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("cost")]
    public void Package_duplicate_type_or_cost_cannot_produce_a_clean_lottery_definition(string duplicate)
    {
        var sections = new List<Section> { new("[stackable type]", [new(PvfTokenKind.QuotedString, 1), I(0)]),
            new("[lottery use cost]", [I(100)]), new("[int data]", [I(8), I(1000), I(1)]) };
        sections.Add(duplicate == "type" ? new("[stackable type]", [new(PvfTokenKind.QuotedString, 1)]) : new("[lottery use cost]", [I(200)]));
        var definition = PvfPackageOpeningDefinitionSource.Parse(7, sections, _ => "[upgradable legacy]");
        Assert.Contains(definition.Diagnostics, message => message.Contains("Duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void Southern_coin_pot_retains_the_declared_key_cost_without_an_archive()
    {
        Section[] sections = [new("[stackable type]", [new(PvfTokenKind.QuotedString, 1), I(0)]),
            new("[int data]", [I(0), I(1000), I(100000)]), new("[need material]", [I(2680739), I(2)])];
        var definition = PvfPackageOpeningDefinitionSource.Parse(7615, sections, _ => "[upgradable legacy]");
        Assert.Empty(definition.Diagnostics);
        Assert.Equal(2680739, definition.MaterialItemId);
        Assert.Equal(2, definition.MaterialCount);
    }

    [Fact]
    public void Gold_silver_horn_pot_does_not_charge_the_purchase_recipe_again_at_opening()
    {
        Section[] sections = [new("[stackable type]", [new(PvfTokenKind.QuotedString, 1), I(0)]),
            new("[int data]", [I(8), I(1000), I(1)]), new("[need material]", [I(490001570), I(10)])];
        var definition = PvfPackageOpeningDefinitionSource.Parse(490001561, sections, _ => "[upgradable legacy]");
        Assert.Empty(definition.Diagnostics);
        Assert.Equal(0, definition.MaterialCount);
    }

    [Theory]
    [InlineData("missing-count")]
    [InlineData("negative-count")]
    [InlineData("zero-count")]
    [InlineData("zero-item")]
    [InlineData("noninteger")]
    [InlineData("multiple-materials")]
    [InlineData("duplicate")]
    public void Lottery_material_declarations_cannot_silently_drop_invalid_costs(string defect)
    {
        var values = defect switch
        {
            "missing-count" => new[] { I(2680739) },
            "negative-count" => [I(2680739), I(-2)],
            "zero-count" => [I(2680739), I(0)],
            "zero-item" => [I(0), I(2)],
            "noninteger" => [I(2680739), new(PvfTokenKind.QuotedString, 2)],
            "multiple-materials" => [I(2680739), I(2), I(201), I(1)],
            _ => [I(2680739), I(2)],
        };
        var sections = new List<Section> { new("[stackable type]", [new(PvfTokenKind.QuotedString, 1), I(0)]),
            new("[int data]", [I(0), I(1000), I(100000)]), new("[need material]", values.ToList()) };
        if (defect == "duplicate") sections.Add(new("[need material]", [I(2680739), I(2)]));
        var definition = PvfPackageOpeningDefinitionSource.Parse(7615, sections, _ => "[upgradable legacy]");
        Assert.NotEmpty(definition.Diagnostics);
    }

    [Theory]
    [InlineData(10007316, 101600023, 100000, 1, 3, 0, true)]
    [InlineData(10007317, 101600023, 100000, 1, 3, 0, false)]
    [InlineData(10007316, 101600024, 100000, 1, 3, 0, false)]
    [InlineData(10007316, 101600023, 100000, 2, 3, 0, false)]
    [InlineData(10007316, 101600023, 100000, 1, 2, 0, false)]
    [InlineData(10007316, 101600023, 100000, 1, 0, 0, false)]
    [InlineData(10007316, 101600023, 99999, 1, 3, 0, false)]
    [InlineData(10007316, 101600023, 100000, 1, 3, 1, false)]
    public void Dark_repulsor_profile_is_bound_only_when_the_whole_observed_declaration_matches(
        int box, int weapon, int weight, int count, int extension, int tail, bool supported)
    {
        Section[] sections = [new("[stackable type]", [new(PvfTokenKind.QuotedString, 1), I(0)]),
            new("[booster info]", []), new("[avatar]", [I(1), I(weapon), I(weight), I(count), I(extension), I(tail)]),
            new("[/avatar]", []), new("[/booster info]", [])];
        var definition = PvfPackageOpeningDefinitionSource.Parse(box, sections, _ => "[booster]");
        Assert.Equal(supported, definition.Diagnostics.IsEmpty);
        Assert.Equal(supported ? 1 : 0, Assert.Single(Assert.Single(definition.Pools).Choices).Reward.TradeRestriction);
    }

    private static PvfToken I(int value) => new(PvfTokenKind.Int32, value);

    private static PvfArchiveBuilder Lifetime(string? defect = null)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("equipment", "equipment.lst").Int(7).Quoted("test.equ").End();
        var script = builder.AddScript("equipment", "test.equ").Tag("[expiration date]");
        if (defect == "expiration-kind") script.Single(1);
        else if (defect == "expiration-date") script.Quoted("not a date");
        else script.Int(0);
        if (defect == "expiration-extra") script.Int(1);
        if (defect == "later-expiration") script.Tag("[expiration date]").Quoted("not a date");
        script.Tag("[usable period]");
        if (defect == "period-kind") script.Single(3);
        else if (defect == "period-negative") script.Int(-1);
        else script.Quoted("3");
        script.End();
        return builder;
    }

    private static PvfArchiveBuilder Collection(string? defect = null)
    {
        var builder = new PvfArchiveBuilder();
        var script = builder.AddScript("localization/collectbox", "collectboxinfo.etc");
        for (var id = 1; id <= 2; id++)
        {
            script.Tag("[Collect Box Info]").Tag("[Index]");
            if (defect == "index") script.Quoted("bad"); else script.Int(id);
            script.Tag("[base expiration date]");
            if (defect == "date-kind") script.Int(20200101); else script.Quoted(defect == "date-value" ? "not a date" : "2020-01-01 00:00:00");
            script.Tag("[max expiration date]").Quoted("2021-01-01 00:00:00").Tag("[Extend Item]").Int(7);
            if (defect == "extend-kind") script.Quoted("bad"); else if (defect != "extend-tail") script.Int(1);
            script.Tag("[Item Position Info]").Int(1).Int(1).Int(8);
            if (defect == "positions-kind") script.Quoted("bad"); else if (defect != "positions-tail") script.Int(1);
            if (defect != "unclosed") script.Tag("[/Collect Box Info]");
            else break;
        }
        script.End();
        return builder;
    }

    private static PvfArchiveBuilder Daily(string? defect = null)
    {
        var builder = new PvfArchiveBuilder();
        var script = builder.AddScript("etc", "dailychallengetable.etc");
        for (var id = 1; id <= 2; id++)
        {
            script.Tag("[group]");
            if (defect == "group") script.Quoted("bad"); else script.Int(id);
            script.Tag("[level]").Int(1);
            if (defect != "level") script.Int(100);
            script.Tag("[slot num table]").Int(1).Int(100);
            if (defect == "allowance-kind") script.Quoted("bad"); else if (defect != "allowance-tail") script.Int(2);
            script.Tag("[/slot num table]").Tag("[slot]").Int(1);
            if (defect == "slot") script.Quoted("bad"); else script.Int(111);
            script.Tag("[/slot]").Tag("[slot]").Int(2).Int(222).Tag("[/slot]")
                .Tag("[reward table]").Int(1).Int(100).Int(8);
            if (defect == "reward-kind") script.Quoted("bad"); else if (defect != "reward-tail") script.Int(1);
            if (defect != "unclosed") script.Tag("[/group]"); else break;
        }
        script.End();
        return builder;
    }

    private sealed class BuiltArchive : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pvf-scoped-typed-{Guid.NewGuid():N}.pvf");
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
