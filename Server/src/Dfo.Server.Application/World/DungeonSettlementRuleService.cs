using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

internal sealed class DungeonSettlementRuleService(ICharacterQuestLogDomainService questLogs)
    : IDungeonSettlementRuleService
{
    public async ValueTask<DungeonSettlementRules> ForAsync(
        AccountId accountId,
        CharacterId characterId,
        DungeonRunState run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var purpose = run.Layout.Purpose;
        if (!purpose.IsQuestLinked)
            return new DungeonSettlementRules(CanComplete: true, QuestMaze: false);

        if (purpose.QuestId is not { } questId || purpose.QuestState is not { } state)
            return new DungeonSettlementRules(CanComplete: false, QuestMaze: true);

        var log = questLogs.LogOf(accountId, characterId);
        var canComplete = state switch
        {
            DungeonQuestConnectionState.Active =>
                (await log.CarriedAsync(cancellationToken).ConfigureAwait(false))
                    .Any(quest => quest.QuestId == questId && quest.TriggerValue == 0),
            DungeonQuestConnectionState.Cleared =>
                (await log.ClearedAsync(cancellationToken).ConfigureAwait(false))
                    .Any(quest => quest.QuestId == questId),
            _ => false,
        };

        return new DungeonSettlementRules(canComplete, QuestMaze: true);
    }
}
