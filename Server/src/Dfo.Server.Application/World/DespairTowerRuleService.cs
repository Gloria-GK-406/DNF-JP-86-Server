using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class DespairTowerRuleService(IDespairTowerContentSource content,
    IClearRewardRateSource rates, IDropItemPoolSource pools) : IDespairTowerRuleService
{
    public DespairTowerCatalog Catalog => content.Load();
    public bool IsTower(int dungeonId) => Catalog.Floors.Any(x => x.DungeonId == dungeonId);
    public DespairTowerFloor? NextFloor(DespairTowerProgress progress) => Catalog.Floors.SingleOrDefault(x =>
        x.Floor == (progress.HighestClearedFloor == Catalog.Floors.Count ? 1 : progress.HighestClearedFloor + 1));
    public IReadOnlyList<DespairTowerReward> Rewards(int floor, int characterLevel, int basisLevel, ulong seed)
    {
        // UpstreamReference: tower draws item candidates directly, without the ordinary card-appearance/map-count gate.
        // CurrentImplementation reuses PVF rarity, grade and creation weights with the server PRNG.
        var mirror = floor is >= 10 and <= 90 && floor % 10 == 0;
        var table = rates.Load(DropRateTables.ClearReward);
        if (table.Rarity is not { } rarity || table.GradeBandFor(basisLevel) is not { } grades)
            throw new InvalidDataException("Despair reward rarity or grade table is incomplete.");
        var pool = pools.Load(DropItemFamily.Equipment);
        var random = new DropRandom(seed);
        var output = new List<DespairTowerReward>();
        for (var i = 0; i < (mirror ? 9 : 5); i++)
        {
            var roll = random.Next(10000);
            var type = 0;
            for (; type < table.ItemTypeRates.Count; type++)
            {
                roll -= table.ItemTypeRates[type];
                if (roll <= 0) break;
            }
            var quality = (DropRarity)rarity.BandOf(random.Next(rarity.HighestRoll));
            // Avatar rewards need their own eligible pool; the ordinary equipment branch is available.
            if (type == 1 && DropRules.Pick(pool, grades, quality, random) is { } item)
                output.Add(new(item.ItemId, 1, item.Durability));
        }
        if (mirror) output.Add(new(1252, 1));
        if (floor == Catalog.Floors.Count) output.Add(new(3314, 1));
        return output;
    }
}
