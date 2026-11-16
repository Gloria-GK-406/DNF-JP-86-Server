using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>Confirmed completion and settlement distinctions for one frozen dungeon run.</summary>
/// <param name="CanComplete">
/// Whether the run has opened its clear after the boss room was verified empty. Quest progress
/// consumes that same clear event and cannot gate it.
/// </param>
/// <param name="QuestMaze">
/// Whether PLAY_RESULT identifies this run as a quest maze. Other presentation and card behavior
/// remains shared until A21 evidence establishes a further distinction.
/// </param>
public readonly record struct DungeonSettlementRules(bool CanComplete, bool QuestMaze);

/// <summary>The committed run facts a maze's clear conditions are measured against.</summary>
/// <param name="DestroyedObjectCodes">Every room object the run destroyed, once per destruction.</param>
/// <param name="KilledMonsterCodes">Every monster the run killed, once per kill.</param>
/// <param name="ClearedRoomMapIds">Map ids of the rooms the run cleared.</param>
public sealed record DungeonClearConditionFacts(
    IReadOnlyCollection<int> DestroyedObjectCodes,
    IReadOnlyCollection<int> KilledMonsterCodes,
    IReadOnlyCollection<int> ClearedRoomMapIds);

/// <summary>One committed monster kill as a boss room entrance condition sees it: what died, and where.</summary>
public readonly record struct DungeonEntranceKill(int Code, Dfo.Server.Abstractions.Content.MazeCell Cell);

public interface IDungeonSettlementRuleService
{
    /// <summary>
    /// Whether <paramref name="kill"/>, added to the run's earlier kills, hunts the last outstanding
    /// target of its boss room entrance condition.
    /// </summary>
    bool CompletesEntranceCondition(DungeonEntrancePlan plan, IReadOnlyCollection<DungeonEntranceKill> earlierKills,
        DungeonEntranceKill kill);

    /// <summary>Whether every target of the boss room entrance condition has been hunted.</summary>
    bool IsEntranceConditionMet(DungeonEntrancePlan plan, IReadOnlyCollection<DungeonEntranceKill> kills);

    /// <summary>
    /// Whether a maze's counted clear conditions are all met by these facts; false when it has none.
    /// </summary>
    bool AreClearConditionsMet(IReadOnlyList<Dfo.Server.Abstractions.Content.DungeonClearCondition> conditions,
        DungeonClearConditionFacts facts);

    ValueTask<DungeonSettlementRules> ForAsync(
        AccountId accountId,
        CharacterId characterId,
        DungeonRunState run,
        CancellationToken cancellationToken);
}

/// <summary>
/// Opens one exact shared run's clear exit when an authoritative runtime change has earned it.
/// </summary>
/// <remarks>
/// A write, not a question: the clear is opened in the run's runtime state, outside any domain
/// transaction, and the change names who saw it open.
/// </remarks>
public interface IDungeonClearDomainService
{
    ValueTask<DungeonClearChange?> TryOpenClearAsync(
        AccountId accountId,
        CharacterId characterId,
        DungeonRunId expectedRunId,
        long expectedRoomVersion,
        CancellationToken cancellationToken);
}
