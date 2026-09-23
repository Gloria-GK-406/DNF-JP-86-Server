using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class QuestEnemyDropRuleService(
    IQuestContentRuleService quests) : IQuestEnemyDropRuleService
{
    public async ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(
        QuestEnemyDropRequest request, MonsterDropParticipant participant,
        IReadOnlyList<int> questIds, IReadOnlyDictionary<int, long> held,
        CancellationToken cancellationToken)
    {
        var definitions = new List<QuestDefinition>();
        foreach (var questId in questIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await quests.FindAsync(questId, cancellationToken).ConfigureAwait(false) is { } definition)
                definitions.Add(definition);
        }
        return QuestMonsterDropRules.Draw(request, participant, definitions, held);
    }
}
