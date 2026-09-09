using Dfo.Server.Data.Content;
using Dfo.Server.Infrastructure.Content;
using Dfo.Pvf.Tokens;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfCeraShopDefinitionSourceTests
{
    [SkippableFact]
    public async Task Knight_coat_shared_ability_case_preserves_sparse_option_ids()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var item = await new PvfCeraShopDefinitionSource(provider).GetItemAsync(413500013, default);
        Assert.NotNull(item?.Avatar);
        Assert.Empty(item.Diagnostics);
        Assert.Contains(2, item.Avatar.SelectableAbilities);
        Assert.Contains(40, item.Avatar.SelectableAbilities);
        Assert.DoesNotContain(4, item.Avatar.SelectableAbilities);
        Assert.DoesNotContain(39, item.Avatar.SelectableAbilities);
    }

    [Fact]
    public void Shared_avatar_case_requires_one_closed_definition_and_complete_options()
    {
        var option = new PvfToken[] { new(PvfTokenKind.Int32, 2), new(PvfTokenKind.QuotedString, 1),
            new(PvfTokenKind.QuotedString, 2), new(PvfTokenKind.Int32, 41), new(PvfTokenKind.Int32, 1) };
        string Text(int id) => id == 1 ? "[SKILL_LEVEL]" : "[knight]";
        var section = new PvfNpcShopDefinitionSource.Section("[ability case]", [new(PvfTokenKind.Int32, 1250), .. option]);
        var end = new PvfNpcShopDefinitionSource.Section("[/ability case]", []);
        var valid = PvfCeraShopDefinitionSource.ResolveAvatarAbilityCase(1250, [section, end], Text);
        Assert.Equal(new[] { 2 }, valid.Abilities);
        Assert.Empty(valid.Diagnostics);
        foreach (var sections in new PvfNpcShopDefinitionSource.Section[][] { [], [section], [section, end, section, end] })
        {
            var invalid = PvfCeraShopDefinitionSource.ResolveAvatarAbilityCase(1250, sections, Text);
            Assert.Empty(invalid.Abilities);
            Assert.NotEmpty(invalid.Diagnostics);
        }
        Assert.NotEmpty(PvfCeraShopDefinitionSource.ResolveAvatarAbilityCase(999, [section, end], Text).Diagnostics);
        foreach (var malformed in new PvfToken[][] { [], option[..4], [.. option, .. option], [.. option, new(PvfTokenKind.Int32, 3)] })
        {
            var invalid = PvfCeraShopDefinitionSource.ParseAvatarAbilities(malformed, Text, "test");
            Assert.Empty(invalid.Abilities);
            Assert.NotEmpty(invalid.Diagnostics);
        }
    }

    [SkippableFact]
    public async Task River_lethe_product_costs_3600_and_delivers_one_item_three()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCeraShopDefinitionSource(provider);
        var product = await source.GetProductAsync(110000, default);
        Assert.NotNull(product);
        Assert.Equal((3, 1, 3600), (product.ItemId, product.Count, product.Price?.Cera));
        Assert.Empty(product.Diagnostics);
        Assert.False(product.IsService);
        var item = await source.GetItemAsync(3, default);
        Assert.NotNull(item);
        Assert.Equal(CeraShopItemKind.Stackable, item.Kind);
        Assert.Empty(item.Diagnostics);
    }

    [SkippableFact]
    public async Task Avatar_tier_resolves_negative_price_and_declares_valid_ability_choices()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCeraShopDefinitionSource(provider);
        var permanent = await source.GetProductAsync(120002, default);
        Assert.NotNull(permanent);
        Assert.Equal((39032, 1, 1980, 0), (permanent.ItemId, permanent.Count, permanent.Price?.Cera, permanent.Price?.Period));
        Assert.Empty(permanent.Diagnostics);
        var item = await source.GetItemAsync(39032, default);
        Assert.NotNull(item?.Avatar);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, item.Avatar.SelectableAbilities);
        Assert.Equal(4, item.Avatar.PriceOptions.Length);
        Assert.Empty(item.Diagnostics);
        var temporary = await source.GetProductAsync(120000, default);
        Assert.NotNull(temporary);
        Assert.Equal((300, 7), (temporary.Price?.Cera, temporary.Price?.Period));
        Assert.Empty(temporary.Diagnostics);
    }

    [SkippableFact]
    public async Task Egg_keeps_its_template_and_reads_species_start_level_and_artifact_is_pet_equipment()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCeraShopDefinitionSource(provider);
        var product = await source.GetProductAsync(105016, default);
        Assert.NotNull(product);
        Assert.Equal((63022, 1, 19800), (product.ItemId, product.Count, product.Price?.Cera));
        var egg = await source.GetItemAsync(product.ItemId, default);
        Assert.NotNull(egg);
        Assert.Equal(CeraShopItemKind.Creature, egg.Kind);
        Assert.Equal(new CeraShopCreatureDeclaration(20, 1), egg.Creature);
        Assert.Empty(egg.Diagnostics);
        var artifact = await source.GetProductAsync(101075, default);
        Assert.NotNull(artifact);
        Assert.Equal(2747175, artifact.ItemId);
        Assert.Equal(CeraShopItemKind.CreatureEquipment, (await source.GetItemAsync(artifact.ItemId, default))?.Kind);
    }

    [SkippableFact]
    public async Task Package_zero_catalog_count_delivers_components_and_purchase_level_is_separate_from_use_level()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCeraShopDefinitionSource(provider);
        var product = await source.GetProductAsync(102425, default);
        Assert.NotNull(product);
        Assert.Equal((2683477, 1, 5300), (product.ItemId, product.Count, product.Price?.Cera));
        Assert.Empty(product.Diagnostics);
        var package = await source.GetItemAsync(product.ItemId, default);
        Assert.NotNull(package);
        Assert.Equal(CeraShopItemKind.Package, package.Kind);
        Assert.Equal(50, package.MinimumPurchaseLevel);
        Assert.Equal(new[] { new CeraShopComponent(2683470, 1, 0), new CeraShopComponent(2683473, 1, 0) }, package.Components);
        Assert.Empty(package.Diagnostics);
        var selection = await source.GetItemAsync(2682779, default);
        Assert.NotNull(selection);
        Assert.Equal(9, selection.Components.Length);
        Assert.Equal(new[] { new CeraShopComponent(2682768, 50, 0), new CeraShopComponent(2682769, 50, 0) }, Assert.Single(selection.SelectionGroups).Choices);
        Assert.Empty(selection.Diagnostics);
    }

    [SkippableTheory]
    [InlineData(3, CeraShopItemKind.Stackable)]
    [InlineData(35, CeraShopItemKind.Service)]
    [InlineData(33, CeraShopItemKind.Service)]
    [InlineData(821, CeraShopItemKind.Service)]
    [InlineData(2681921, CeraShopItemKind.Service)]
    [InlineData(50, CeraShopItemKind.Service)]
    [InlineData(2682205, CeraShopItemKind.Service)]
    [InlineData(2682006, CeraShopItemKind.Service)]
    public async Task Service_declarations_distinguish_contracts_and_upgrades_from_physical_etc_items(int id, CeraShopItemKind expected)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        Assert.Equal(expected, (await new PvfCeraShopDefinitionSource(provider).GetItemAsync(id, default))?.Kind);
    }

    [Fact]
    public void Typed_catalog_rejects_string_in_price_and_unknown_nonzero_payment()
    {
        var row = Numbers(110000, 3, 1, 0, 0, 3600, 0, 0, 0);
        row[6] = new(PvfTokenKind.QuotedString, 123);
        Assert.Empty(PvfCeraShopDefinitionSource.ParseProduct("[item]", row).Diagnostics);
        row[4] = new(PvfTokenKind.Int32, 9);
        Assert.Contains(PvfCeraShopDefinitionSource.ParseProduct("[item]", row).Diagnostics, x => x.Code == "UnsupportedPayment");
        row[5] = new(PvfTokenKind.QuotedString, 3600);
        Assert.Contains(PvfCeraShopDefinitionSource.ParseProduct("[item]", row).Diagnostics, x => x.Code == "MalformedCatalog");
    }

    [SkippableFact]
    public async Task Mixed_package_exposes_service_components_and_unimplemented_purchase_restrictions_are_explicit()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfCeraShopDefinitionSource(provider);
        var mixed = await source.GetItemAsync(490700700, default);
        Assert.NotNull(mixed);
        Assert.Contains(mixed.Components, x => x.ItemId == 33);
        Assert.Equal(CeraShopItemKind.Service, (await source.GetItemAsync(33, default))?.Kind);
        Assert.Equal(CeraShopItemKind.Stackable, (await source.GetItemAsync(2683470, default))?.Kind);
        var restricted = await source.GetProductAsync(100494, default);
        Assert.NotNull(restricted);
        Assert.Contains(restricted.Diagnostics, x => x.Code == "PurchaseRestriction");
        Assert.Null(await source.GetProductAsync(int.MaxValue, default));
        Assert.Null(await source.GetItemAsync(int.MaxValue, default));
    }

    [Fact]
    public void Unknown_avatar_tier_is_never_a_free_product()
    {
        var result = PvfCeraShopDefinitionSource.ParseProduct("[avatar]", Numbers(120002, 39032, 3, 0, 0, -1));
        Assert.Null(result.Price);
        Assert.Contains(result.Diagnostics, x => x.Code == "UnknownAvatarPrice");
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, false)]
    [InlineData(2, 0, true)]
    [InlineData(0, 1, true)]
    public void Negative_avatar_catalog_flags_accept_only_observed_values(int flag, int other, bool rejected)
    {
        var result = PvfCeraShopDefinitionSource.ParseProduct("[avatar]", Numbers(630019, 413550012, 2, flag, other, -1));
        Assert.Equal(rejected, result.Diagnostics.Any(x => x.Code == "UnsupportedAvatarCatalogFlags"));
        Assert.Null(result.Price);
        Assert.Contains(result.Diagnostics, x => x.Code == "UnknownAvatarPrice");
    }

    [Fact]
    public void Incomplete_or_noninteger_package_components_cannot_silently_drop_rewards()
    {
        Assert.Contains(PvfCeraShopDefinitionSource.ParseComponents(Numbers(3, 1, 0, 4), 3, "synthetic").Diagnostics, x => x.Code == "MalformedPackage");
        var row = Numbers(3, 1);
        row[1] = new(PvfTokenKind.Single, 1);
        Assert.Contains(PvfCeraShopDefinitionSource.ParseComponents(row, 2, "synthetic").Diagnostics, x => x.Code == "MalformedPackage");
        var timed = PvfCeraShopDefinitionSource.ParseComponents(Numbers(3, 1, 7), 3, "synthetic");
        Assert.Empty(timed.Diagnostics);
        Assert.Equal(7, Assert.Single(timed.Components).UsablePeriod);
    }

    private static PvfToken[] Numbers(params int[] values) => values.Select(x => new PvfToken(PvfTokenKind.Int32, x)).ToArray();

    [Fact]
    public void Nondefault_instance_expiry_or_trade_declarations_are_visible_refusals()
    {
        var errors = PvfCeraShopDefinitionSource.ParseInstanceDeclarations(
            [new("[expiration date]", [new(PvfTokenKind.QuotedString, 99)]),
             new("[usable period]", [new(PvfTokenKind.Int32, 7)]),
             new("[trade limit]", [new(PvfTokenKind.Int32, 1)])], "synthetic");
        Assert.Equal(3, errors.Length);
        Assert.All(errors, x => Assert.Equal("UnsupportedInstanceDeclaration", x.Code));
        Assert.Empty(PvfCeraShopDefinitionSource.ParseInstanceDeclarations(
            [new("[usable period]", [new(PvfTokenKind.Int32, 0)])], "synthetic"));
    }
}
