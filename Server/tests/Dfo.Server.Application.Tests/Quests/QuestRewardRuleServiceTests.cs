using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Quests;

namespace Dfo.Server.Application.Tests.Quests;

public sealed class QuestRewardRuleServiceTests
{
    private static readonly QuestParameters Parameters = new(
        [100],
        new Dictionary<char, int> { ['M'] = 10 },
        80,
        30,
        120,
        140,
        [72, 101]);

    [Fact]
    public void FixedAndSelectedRewardsAreFilteredBeforeSelectionAndKeepDeclarationOrder()
    {
        var quest = Quest(new QuestItemRewardDefinition(
            Fixed:
            [
                new(10, 2),
                new(20, 1, new(0, -1)),
                new(20, 1, new(1, -1)),
            ],
            Selectable:
            [
                new(30, 1, new(1, -1)),
                new(40, 3, new(0, -1)),
                new(50, 4, new(0, 2)),
            ],
            HasGoldFormulaMarker: false,
            FixedGold: 0));

        var reward = Resolve(quest, job: 0, growType: 0, selection: 0);

        Assert.True(reward.IsValid);
        Assert.Equal(
            [new QuestRewardItemGrant(10, 2), new(20, 1), new(40, 3)],
            reward.Plan!.Items);
    }

    [Fact]
    public void SelectionPresenceRangeAndCompletionCountFailClosed()
    {
        var withChoice = Quest(new QuestItemRewardDefinition(
            [], [new(40, 1, new(0, -1))], false, 0));
        var withoutChoice = Quest(new QuestItemRewardDefinition(
            [new(10, 1)], [], false, 0));

        Assert.Equal(
            QuestRewardRejection.RewardSelectionRequired,
            Resolve(withChoice, selection: ushort.MaxValue).Rejection);
        Assert.Equal(
            QuestRewardRejection.RewardSelectionOutOfRange,
            Resolve(withChoice, selection: 1).Rejection);
        Assert.Equal(
            QuestRewardRejection.RewardSelectionForbidden,
            Resolve(withoutChoice, selection: 0).Rejection);
        Assert.Equal(
            QuestRewardRejection.InvalidCompletionCount,
            Resolve(withoutChoice, completionCount: 2).Rejection);
    }

    [Fact]
    public void FixedGoldWinsOtherwiseTheFormulaUsesTheQuestTable()
    {
        var fixedGold = Resolve(Quest(new([], [], true, 37)));
        var formula = Resolve(Quest(new([], [], true, 0)));
        var multiplierWithoutMarker = Resolve(
            Quest(new([], [], false, 0)) with { GoldMultiplier = 250 });

        Assert.Equal(37, fixedGold.Plan!.Gold);
        Assert.Equal(101, formula.Plan!.Gold);
        Assert.Equal(252, multiplierWithoutMarker.Plan!.Gold);
    }

    private static QuestRewardResolution Resolve(
        QuestDefinition quest,
        byte job = 0,
        byte growType = 0,
        ushort selection = ushort.MaxValue,
        ushort completionCount = 1) =>
        new QuestRewardRuleService().Resolve(
            quest,
            Parameters,
            new(job, growType, 1),
            selection,
            completionCount);

    private static QuestDefinition Quest(QuestItemRewardDefinition rewards) =>
        new(1, "", QuestGrade.Epic, null, new LevelRange(1, 99), null, null, null,
            [new QuestReward(QuestRewardKind.Item, [])], "meet npc", "M",
            null, null, null, null, null, null, null, [], [], [], [],
            ItemRewards: rewards);
}
