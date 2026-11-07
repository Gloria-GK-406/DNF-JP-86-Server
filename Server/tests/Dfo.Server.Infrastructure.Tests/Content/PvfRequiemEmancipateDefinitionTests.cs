using Dfo.Pvf.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfRequiemEmancipateDefinitionTests
{
    [SkippableFact]
    public async Task A21_requiem_short_sword_converts_with_the_requiem_book()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfEquipmentEmancipateDefinitionSource(provider);

        Assert.Equal(new EmancipateTicketDefinition(19), await source.GetTicketAsync(10150749, default));
        Assert.Equal(new EmancipateTicketDefinition(2), await source.GetTicketAsync(10012055, default));
        Assert.Null(await source.GetTicketAsync(10000204, default));
        Assert.Null(await source.GetTicketAsync(101000027, default));

        var block = Assert.Single(await source.GetEquipmentAsync(101000027, default), x => x.Type == 19);
        Assert.Equal([new EmancipateItemCount(10000204, 15), new EmancipateItemCount(10000327, 75),
            new EmancipateItemCount(10000224, 1), new EmancipateItemCount(3167, 250), new EmancipateItemCount(3037, 10000)], block.Inputs);
        Assert.Equal([new EmancipateItemCount(101000632, 1)], block.Outputs);
        Assert.Null(block.Growth);
    }

    [SkippableFact]
    public async Task A21_liberated_short_sword_drawing_is_a_ten_percent_upgrade_that_hands_the_weapon_back()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var plan = await new PvfRecipeContentSource(provider).GetAsync(2651400, default);

        Assert.NotNull(plan);
        Assert.Equal([new RecipeAmount(101000027, 1), new RecipeAmount(10000204, 15), new RecipeAmount(10000327, 75),
            new RecipeAmount(10000224, 1), new RecipeAmount(3167, 250), new RecipeAmount(3037, 10000)], plan.Required.ToArray());
        Assert.Equal([new RecipeAmount(101000028, 1)], plan.Produced.ToArray());
        var chance = Assert.IsType<RecipeChance>(plan.Chance);
        Assert.Equal(1000, chance.SuccessPerTenThousand);
        Assert.True(chance.MaintainAbility);
        Assert.Equal([new RecipeAmount(101000027, 1), new RecipeAmount(10150748, 1)], chance.FailOutputs.ToArray());
    }
}
