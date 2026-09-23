using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
namespace Dfo.Server.Application.World;

/// <summary>Tagged Extra candidates only. Magic-sealed equipment selection is a separate route.</summary>
internal sealed class ExtraDropRuleService(
    IDropRateTableSource tables, IExtraDropContentSource source, DropRateOptions options) : IExtraDropRuleService
{
    private readonly Lazy<(DropRateTable Rates, ExtraDropContent Content)> cache = new(() =>
        (tables.Load(DropRateTables.MonsterExtra), source.Load()));

    public int Initialize() => cache.Value.Content.ItemCount;

    public IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request, int attempt)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);
        var (table, content) = cache.Value;
        if (content.ItemCount == 0 || request.MonsterRank is < 0 or > 3)
            return [];
        if (!content.Dungeons.TryGetValue(request.Context.DungeonId, out var dungeonType))
            throw new InvalidDataException($"Extra drops: unknown dungeon {request.Context.DungeonId}.");
        if (table.BandFor(request.MonsterLevel) is not { } band
            || table.GradeBandFor(request.MonsterLevel) is not { } grades)
            return [];
        var rates = DropRules.RatesFor(table, band, request.MonsterRank,
            request.Context.Difficulty, request.PartyMemberCount, options.RateBasis);
        // Extra has no gold path; do not consume an ordinary RNG stream.
        rates = rates with { Slots = rates.Slots.Where(s => s.Slot != DropRateSlot.Gold).ToArray() };
        var random = new DropRandom(DropRandom.SeedFor(request.Context.Seed, request.Context.MapId,
            request.Context.Cell.X, request.Context.Cell.Y, request.ActorSequence)
            ^ 0x455854524144524FUL ^ ((ulong)(uint)attempt * 0x9E3779B97F4A7C15UL));
        var result = new List<RolledDrop>();
        foreach (var slot in DropRules.Gate(rates, random))
        {
            var kind = DropRules.KindOf(slot);
            var rarity = DropRules.RollRarity(table, slot, random);
            if (content.Pools.TryGetValue((kind, (int)request.Context.Difficulty, dungeonType), out var pool)
                && DropRules.Pick(pool, grades, rarity, random) is { } item)
                result.Add(new(item.ItemId, 1, rarity, kind, item.Durability));
        }
        return result;
    }
}
