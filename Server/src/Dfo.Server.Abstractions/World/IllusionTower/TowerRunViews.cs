using System.Collections.Immutable;

namespace Dfo.Server.Abstractions.World.IllusionTower;

/// <summary>
/// What a run snapshot of either tower engine carries for the projections both towers share:
/// the party, its room and how the run ended. Engine-specific state stays on the concrete snapshot.
/// </summary>
public interface ITowerRunSnapshot
{
    Guid RunId { get; }
    int DungeonId { get; }
    ImmutableArray<TowerMember> Members { get; }
    TowerRoomSnapshot Room { get; }
    TowerCompletion? Completion { get; }
    DateTimeOffset StartedAt { get; }
}

/// <summary>What an action of either tower engine reports to the projections both towers share.</summary>
public interface ITowerActionOutcome
{
    bool Changed { get; }
    ITowerRunSnapshot? Run { get; }
    ImmutableArray<TowerKillReward> KillRewards { get; }
    bool SettlementCreated { get; }
}
