using System.Collections.Immutable;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;

namespace Dfo.Server.Abstractions.World;

public readonly record struct TowerDepartureEntryId(Guid Value);
public interface IBossTowerDepartureProjectionDomainService
{
    ImmutableArray<TowerDepartureEntryId> SnapshotPending();
    bool TryClaim(TowerDepartureEntryId expected, out BossTowerActionResult? action);
}
public interface IIllusionTowerDepartureProjectionDomainService
{
    ImmutableArray<TowerDepartureEntryId> SnapshotPending();
    bool TryClaim(TowerDepartureEntryId expected, out TowerActionResult? action);
}
