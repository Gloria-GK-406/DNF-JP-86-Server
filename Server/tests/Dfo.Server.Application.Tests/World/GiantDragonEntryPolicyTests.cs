using Dfo.Server.Abstractions.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class GiantDragonDungeonTests
{
    [SkippableTheory]
    [InlineData(3200, null, GiantDragonQuestRequirement.None, false)]
    [InlineData(3201, null, GiantDragonQuestRequirement.None, false)]
    [InlineData(3202, null, GiantDragonQuestRequirement.None, false)]
    [InlineData(3536, 20720, GiantDragonQuestRequirement.Cleared, false)]
    [InlineData(3537, 20722, GiantDragonQuestRequirement.Cleared, false)]
    [InlineData(3538, 20728, GiantDragonQuestRequirement.ActiveOrCleared, false)]
    [InlineData(3539, 20728, GiantDragonQuestRequirement.Cleared, false)]
    [InlineData(3540, 20727, GiantDragonQuestRequirement.Active, true)]
    public async Task Entry_plan_owns_each_quest_mode_and_directed_maze_policy(
        int dungeon, int? quest, GiantDragonQuestRequirement requirement, bool directed)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = await Fixture(restored: true);
        var plan = await f.Services.GetRequiredService<IGiantDragonRuleService>().EntryAsync(dungeon, default);
        Assert.Equal(quest, plan.QuestId);
        Assert.Equal(requirement, plan.QuestRequirement);
        Assert.Equal<DungeonEntryIntent?>(directed ? DungeonEntryIntent.Directed(dungeon) : null, plan.IntentOverride);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(GiantDragonQuestRequirement.None, false, false, true)]
    [InlineData(GiantDragonQuestRequirement.Active, false, false, false)]
    [InlineData(GiantDragonQuestRequirement.Active, false, true, false)]
    [InlineData(GiantDragonQuestRequirement.Active, true, false, true)]
    [InlineData(GiantDragonQuestRequirement.Active, true, true, true)]
    [InlineData(GiantDragonQuestRequirement.Cleared, false, false, false)]
    [InlineData(GiantDragonQuestRequirement.Cleared, false, true, true)]
    [InlineData(GiantDragonQuestRequirement.Cleared, true, false, false)]
    [InlineData(GiantDragonQuestRequirement.Cleared, true, true, true)]
    [InlineData(GiantDragonQuestRequirement.ActiveOrCleared, false, false, false)]
    [InlineData(GiantDragonQuestRequirement.ActiveOrCleared, false, true, true)]
    [InlineData(GiantDragonQuestRequirement.ActiveOrCleared, true, false, true)]
    [InlineData(GiantDragonQuestRequirement.ActiveOrCleared, true, true, true)]
    public void Quest_mode_keeps_active_only_distinct_from_active_or_cleared(
        GiantDragonQuestRequirement requirement, bool active, bool cleared, bool expected)
    {
        var plan = new GiantDragonEntryRule(null!, 0, 0, 1, requirement);
        Assert.Equal(expected, plan.AcceptsQuest(active, cleared));
    }
}
