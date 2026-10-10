using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

public sealed record DimensionChroniclePool(IReadOnlyList<RolledDrop> Normal, IReadOnlyList<RolledDrop> Set);
public sealed record DimensionDungeonContent(IReadOnlySet<int> DungeonIds, int DailyEntries, int ExtraEntries,
    IReadOnlyDictionary<(int Job, int GrowType), DimensionChroniclePool> Pools, RolledDrop Fragment);
public interface IDimensionDungeonContentSource { DimensionDungeonContent Load(); }

public interface IDimensionDungeonRuleService
{
    int Initialize();
    bool IsDimension(int dungeonId);
    DimensionQuotaSnapshot Snapshot(DateOnly period, int used);
    IReadOnlyList<RolledDrop> DrawMonster(int dungeonId, int monsterId, int job, int growType, ulong seed);
    ClearRewardCardFace? DrawCard(int dungeonId, int job, int growType, ClearRewardCardFace ordinary, ulong seed);
}

public interface IDimensionDungeonDomainService
{
    ValueTask<DimensionQuotaSnapshot> ReadQuotaAsync(CharacterId character, CancellationToken ct);
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct);
}
