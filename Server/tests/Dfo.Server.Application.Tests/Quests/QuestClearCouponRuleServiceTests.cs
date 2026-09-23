using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Quests;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Quests;

public sealed class QuestClearCouponRuleServiceTests
{
    [Fact]
    public void Catchup_includes_old_story_and_safe_prerequisites_but_not_future_or_standalone_side()
    {
        var side = Quest(10, 1) with { Grade = QuestGrade.Side, RequiredLevel = new(1, 5) };
        var story = Quest(11, 80) with { CompletedQuestPrerequisiteGroups = [[10]] };
        Assert.Equal([1, 10, 11], Select([Quest(1, 1), side, story, Quest(12, 81),
            Quest(13, 1) with { Grade = QuestGrade.Side }]));
    }

    [Fact]
    public void Special_progression_and_branches_that_depend_on_it_are_left_alone()
    {
        var career = Quest(1, 15) with { JobChangeQuestCode = 1 };
        var dependent = Quest(2, 20) with { CompletedQuestPrerequisiteGroups = [[1]] };
        var permanent = Quest(3, 20) with { HasSpecialRewardStatus = true };
        var awakening = Quest(4, 20) with { Rewards = [new(QuestRewardKind.AwakeningType, [1])] };
        Assert.Empty(Select([career, dependent, permanent, awakening]));
        Assert.Equal([2], Select([career, dependent], [new ClearedQuest(1, 1)]));
    }

    [Fact]
    public void Alternative_prerequisites_choose_one_complete_safe_group_without_leaking_failed_branch()
    {
        var side = Quest(10, 1) with { Grade = QuestGrade.Side };
        var root = Quest(1, 80) with { CompletedQuestPrerequisiteGroups = [[10, 999], [11]] };
        Assert.Equal([1, 11], Select([root, side, side with { QuestId = 11 }]));
    }

    [Fact]
    public void Cycles_missing_definitions_and_unresolved_choices_do_not_clear_partial_roots()
    {
        Assert.Empty(Select([
            Quest(1, 1) with { CompletedQuestPrerequisiteGroups = [[2]] },
            Quest(2, 1) with { CompletedQuestPrerequisiteGroups = [[1]] },
            Quest(3, 1) with { RequiredQuestAnswers = [new(4, 1)] },
            Quest(5, 1) with { CollisionQuestIds = [6] },
            Quest(7, 1) with { CompletedQuestPrerequisiteGroups = [[999]] }]));
    }

    [Fact]
    public void Character_restrictions_repeatables_and_unreadable_levels_are_respected()
    {
        Assert.Equal([1], Select([
            Quest(1, 80), Quest(2, 1) with { Grade = QuestGrade.Daily },
            Quest(3, 1) with { JobRestriction = new(false, [QuestJobRequirement.Gunner]) },
            Quest(4, 1) with { GrowTypeRequirement = 2 },
            Quest(5, 1) with { RequiredLevel = null },
            Quest(6, 1) with { IsEventOnly = true },
            Quest(7, 1) with { IsExposedByNpc = false }]));
    }

    [Theory]
    [InlineData(85, 0)]
    [InlineData(80, 1800000000)]
    public async Task Coupon_ceiling_and_exact_expiration_boundary_refuse_before_loading_quests(byte level, long expiry)
    {
        var service = new QuestClearCouponRuleService(new ItemSource(new("[quest clear]", 0, 84, expiry, true)), new NoQuests());
        var error = await Assert.ThrowsAsync<OpeningRejectedException>(async () =>
            await service.SelectAsync(10006007, level, 0, 0, [], DateTimeOffset.FromUnixTimeSeconds(1800000000), default));
        Assert.Equal(OpeningRejection.ItemUnusable, error.Reason);
    }

    [Fact]
    public async Task Request_action_cannot_turn_another_item_into_a_coupon()
    {
        var service = new QuestClearCouponRuleService(new ItemSource(new("[quest clear coupon]", 1, 84, 0, true)), new NoQuests());
        var error = await Assert.ThrowsAsync<OpeningRejectedException>(async () =>
            await service.SelectAsync(10006007, 80, 0, 0, [], DateTimeOffset.UtcNow, default));
        Assert.Equal(OpeningRejection.UnsupportedContent, error.Reason);
    }

    private static IReadOnlyList<int> Select(IReadOnlyList<QuestDefinition> quests, IReadOnlyList<ClearedQuest>? cleared = null) =>
        QuestClearCouponRuleService.Select(quests, 80, 0, 0, cleared ?? []);

    private static QuestDefinition Quest(int id, int level) => new(id, "", QuestGrade.Epic,
        null, new(level, 99), null, null, null, [], "meet npc", "", true, false,
        null, null, null, null, null, [], [], [], []);

    private sealed class ItemSource(StackableActionDefinition item) : IStackableActionDefinitionSource
    {
        public ValueTask<StackableActionDefinition?> GetAsync(int itemId, CancellationToken cancellationToken) => ValueTask.FromResult<StackableActionDefinition?>(item);
    }

    private sealed class NoQuests : IQuestContentRuleService
    {
        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Must reject before reading quests.");
        public ValueTask<QuestDefinition?> FindAsync(int questId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public QuestParameters Parameters() => throw new NotSupportedException();
    }
}
