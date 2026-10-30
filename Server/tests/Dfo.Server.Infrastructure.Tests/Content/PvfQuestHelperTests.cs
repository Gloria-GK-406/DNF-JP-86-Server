using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfQuestHelperTests
{
    [SkippableFact]
    public async Task A21_helper_values_and_mixed_quest_requirements_keep_materials_separate()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        Assert.Equal(new[] { 100, 70, 30 }, new PvfPremiumServiceDefinitionSource(provider).ReadQuestHelperMinimumDropRates());
        var placements = new PvfItemPlacementDefinitionSource(provider, new Dfo.Server.Abstractions.Inventory.InventorySlotMappingRuleService());
        Assert.Equal(BusinessInventorySpace.Quest, (await placements.GetAsync(10088410, default))!.Destination.Space);
        Assert.Equal(BusinessInventorySpace.Material, (await placements.GetAsync(3205, default))!.Destination.Space);
        var quest = await new PvfQuestDefinitionSource(provider).GetAsync(12014, default);
        Assert.NotNull(quest);
        Assert.Contains(quest.SeekingItems!, r => r.ItemId == 3205 && r.RequiredCount == 15);
        Assert.Contains(quest.SeekingItems!, r => r.ItemId == 10088410 && r.RequiredCount == 10);
        Assert.DoesNotContain(quest.EnemyRewardItems, r => r.ItemId == 3205);
        Assert.Contains(quest.EnemyRewardItems, r => r.ItemId == 10088410);
    }
}
