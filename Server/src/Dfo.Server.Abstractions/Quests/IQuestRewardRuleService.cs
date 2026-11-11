using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.Quests;

public sealed record QuestRewardCandidate(byte Job, byte GrowType, byte Level);

public sealed record QuestRewardItemGrant(int ItemId, int Count);

public sealed record QuestCompletionRewardPlan(
    uint Experience,
    int Gold,
    IReadOnlyList<QuestRewardItemGrant> Items,
    byte? GrowType = null,
    IReadOnlyList<SeekingItemRequirement>? ConsumedItems = null)
{
    public byte? Awakening { get; init; }

    /// <summary>The extra equipment slot a <c>[slot expansion]</c> reward opens.</summary>
    public Dfo.Server.Data.Characters.ExtraEquipmentSlots ExtraEquipmentSlot { get; init; }
}

public enum QuestRewardRejection
{
    InvalidCompletionCount,
    InvalidCareerReward,
    CycleRewardUnavailable,
    UnmappedTitleReward,
    RewardSelectionForbidden,
    RewardSelectionRequired,
    RewardSelectionOutOfRange,
    InvalidSlotExpansionReward,
}

public sealed record QuestRewardResolution(
    QuestCompletionRewardPlan? Plan,
    QuestRewardRejection? Rejection)
{
    public bool IsValid => this.Plan is not null && this.Rejection is null;

    public static QuestRewardResolution Valid(QuestCompletionRewardPlan plan) => new(plan, null);

    public static QuestRewardResolution Refused(QuestRewardRejection rejection) => new(null, rejection);
}

public interface IQuestRewardRuleService
{
    QuestRewardResolution Resolve(
        QuestDefinition definition,
        QuestParameters parameters,
        QuestRewardCandidate candidate,
        ushort rewardSelection,
        ushort completionCount);
}
