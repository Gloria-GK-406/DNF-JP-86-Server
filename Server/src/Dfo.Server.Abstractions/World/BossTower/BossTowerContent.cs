using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Abstractions.World.BossTower;

public sealed record BossTowerMapCandidate(int Weight, TowerRoomDefinition Room, IReadOnlyList<int> NpcIds);
public sealed record BossTowerStageDefinition(int Stage, IReadOnlyList<BossTowerMapCandidate> Maps);
public sealed record BossTowerDefinition(DungeonAdmissionDefinition Admission, int BasisLevel,
    IReadOnlyList<BossTowerStageDefinition> Stages, IllusionTowerRewardParameters Rewards, int MaximumClearItemCount)
{
    public IReadOnlySet<int> AutoRootItemIds { get; init; } = new HashSet<int>();
}
public sealed record BossTowerRoomSeed(TowerRoomSeed Combat, IReadOnlyList<int> NpcIds);
public interface IBossTowerContentSource
{
    ValueTask<BossTowerDefinition> GetAsync(CancellationToken ct);
}
public interface IBossTowerRuleService
{
    bool Supports(int dungeonId);
    int EventId { get; }
    int TicketItemId { get; }
    int PartyLimit { get; }
    TimeSpan LoadingTimeout { get; }
    TimeSpan ResultTimeout { get; }
    bool CanTrade(int npcId, int shopId);
    EventConfiguration InitialActivity { get; }
    ValueTask<BossTowerDefinition> DefinitionAsync(CancellationToken ct);
    ValueTask<BossTowerRoomSeed> CreateRoomAsync(int stage, uint seed, CancellationToken ct);
}
