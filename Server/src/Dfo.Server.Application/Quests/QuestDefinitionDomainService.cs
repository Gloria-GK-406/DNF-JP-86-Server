using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;

namespace Dfo.Server.Application.Quests;

/// <summary>What the quests a character is working through say.</summary>
internal sealed class QuestDefinitionDomainService(
    IQuestContentRuleService quests,
    IQuestRewardRuleService rewards) : IQuestDefinitionDomainService
{
    public ValueTask<QuestDefinition?> FindAsync(int questId, CancellationToken cancellationToken) =>
        quests.FindAsync(questId, cancellationToken);

    public QuestParameters RewardParameters() => quests.Parameters();

    public QuestRewardResolution ResolveRewards(
        QuestDefinition definition,
        QuestRewardCandidate candidate,
        ushort rewardSelection,
        ushort completionCount) =>
        rewards.Resolve(definition, quests.Parameters(), candidate, rewardSelection, completionCount);
}
