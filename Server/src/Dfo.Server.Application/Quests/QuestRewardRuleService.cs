using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Quests;

internal sealed class QuestRewardRuleService(ICycleDungeonRuleService? cycles = null) : IQuestRewardRuleService
{
    private const ushort NoRewardSelection = ushort.MaxValue;

    public QuestRewardResolution Resolve(
        QuestDefinition definition,
        QuestParameters parameters,
        QuestRewardCandidate candidate,
        ushort rewardSelection,
        ushort completionCount)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(candidate);

        if (completionCount != 1)
            return QuestRewardResolution.Refused(QuestRewardRejection.InvalidCompletionCount);

        var careerRewards = definition.Rewards.Where(reward => reward.Kind == QuestRewardKind.GrowType).ToArray();
        if (careerRewards.Length > 1 || careerRewards.Any(reward =>
                reward.Values.Count != 1 || reward.Values[0] is < 1 or > 15))
            return QuestRewardResolution.Refused(QuestRewardRejection.InvalidCareerReward);
        byte? growType = careerRewards.Length == 0 ? null : checked((byte)careerRewards[0].Values[0]);
        var declaration = definition.ItemRewards;
        var eligibleSelection = declaration.Selectable
            .Where(rule => Matches(rule, candidate))
            .ToArray();
        if (declaration.Selectable.Count == 0)
        {
            if (rewardSelection != NoRewardSelection)
            {
                return QuestRewardResolution.Refused(
                    QuestRewardRejection.RewardSelectionForbidden);
            }
        }
        else
        {
            if (rewardSelection == NoRewardSelection)
            {
                return QuestRewardResolution.Refused(
                    QuestRewardRejection.RewardSelectionRequired);
            }

            if (rewardSelection >= eligibleSelection.Length)
            {
                return QuestRewardResolution.Refused(
                    QuestRewardRejection.RewardSelectionOutOfRange);
            }
        }

        var items = declaration.Fixed
            .Where(rule => Matches(rule, candidate))
            .Select(rule => new QuestRewardItemGrant(rule.ItemId, rule.Count))
            .ToList();
        if (declaration.Selectable.Count > 0)
        {
            var selected = eligibleSelection[rewardSelection];
            items.Add(new QuestRewardItemGrant(selected.ItemId, selected.Count));
        }

        var rewardLevel = definition.RequiredLevel is { Minimum: > 0 } band ? band.Minimum : 1;
        var difficulty = string.IsNullOrWhiteSpace(definition.DifficultyCode)
            ? ' '
            : definition.DifficultyCode.Trim()[0];
        var experience = parameters.ExperienceFor(
            candidate.Level,
            rewardLevel,
            difficulty,
            definition.Grade == QuestGrade.Epic,
            definition.PaysAtCharacterLevel);
        var gold = declaration.FixedGold > 0
            ? declaration.FixedGold
            : declaration.HasGoldFormulaMarker || definition.GoldMultiplier > 0
                ? checked((int)parameters.GoldFor(
                    candidate.Level,
                    rewardLevel,
                    definition.GoldMultiplier,
                    definition.PaysAtCharacterLevel))
                : 0;

        if (definition.Grade == QuestGrade.Circle)
        {
            var regional = cycles?.RewardsForQuest(definition.QuestId);
            if (regional is not { Count: > 0 } || !definition.Rewards.Any(x => x.Kind == QuestRewardKind.CircleDungeon))
                return QuestRewardResolution.Refused(QuestRewardRejection.CycleRewardUnavailable);
            return QuestRewardResolution.Valid(new(experience, 0, regional, ConsumedItems: definition.SeekingItems));
        }
        var consumes = definition.ObjectiveTypeCode.Trim().Trim('[', ']').ToLowerInvariant()
            is "seeking" or "seek n meet npc" ? definition.SeekingItems : null;
        return QuestRewardResolution.Valid(new QuestCompletionRewardPlan(experience, gold, items, growType, consumes));
    }

    private static bool Matches(QuestItemRewardRule rule, QuestRewardCandidate candidate)
    {
        if (rule.Condition is not { } condition)
            return true;
        var jobMatches = condition.JobId < 0 || condition.JobId == candidate.Job;
        var growType = candidate.GrowType & 0x0F;
        var growMatches = condition.GrowType < 0 || condition.GrowType == growType;
        return jobMatches && growMatches;
    }
}
