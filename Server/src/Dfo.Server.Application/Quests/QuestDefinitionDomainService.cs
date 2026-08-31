using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;

namespace Dfo.Server.Application.Quests;

/// <summary>What the quests a character is working through say.</summary>
internal sealed class QuestDefinitionDomainService(
    IQuestContentRuleService quests) : IQuestDefinitionDomainService
{
    public ValueTask<QuestDefinition?> FindAsync(int questId, CancellationToken cancellationToken) =>
        quests.FindAsync(questId, cancellationToken);

    public QuestParameters RewardParameters() => quests.Parameters();
}
