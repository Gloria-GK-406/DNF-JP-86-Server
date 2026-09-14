using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.Quests;

/// <summary>
/// What the quests a character is working through say.
/// </summary>
/// <remarks>
/// The seam the quest use cases reach the archive through. It carries nothing of its own: what a
/// quest asks for and what a completion pays are rules, and this is the point at which a flow that
/// changes a character's quest log gets to ask about them without holding the rules itself.
/// </remarks>
public interface IQuestDefinitionDomainService
{
    /// <summary>One quest, or null when the content does not define it.</summary>
    ValueTask<QuestDefinition?> FindAsync(int questId, CancellationToken cancellationToken);

    /// <summary>The shared parameters a quest reward is worked out from.</summary>
    QuestParameters RewardParameters();

    QuestRewardResolution ResolveRewards(
        QuestDefinition definition,
        QuestRewardCandidate candidate,
        ushort rewardSelection,
        ushort completionCount);
}
