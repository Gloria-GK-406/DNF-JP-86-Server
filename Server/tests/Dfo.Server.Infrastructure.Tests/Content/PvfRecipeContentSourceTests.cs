using Dfo.Pvf.Tests.Support;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Infrastructure.Tests.Content;
public sealed class PvfRecipeContentSourceTests
{
    [SkippableFact]
    public async Task Pure_amplification_drawing_returns_exact_requirements_and_output()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var recipe = await new PvfRecipeContentSource(provider).GetAsync(1287, default);
        Assert.NotNull(recipe); Assert.Equal(new[] { new RecipeAmount(3244, 25), new RecipeAmount(3262, 100) }, recipe.Required);
        Assert.Equal(new RecipeAmount(1286, 1), Assert.Single(recipe.Produced));
        Assert.Null(await new PvfRecipeContentSource(provider).GetAsync(1286, default));
        var reward = await new PvfPackageOpeningDefinitionSource(provider, new PvfCeraShopDefinitionSource(provider), new PvfItemPlacementDefinitionSource(provider, new Dfo.Server.Abstractions.Inventory.InventorySlotMappingRuleService())).GetRewardAsync(1286, default);
        Assert.NotNull(reward); Assert.True(reward.Supported); Assert.Equal(1000, reward.Placement.StackLimit);
    }
}
