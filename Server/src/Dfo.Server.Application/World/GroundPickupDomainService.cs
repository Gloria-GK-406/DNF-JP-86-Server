using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>What a character can take off the floor of the room they are standing in.</summary>
internal sealed class GroundPickupDomainService : IGroundPickupDomainService
{
    public ValueTask<GroundPiece?> FindOnFloorAsync(
        DungeonRunState run, ushort groundDropNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            run.CurrentCellState.FindGroundItem(groundDropNumber)?.ToGroundPiece());
    }
}
