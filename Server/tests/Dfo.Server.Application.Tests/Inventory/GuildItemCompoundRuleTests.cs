using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

[Trait("Category", "Unit")]
public sealed class GuildItemCompoundRuleTests
{
    [Theory]
    [InlineData(GuildCompoundKind.GuardianGem, 0, 2, 2, true)]
    [InlineData(GuildCompoundKind.Medal, 6, 4, 4, true)]
    [InlineData(GuildCompoundKind.GuardianGem, 1, 1, 4, false)]
    [InlineData(GuildCompoundKind.Medal, 1, 5, 5, false)]
    [InlineData(GuildCompoundKind.GuardianGem, 1, 4, 3, false)]
    [InlineData(GuildCompoundKind.Medal, -1, 2, 4, false)]
    [InlineData(GuildCompoundKind.GuardianGem, 7, 2, 4, false)]
    [InlineData((GuildCompoundKind)2, 1, 2, 4, false)]
    public void Recipe_requires_supported_kind_rarity_and_material_count(
        GuildCompoundKind kind, int rarity, int count, int recipeLimit, bool eligible)
    {
        var recipe = new GuildCompoundRule(recipeLimit, 0, [100], []);
        var content = TestRequired.Absent<IGuildCompoundContentSource>((_, _) => recipe);
        var rules = new GuildItemCompoundRuleService(content, TestRequired.Absent<ILotteryRandomSource>());
        var result = rules.Recipe(new(kind, rarity, new BusinessSlot[count]));
        Assert.Same(eligible ? recipe : null, result);
    }
}
