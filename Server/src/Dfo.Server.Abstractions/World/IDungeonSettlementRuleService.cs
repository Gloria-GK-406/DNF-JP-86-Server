using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>Confirmed completion and settlement distinctions for one frozen dungeon run.</summary>
/// <param name="CanComplete">
/// Whether the ordinary room rule and, for a quest-linked run, its frozen quest condition permit
/// the exit and result settlement.
/// </param>
/// <param name="QuestMaze">
/// Whether PLAY_RESULT identifies this run as a quest maze. Other presentation and card behavior
/// remains shared until A21 evidence establishes a further distinction.
/// </param>
public readonly record struct DungeonSettlementRules(bool CanComplete, bool QuestMaze);

public interface IDungeonSettlementRuleService
{
    ValueTask<DungeonSettlementRules> ForAsync(
        AccountId accountId,
        CharacterId characterId,
        DungeonRunState run,
        CancellationToken cancellationToken);
}
