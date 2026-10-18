using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfEntryLifecycleContentSourceTests
{
    [SkippableTheory]
    [InlineData(2600026)] // Fatigue potion.
    [InlineData(7110)] // Upgradable legacy package.
    [InlineData(10002915)] // Wedding guardian ticket: corrected PVF must have one unambiguous type.
    public async Task Potions_and_packages_share_the_consumable_admission_category(int itemId)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var placement = await new PvfItemPlacementDefinitionSource(provider).GetAsync(itemId, default);
        Assert.NotNull(placement);
        Assert.Equal(ItemTraits.Consumable, placement.Traits);
    }

    private static readonly DateTimeOffset PreparedAt =
        new(2026, 8, 4, 6, 0, 0, TimeSpan.FromHours(8));

    [SkippableFact]
    public async Task Known_creature_equipment_uses_the_first_pet_equipment_slot()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var placement = await new PvfItemPlacementDefinitionSource(provider).GetAsync(64016, default);
        Assert.NotNull(placement);
        Assert.Equal((byte)6, placement.ItemKind);
        Assert.Equal(new BusinessSlotRange(BusinessInventorySpace.CreatureEquipment, 0, 48), placement.Destination);
    }

    [Fact]
    public void DailyRefill_ParsesCompleteTuplesSkipsInvalidRowsAndRejectsTruncatedSection()
    {
        string[] values =
        [
            "2680738", "2", "2030-01-01 06:00:00", "0",
            "123", "0", "2030-01-01 06:00:00", "0",
            "124", "1", "2015-01-01 06:00:00", "0",
            "690001556", "1", "2030-01-01 06:00:00", "1",
        ];

        var rules = PvfEntryLifecycleContentSource.Parse(values, PreparedAt,
            _ => new PvfEntryLifecycleContentSource.RefillItemMetadata(100, 2, 65, 120, ItemWorldClass.Interchangeable));

        Assert.Equal([2680738, 690001556], rules.Select(value => value.ItemId));
        // How many a rule grants is no longer the content source's judgement to make.
        Assert.Equal(2, new DailyRefillRuleService().GrantFor(rules[0], 0));
        Assert.Equal(1, new DailyRefillRuleService().GrantFor(rules[1], 99));
        Assert.Throws<FormatException>(() => PvfEntryLifecycleContentSource.Parse(
            ["1", "2", "2030-01-01 06:00:00"], PreparedAt,
            _ => new PvfEntryLifecycleContentSource.RefillItemMetadata(100, 2, 65, 120, ItemWorldClass.Interchangeable)));

        Assert.Equal(7, PvfEntryLifecycleContentSource.ResolveStackableItemKind(1, "[creature]"));
        Assert.Equal(9, PvfEntryLifecycleContentSource.ResolveStackableItemKind(1, "[avatar emblem]"));
        Assert.Equal(10, PvfEntryLifecycleContentSource.ResolveStackableItemKind(1, "[material expert job]"));
        Assert.Equal(4, PvfEntryLifecycleContentSource.ResolveStackableItemKind(1, "[quest]"));
        Assert.Equal(3, PvfEntryLifecycleContentSource.ResolveStackableItemKind(100, "[material]"));
        Assert.Equal(11, PvfEntryLifecycleContentSource.ResolveStackableItemKind(3033, "[material]"));
        Assert.Equal(2, PvfEntryLifecycleContentSource.ResolveStackableItemKind(1, "[consumable]"));
    }

    /// <summary>
    /// What an <c>[equipment type]</c> name says the piece is, including the avatar answer.
    /// </summary>
    /// <remarks>
    /// The avatar branch is the one of the three with a consequence beyond the classification
    /// itself: the entry load refuses a row the archive calls an avatar unless the row says so too,
    /// and nothing else decides that. It was the only branch of this function no assertion reached,
    /// so a reading that stopped answering <see cref="ItemWorldClass.Avatar"/> would have left the
    /// suite green and the refusal silently disarmed.
    ///
    /// The two avatar names are the ones this function's own remarks give; the rest are names the
    /// slot table already lists. The backtick and the padding are what the archive's strings
    /// actually carry, so the normalisation is asserted rather than assumed.
    /// </remarks>
    [Theory]
    [InlineData("[hat avatar]", ItemWorldClass.Avatar)]
    [InlineData("[coat avatar]", ItemWorldClass.Avatar)]
    [InlineData("`[coat avatar]`", ItemWorldClass.Avatar)]
    [InlineData("  [COAT AVATAR]  ", ItemWorldClass.Avatar)]
    [InlineData("[creature]", ItemWorldClass.Creature)]
    [InlineData("`[creature]`", ItemWorldClass.Creature)]
    [InlineData("[coat]", ItemWorldClass.Equipment)]
    [InlineData("[weapon]", ItemWorldClass.Equipment)]
    [InlineData("", ItemWorldClass.Equipment)]
    [InlineData(null, ItemWorldClass.Equipment)]
    public void EquipmentType_IsReadAsTheAvatarTheCreatureOrOrdinaryGear(
        string? equipmentType, ItemWorldClass expected) =>
        Assert.Equal(
            expected, PvfEntryLifecycleContentSource.ClassifyEquipmentType(equipmentType));

    [SkippableFact]
    public async Task DailyRefill_RealArchiveProducesAuthoritativeRulesWithStackLimits()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfEntryLifecycleContentSource(provider);

        var rules = await source.GetDailyRefillRulesAsync(PreparedAt, CancellationToken.None);
        Assert.NotEmpty(rules);
        Assert.All(rules, rule =>
        {
            Assert.True(rule.ItemId > 0);
            Assert.True(rule.Quantity > 0);
            Assert.True(rule.ExpiresAt > PreparedAt);
            Assert.Contains(rule.ItemKind, new byte[] { 1, 2, 3, 4, 7, 9, 10, 11 });
            Assert.Equal(BusinessInventorySpace.Quest, rule.Destination.Space);
            Assert.Equal(0, rule.Destination.FirstIndex);
            Assert.Equal(55, rule.Destination.LastIndex);
        });
        Assert.Equal(
            new Dictionary<int, byte>
            {
                [2680738] = 4,
                [2749110] = 4,
                [10002520] = 4,
                [10093393] = 4,
                [10003235] = 4,
                [690001556] = 4,
            },
            rules.ToDictionary(rule => rule.ItemId, rule => rule.ItemKind));
    }
}
