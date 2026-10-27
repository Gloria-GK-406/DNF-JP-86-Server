using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Tests.World;

[Trait("Category", "Unit")]
public sealed class DungeonClearExperienceRulesTests
{
    private static readonly ExperienceParameters Rates = new([1], [1, 1.5, 2, 2.5], 1, 1, [0.05, 0.1, 0.12, 0.15, 0.2]);
    [Theory]
    [InlineData(0, 5, 1, 0u)]
    [InlineData(10, 5, 1, 350u)]
    [InlineData(20, 5, 1, 700u)]
    [InlineData(10, 12, 1, 17u)]
    [InlineData(10, 5, 2, 262u)]
    [InlineData(10, 5, 4, 218u)]
    public void RealKillsPartyAndLevelGapPriceTheClear(int kills, int level, int party, uint expected)
    {
        var clear = new ClearCircumstances(5, 0, 1, 99, kills, party, level);
        Assert.Equal(expected, DungeonClearExperienceRules.BaseWorth(clear, ExperienceTuning.Default, Rates));
    }
    [Fact]
    public void RankIsAnAdditionalPartOfTheClear()
    {
        Assert.Equal(70u, DungeonClearExperienceRules.RankBonus(350, 99,
            new DungeonRankGrades([99, 90, 80, 60, 50]), Rates));
    }
}
