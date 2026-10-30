using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class MarriageProposalContentTests
{
    [SkippableFact]
    public async Task Current_archive_declares_separate_underfoot_church_slots_with_walkable_spawn_points()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var towns = new PvfTownDefinitionSource(provider);
        var underfoot = await towns.GetAsync(2, default);
        Assert.NotNull(underfoot);
        var slots = underfoot.Areas.Where(x => x.AreaId is >= 12 and <= 62
            && x.MapPath.Replace('\\', '/').EndsWith("church/church.map", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Equal(51, slots.Length);
        foreach (var slot in slots)
        {
            var geometry = await towns.GetMapGeometryAsync(slot.MapPath, default);
            Assert.NotNull(geometry);
            Assert.NotEmpty(geometry.WalkableAreas);
            var floor = geometry.WalkableAreas[0];
            Assert.True(geometry.IsWalkable(floor.CenterX, floor.CenterY));
        }
    }
    [SkippableFact]
    public void Ceremony_tiers_and_wait_selections_come_from_the_current_archive()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var content = new PvfMarriageCeremonyContentSource(provider).Load();
        Assert.Equal(new[] { 0, 1, 2 }, content.Tiers.Select(x => x.Tier));
        Assert.Equal(500000, content.Tiers.Single(x => x.Tier == 0).Gold);
        Assert.Equal(new[] { 300, 600, 900 }, content.WaitDurations);
        Assert.Equal(10002629, content.Tiers.Single(x => x.Tier == 0).GiftItemId);
        Assert.Equal(10, content.Tiers.Single(x => x.Tier == 0).OpaqueValue);
        Assert.Equal(60, content.InvitationCutoffSeconds);
        Assert.Equal(999, content.MaximumLovePoints); Assert.Equal(1, content.LovePointItems[10002901]);
        Assert.Equal(new[] { (670, 330, 0), (685, 360, 0) }, content.CeremonyPositions.Select(x => ((int)x.X, (int)x.Y, x.Z)));
        Assert.Equal(10000, content.GiftMinimum); Assert.Equal(400000000, content.GiftMaximum);
        Assert.Equal(50, content.GiftChargePercent); Assert.Equal(6, content.GuestRewards.Select(x => x.MaximumGold).Distinct().Count());
        Assert.Contains(content.GuestRewards, x => x.MaximumGold == 99999 && x.Tier == 0 && x.ItemId == 10002623 && x.Count == 2);
    }
    [SkippableFact]
    public void Minimum_proposal_level_comes_from_current_wedding_content()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var content = new PvfMarriageProposalContentSource(provider).Load();
        Assert.Equal(30, content.MinimumLevel);
        Assert.Equal(20737, content.TrialQuestId);
        Assert.Equal(20738, content.MaterialQuestId);
    }

    [SkippableFact]
    public async Task Current_ordinary_boss_maps_and_trial_reward_box_are_supported()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var definitions = new PvfDungeonDefinitionSource(provider);
        var maps = new PvfDungeonMapDefinitionSource(provider);
        var map = (await maps.ListForDungeonAsync(2, default)).First();
        var source = new PvfMarriageDungeonContentSource(provider, definitions, maps);
        Assert.True(await source.IsOrdinaryMapAsync(2, map.MapId, default));
        Assert.False(await source.IsOrdinaryMapAsync(2, int.MaxValue, default));
        var packages = new PvfPackageOpeningDefinitionSource(provider,
            new PvfCeraShopDefinitionSource(provider), new PvfItemPlacementDefinitionSource(provider, new Dfo.Server.Abstractions.Inventory.InventorySlotMappingRuleService()));
        Assert.NotNull(await packages.GetAsync(10002616, default));
    }
}
