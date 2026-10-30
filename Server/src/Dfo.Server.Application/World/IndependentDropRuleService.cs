using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>Cached independent rows, drawn against the level the caller read.</summary>
internal sealed class IndependentDropRuleService(
    IIndependentDropTableSource source,
    ILogger<IndependentDropRuleService> logger, IndependentDropOptions options) : IIndependentDropRuleService
{
    private sealed record Prepared(IndependentDropRow Row, ImmutableArray<int> Ends, bool BossEquipment);
    private sealed record Cache(IndependentDropTable Table,
        IReadOnlyDictionary<(IndependentDropSourceType, int), Prepared[]> Rows);
    private readonly Lazy<Cache> cache = new(() => Prepare(source.Load(), logger));

    public int Initialize()
    {
        var count = cache.Value.Table.Rows.Length;
        if (options.DebugGuaranteeGrowthBossEquipment)
            logger.LogWarning("Temporary acceptance override enabled: eligible growth-contract boss equipment pools have 100% hit probability");
        return count;
    }

    public bool Covers(MonsterDropRequest request) =>
        cache.Value.Rows.ContainsKey((request.Context.SourceType, request.MonsterCode));

    /// <remarks>Party trial counts are applied once, never once per member.</remarks>
    public IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request, int playerLevel, int growthBonusPercent = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(growthBonusPercent);
        if (request.PartyMemberCount is < 1 or > 4 || (int)request.Context.Difficulty > 4)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (!cache.Value.Rows.TryGetValue((request.Context.SourceType, request.MonsterCode), out var rows))
            return [];
        var result = new List<RolledDrop>();
        foreach (var prepared in rows)
        {
            var row = prepared.Row;
            if ((row.MinimumLevel > 0 && playerLevel < row.MinimumLevel)
                || (row.MaximumLevel > 0 && playerLevel > row.MaximumLevel)) continue;
            var random = new DropRandom(DropRandom.SeedFor(request.Context.Seed, request.Context.MapId,
                request.Context.Cell.X, request.Context.Cell.Y, request.ActorSequence)
                ^ 0x494E444550445250UL ^ ((ulong)(uint)row.Ordinal * 0x9E3779B97F4A7C15UL));
            var probability = row.Fields[3 + (int)request.Context.Difficulty];
            if (prepared.BossEquipment && request.Context.SourceType == IndependentDropSourceType.Monster
                && cache.Value.Table.GrowthBonusDungeonIds.Contains(request.Context.DungeonId)
                && request.MonsterRank == 3 && growthBonusPercent > 0)
                probability = GrowthProbability(probability, growthBonusPercent,
                    options.DebugGuaranteeGrowthBossEquipment);
            var count = IndependentDropRules.SuccessfulAttempts(row.Fields[7 + request.PartyMemberCount],
                row.Fields[12], probability, 1000000, random);
            for (var i = 0; i < count; i++)
            {
                var id = row.PoolMode == 0 ? row.ItemId : row.Choices[
                    IndependentDropRules.Select(prepared.Ends, random.Next(prepared.Ends[^1]) - 1)].ItemId;
                if (cache.Value.Table.Items[id] is { } item)
                    result.Add(item with { Count = 1 });
                else
                    logger.LogWarning("Independent drop row={Row} sourceType={SourceType} sourceId={SourceId} "
                        + "item={ItemId} selected with missing metadata; no item emitted and no reroll",
                        row.Ordinal, row.SourceType, row.SourceId, id);
            }
        }
        return result;
    }

    internal static int GrowthProbability(int probability, int bonusPercent, bool guarantee) =>
        guarantee ? 1000000 : (int)Math.Min(1000000L, (long)probability * (100L + bonusPercent) / 100);

    private static Cache Prepare(IndependentDropTable table, ILogger logger)
    {
        var prepared = new List<Prepared>();
        foreach (var row in table.Rows)
        {
            var ends = ImmutableArray.CreateBuilder<int>();
            var total = 0;
            foreach (var choice in row.Choices)
            {
                total = checked(total + choice.Weight);
                ends.Add(total);
            }
            if (row.PoolMode == 1 && total <= 0)
                throw new InvalidDataException($"Independent drop row {row.Ordinal}: empty weighted pool.");
            foreach (var id in row.PoolMode == 0 ? new[] { row.ItemId } : row.Choices.Select(c => c.ItemId))
            {
                if (!table.Items.TryGetValue(id, out var item))
                    throw new InvalidDataException($"Independent drop row {row.Ordinal}: uncached item {id}.");
                if (item is null)
                    logger.LogWarning("Independent drop row={Row} sourceType={SourceType} sourceId={SourceId} "
                        + "item={ItemId} has no PVF definition; rule and weight retained",
                        row.Ordinal, row.SourceType, row.SourceId, id);
            }
            var tagged = row.Choices.Count(c => table.BossEquipmentIds.Contains(c.ItemId));
            var bossEquipment = row.PoolMode == 1 && tagged == row.Choices.Length && tagged > 0;
            if (tagged > 0 && !bossEquipment)
                logger.LogWarning("Independent drop row={Row} mixes boss equipment with other items; growth bonus disabled", row.Ordinal);
            prepared.Add(new(row, ends.ToImmutable(), bossEquipment));
        }
        return new(table, prepared.GroupBy(p => ((IndependentDropSourceType)p.Row.SourceType, p.Row.SourceId))
            .ToDictionary(g => g.Key, g => g.ToArray()));
    }
}
