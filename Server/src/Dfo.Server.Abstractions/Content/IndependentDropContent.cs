using System.Collections.Immutable;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Abstractions.Content;

public sealed record IndependentDropRow(int Ordinal, ImmutableArray<int> Fields,
    ImmutableArray<IndependentDropChoice> Choices)
{
    public int SourceType => Fields[0];
    public int SourceId => Fields[1];
    public int ItemId => Fields[2];
    public int MinimumLevel => Fields[13];
    public int MaximumLevel => Fields[14];
    // Semantics unresolved; retained but not used as a difficulty filter (#128).
    public int RawFilter => Fields[15];
    public int PoolMode => Fields[16];
}

public readonly record struct IndependentDropChoice(int ItemId, int Weight);
public sealed record IndependentDropTable(ImmutableArray<IndependentDropRow> Rows,
    ImmutableDictionary<int, RolledDrop?> Items)
{
    public ImmutableHashSet<int> BossEquipmentIds { get; init; } = [];
    // Conservative server policy: explicit special dungeon declarations are excluded.
    public ImmutableHashSet<int> GrowthBonusDungeonIds { get; init; } = [];
}

public sealed class IndependentDropOptions
{
    // Temporary acceptance aid. Only eligible growth-contract pools are forced to hit.
    public bool DebugGuaranteeGrowthBossEquipment { get; init; }
}
public interface IIndependentDropTableSource
{
    IndependentDropTable Load();
}

public enum IndependentDropSourceType { Monster = 0, APC = 1 }

public interface IIndependentDropRuleService
{
    int Initialize();
    ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(MonsterDropRequest request, CancellationToken cancellationToken,
        int growthBonusPercent = 0);
}
