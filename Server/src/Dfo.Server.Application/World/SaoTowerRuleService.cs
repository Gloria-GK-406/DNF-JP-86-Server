using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class SaoTowerRuleService(ISaoTowerContentSource source) : ISaoTowerRuleService
{
    public SaoTowerCatalog Catalog => source.Load();
    // A21's known SAO resource namespace is a cheap prefilter; the catalog remains authoritative.
    public bool Supports(int dungeonId) => dungeonId is >= 3600 and <= 3699
        && Catalog.Floors.Any(x => x.DungeonId == dungeonId);
    public SaoTowerFloor Floor(int floor) => Catalog.Floors.Single(x => x.Floor == floor);
}
