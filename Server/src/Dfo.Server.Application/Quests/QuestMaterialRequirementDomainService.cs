using System.Collections.Frozen;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Quests;

internal sealed class QuestMaterialRequirementDomainService(IQuestContentRuleService quests,
    ICharacterQuestLogDomainService logs, ICharacterContainerDomainService containers,
    IQuestStateRuleService? stateRules = null, IQuestFactsDomainService? facts = null) : IQuestMaterialRequirementDomainService
{
    private readonly IQuestStateRuleService rules = stateRules ?? new QuestStateRuleService(quests);
    public async ValueTask<IReadOnlySet<int>> GetOutstandingMaterialIdsAsync(AccountId accountId,
        CharacterId characterId, CancellationToken ct)
    {
        var log = logs.LogOf(accountId, characterId);
        var carried = await log.CarriedAsync(ct).ConfigureAwait(false);
        if (carried.Count == 0) return FrozenSet<int>.Empty;
        var holdings = facts is not null ? await facts.HoldingsAsync(characterId, ct).ConfigureAwait(false)
            : await QuestHoldingsReader.ReadAsync(containers, rules, characterId, 0, ct).ConfigureAwait(false);
        var states = await rules.EvaluateAsync(carried, await log.ClearedAsync(ct).ConfigureAwait(false), holdings, ct).ConfigureAwait(false);
        // Each quest's unmet quantity is independent; gold is not a ground-material target.
        return states.SelectMany(x => x.Items).Where(x => x.ItemId > 0 && x.Missing > 0)
            .Select(x => x.ItemId).ToFrozenSet();
    }
}
