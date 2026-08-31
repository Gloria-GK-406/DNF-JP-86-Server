using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>What a character can take off the floor of the room they are standing in.</summary>
internal sealed class GroundPickupDomainService(
    IDungeonRoomRuleService rooms,
    IItemPlacementRuleService placementRules) : IGroundPickupDomainService
{
    public async ValueTask<GroundPiece?> FindOnFloorAsync(
        DungeonRunState run, ushort groundDropNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        var remaining = await rooms.RemainingGroundDropsAsync(run, cancellationToken)
            .ConfigureAwait(false);
        if (remaining.FirstOrDefault(candidate => candidate.Number == groundDropNumber) is { } room)
        {
            return new GroundPiece(
                room.Drop.ItemId,
                Math.Max(1, room.Drop.Count),
                room.Drop.Durability,
                await this.IsEquipmentAsync(room.Drop.ItemId, cancellationToken)
                    .ConfigureAwait(false),
                room.Drop.ObjectIndex,
                null);
        }

        return run.FindMonsterDrop(groundDropNumber) is { } dropped
            ? new GroundPiece(
                dropped.Drop.ItemId,
                Math.Max(1, dropped.Drop.Count),
                dropped.Drop.Rolled.Durability,
                await this.IsEquipmentAsync(dropped.Drop.ItemId, cancellationToken)
                    .ConfigureAwait(false),
                null,
                dropped.ActorSequence)
            : null;
    }

    private ValueTask<bool> IsEquipmentAsync(int itemId, CancellationToken cancellationToken) =>
        placementRules.IsEquipmentAsync(itemId, cancellationToken);
}
