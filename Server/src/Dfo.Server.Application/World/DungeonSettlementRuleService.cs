using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

internal sealed class DungeonSettlementRuleService
    : IDungeonSettlementRuleService
{
    public bool AreClearConditionsMet(IReadOnlyList<Dfo.Server.Abstractions.Content.DungeonClearCondition> conditions,
        DungeonClearConditionFacts facts) =>
        DungeonClearConditionRules.AreMet(conditions, facts);

    public ValueTask<DungeonSettlementRules> ForAsync(
        AccountId accountId,
        CharacterId characterId,
        DungeonRunState run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        return ValueTask.FromResult(new DungeonSettlementRules(
            run.ClearOpen, run.Layout.Purpose.IsQuestLinked));
    }
}
