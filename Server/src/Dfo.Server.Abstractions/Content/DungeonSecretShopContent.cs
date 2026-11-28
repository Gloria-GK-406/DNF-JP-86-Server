using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Content;

public sealed record DungeonSecretShopLevel(int Id, int Minimum, int Maximum);
public sealed record DungeonSecretShopNpcWeight(int Group, int NpcId, IReadOnlyList<int> Weights);
public sealed record DungeonSecretShopCandidate(DungeonSecretShopItem Item, int Weight);
public sealed record DungeonSecretShopPool(int NpcId, int Group, bool IsDungeon, int DrawCount,
    IReadOnlyList<DungeonSecretShopCandidate> Candidates);

public sealed record DungeonSecretShopCatalog(
    IReadOnlyList<DungeonSecretShopLevel> Levels,
    IReadOnlyList<DungeonSecretShopNpcWeight> DungeonWeights,
    IReadOnlyList<DungeonSecretShopNpcWeight> LevelWeights,
    IReadOnlyList<DungeonSecretShopPool> Pools)
{
    public static DungeonSecretShopCatalog Empty { get; } = new([], [], [], []);
}

public interface IDungeonSecretShopContentSource
{
    DungeonSecretShopCatalog Read();
}
