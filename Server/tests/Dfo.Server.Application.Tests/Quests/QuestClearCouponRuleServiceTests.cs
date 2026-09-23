using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Quests;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Quests;

public sealed class QuestClearCouponRuleServiceTests
{
    [Fact]
    public void Only_level_eligible_epics_are_cleared_without_pulling_other_grade_prerequisites()
    {
        var side = Quest(10, 1) with { Grade = QuestGrade.Side };
        var story = Quest(11, 80) with { CompletedQuestPrerequisiteGroups = [[10]] };
        Assert.Equal([1, 11], Select([Quest(1, 1) with { RequiredLevel = new(1, 5) },
            side, story, Quest(12, 81), Quest(13, 1) with { Grade = QuestGrade.Normal },
            Quest(14, 1) with { Grade = QuestGrade.CommonUnique },
            Quest(15, 1) with { Grade = QuestGrade.Achievement },
            Quest(16, 1) with { Grade = QuestGrade.System }]));
    }

    [Fact]
    public void Xilan_dialogue_and_both_story_branches_are_completed_without_answer_gates()
    {
        var dialogue = Quest(2403, 70) with { ObjectiveTypeCode = "[question]" };
        var first = Quest(2404, 70) with { RequiredQuestAnswers = [new(2403, 0)],
            CompletedQuestPrerequisiteGroups = [[2403]], CollisionQuestIds = [2405] };
        var second = Quest(2405, 70) with { RequiredQuestAnswers = [new(2403, 1)],
            CompletedQuestPrerequisiteGroups = [[2403]], AccountCollisionQuestIds = [2404] };
        Assert.Equal([2403, 2404, 2405], Select([dialogue, first, second]));
        Assert.Equal([2404, 2405], Select([dialogue, first, second], [new(2403, 2)]));
    }

    [Fact]
    public void Acceptance_gates_do_not_block_story_catchup_or_expand_the_selected_scope()
    {
        Assert.Equal([1, 2, 3, 4], Select([
            Quest(1, 1) with { CompletedQuestPrerequisiteGroups = [[2]] },
            Quest(2, 1) with { CompletedQuestPrerequisiteGroups = [[1]] },
            Quest(3, 1) with { IsExposedByNpc = false, IsEventOnly = true },
            Quest(4, 1) with { CompletedQuestPrerequisiteGroups = [[999]] }]));
    }

    [Fact]
    public void Character_restrictions_invalid_levels_and_existing_completion_are_respected()
    {
        Assert.Equal([1], Select([
            Quest(1, 80), Quest(2, 1) with { Grade = QuestGrade.Daily },
            Quest(3, 1) with { JobRestriction = new(false, [QuestJobRequirement.Gunner]) },
            Quest(4, 1) with { GrowTypeRequirement = 2 },
            Quest(5, 1) with { RequiredLevel = null },
            Quest(6, 1) with { RequiredLevel = new(20, 10) },
            Quest(7, 1), Quest(30000, 1)], [new(7, 2)]));
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
