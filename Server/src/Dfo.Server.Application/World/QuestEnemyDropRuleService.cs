using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class QuestEnemyDropRuleService(
    IQuestContentRuleService quests,
    IPremiumServiceRuleService premiums,
    IItemPlacementRuleService placements) : IQuestEnemyDropRuleService
{
    public async ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(
        QuestEnemyDropRequest request, MonsterDropParticipant participant,
        IReadOnlyList<int> questIds, IReadOnlyDictionary<int, long> held, bool hasQuestHelper,
        CancellationToken cancellationToken)
    {
        var definitions = new List<QuestDefinition>();
        foreach (var questId in questIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await quests.FindAsync(questId, cancellationToken).ConfigureAwait(false) is { } definition)
                definitions.Add(definition);
        }
        QuestHelperDropAdjustment? adjustment = null;
        if (hasQuestHelper)
        {
            var eligible = new HashSet<int>();
            foreach (var itemId in definitions.SelectMany(q => q.EnemyRewardItems).Select(r => r.ItemId).Distinct())
            {
                var placement = await placements.PlacementForAsync(itemId, cancellationToken).ConfigureAwait(false);
                if (placement?.Destination.Space == BusinessInventorySpace.Quest) eligible.Add(itemId);
            }
            adjustment = new(eligible, premiums.QuestHelperMinimumDropRates);
        }
        return QuestMonsterDropRules.Draw(request, participant, definitions, held, adjustment);
    }
}
