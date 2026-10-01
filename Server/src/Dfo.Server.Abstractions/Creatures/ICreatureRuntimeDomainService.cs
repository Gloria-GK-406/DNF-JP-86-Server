using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Creatures;

public sealed record CreaturePendingProjection(SelectedSessionIdentity Session, IReadOnlyList<CreatureRuntimeChange> Changes);

public sealed record CreatureRuntimeChange(CreatureState Before, CreatureState After, bool Equipped)
{
    public CreatureTransformationReceipt? Transformation { get; init; }
}

/// <summary>Online creature clocks and actions. Durable actions join the current use case.</summary>
public interface ICreatureRuntimeDomainService
{
    ValueTask<CreatureTransformationReceipt?> HatchAsync(SelectedSessionIdentity session,
        BusinessSlot eggSlot, CancellationToken cancellationToken);
    void Enter(SelectedSessionIdentity session);
    void DiscardExact(SelectedSessionIdentity session);
    void QueueRoomExperience(SelectedSessionIdentity session, DungeonRunId runId, MazeCell cell, int amount);
    IReadOnlyList<CreaturePendingProjection> DrainProjections();
    bool IsEquipped(SelectedSessionIdentity session, int itemUid);
    ValueTask LeaveAsync(SelectedSessionIdentity session, CancellationToken cancellationToken);
    IReadOnlyList<SelectedSessionIdentity> DueSessions();
    ValueTask<IReadOnlyList<CreatureRuntimeChange>> AdvanceAsync(SelectedSessionIdentity session,
        CancellationToken cancellationToken, bool? combat = null);
    ValueTask<CreatureRuntimeChange?> GainExperienceAsync(SelectedSessionIdentity session, int amount,
        CancellationToken cancellationToken);
    ValueTask<CreatureRuntimeChange?> GainRoomExperienceAsync(SelectedSessionIdentity session,
        DungeonRunId runId, MazeCell cell, int amount, CancellationToken cancellationToken);
    void Invalidate(CharacterId characterId);
    void ChangeCombat(CharacterId characterId, bool combat);
}
