using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.World;

namespace Dfo.Server.Application.Tests.Guilds;

[Trait("Category", "Unit")]
public sealed class GuildTravelRuleTests
{
    private static readonly TownAreaDefinition Area = new(1, "town/map", TownAreaKind.Normal, 0, 0)
        { MinimumLevel = 20 };

    [Theory]
    [InlineData(null)]
    [InlineData(19)]
    public void Missing_or_insufficient_level_locks_the_destination_before_loading_quests(int? level)
    {
        var gated = Area with { RequiredQuestIds = [10] };
        Assert.Equal(GuildTeleportEligibility.Locked, GuildTravelRuleService.EvaluateTeleportEligibility(gated, level));
    }

    [Fact]
    public void At_the_minimum_level_only_quest_gated_destinations_need_the_log()
    {
        Assert.Equal(GuildTeleportEligibility.Allowed, GuildTravelRuleService.EvaluateTeleportEligibility(Area, 20));
        var gated = Area with { RequiredQuestIds = [10, 11] };
        Assert.Equal(GuildTeleportEligibility.NeedsQuestLog, GuildTravelRuleService.EvaluateTeleportEligibility(gated, 20));
        GuildTeleportEligibility Evaluate(params ClearedQuest[] cleared) =>
            GuildTravelRuleService.EvaluateTeleportEligibility(gated, 20, cleared);

        Assert.Equal(GuildTeleportEligibility.Locked, Evaluate());
        Assert.Equal(GuildTeleportEligibility.Locked, Evaluate(new(10, 1), new(12, 1)));
        Assert.Equal(GuildTeleportEligibility.Locked, Evaluate(new(10, 1), new(11, 0)));
        Assert.Equal(GuildTeleportEligibility.Allowed, Evaluate(new(10, 1), new(11, 2)));
    }
}
