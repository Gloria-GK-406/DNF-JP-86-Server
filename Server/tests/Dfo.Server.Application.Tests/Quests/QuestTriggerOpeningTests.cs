using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;

namespace Dfo.Server.Application.Tests.Quests;

/// <summary>The one way a declared objective becomes the trigger a quest opens at.</summary>
[Trait("Category", "Unit")]
public sealed class QuestTriggerOpeningTests
{
    [Fact]
    public void Each_named_channel_opens_at_its_count()
    {
        Assert.Equal(1u, QuestTrigger.FromRemainingCounts([(0, 1)]).PackedValue);
        Assert.Equal(2u | (3u << 9) | (5u << 18),
            QuestTrigger.FromRemainingCounts([(2, 5), (0, 2), (1, 3)]).PackedValue);
        Assert.Equal(0x7FFFFFFu,
            QuestTrigger.FromRemainingCounts([(0, 511), (1, 511), (2, 511)]).PackedValue);
        Assert.Equal(5u << 9, QuestTrigger.FromRemainingCounts([(1, 5)]).PackedValue);
    }

    /// <summary>
    /// Wrapping would open 600 at 88 and spill into the next channel; clamping a non-positive count
    /// would open the quest already complete. Neither is a reading of the declaration.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 512)]
    [InlineData(0, 600)]
    [InlineData(-1, 1)]
    [InlineData(3, 1)]
    public void A_count_or_channel_the_word_cannot_hold_is_refused(int channel, int count) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => QuestTrigger.FromRemainingCounts([(channel, count)]));

    [Fact]
    public void A_channel_named_twice_or_no_channel_at_all_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => QuestTrigger.FromRemainingCounts([(0, 1), (0, 2)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuestTrigger.FromRemainingCounts([]));
    }

    [Fact]
    public void Kill_and_mirror_hunt_objectives_open_through_the_same_construction()
    {
        var kill = new QuestKillObjectiveDefinition(QuestKillParseStatus.Parsed,
        [
            new(new(QuestKillScopeKind.Dungeon, 144), 0, QuestKillDifficultyMatch.Minimum, new(QuestKillTargetKind.Exact, 1, 0), 2, 0),
            new(new(QuestKillScopeKind.Dungeon, 144), 0, QuestKillDifficultyMatch.Minimum, new(QuestKillTargetKind.Exact, 2, 0), 3, 1),
        ], []);
        Assert.Equal(2u | (3u << 9), kill.InitialTrigger);

        var mirror = new MirrorQuestObjective(Definition(QuestGrade.Crack), MirrorQuestKind.Hunt,
            [new MirrorHuntTarget(144, 0, 1, 0, 4, 0), new MirrorHuntTarget(144, 0, 2, 0, 6, 2)]);
        Assert.Equal(4u | (6u << 18), mirror.InitialTrigger);
    }

    /// <summary>
    /// A title's objective arguments follow the achievement flow's grammar -- condition under clear
    /// 13 ends in a consumable limit, not a count -- so the ordinary log derives nothing from them.
    /// </summary>
    [Theory]
    [InlineData(QuestGrade.Title)]
    [InlineData(QuestGrade.Crack)]
    public void Quests_managed_elsewhere_keep_the_declared_trigger(QuestGrade grade)
    {
        var kill = new QuestKillObjectiveDefinition(QuestKillParseStatus.Parsed,
            [new(new(QuestKillScopeKind.AnyDungeon), 0, QuestKillDifficultyMatch.Minimum, new(QuestKillTargetKind.Exact, 1, 0), 7, 0)], []);
        var managed = Definition(grade) with { KillObjectives = kill };
        var ordinary = Definition(QuestGrade.Normal) with { KillObjectives = kill };

        Assert.True(managed.IsManagedByAchievement);
        Assert.Equal(1u, managed.InitialTrigger);
        Assert.False(ordinary.IsManagedByAchievement);
        Assert.Equal(7u, ordinary.InitialTrigger);
    }

    private static QuestDefinition Definition(QuestGrade grade) =>
        new(9000, "", grade, null, null, null, null, null, [], "hunt monster", "",
            null, null, null, null, null, null, null, [], [], [], []);
}
