using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class QuestMonsterDropRuleService(
    IQuestContentRuleService quests) : IQuestMonsterDropRuleService
{
    public async ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(
        MonsterDropRequest request, MonsterDropParticipant participant,
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
        var result = new List<RolledDrop>();
        result.AddRange(QuestMonsterDropRules.DrawMonsterRewards(request, participant, definitions, held));
        result.AddRange(QuestMonsterDropRules.DrawEnemyRewards(request, participant, definitions, held));
        return result;
    }
}
