using System.Collections.Frozen;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class WorldDropRuleService(IWorldDropTableSource source, WorldDropOptions options) : IWorldDropRuleService
{
    private readonly Lock gate = new();
    private FrozenDictionary<int, WorldDropDistribution>? levels;

    public int Initialize()
    {
        lock (gate)
        {
            if (levels is not null) return levels.Count;
            if (options.ProbabilityBasis <= 0) throw new InvalidDataException("WorldDrops:ProbabilityBasis must be positive.");
            var compiled = source.Load().ToDictionary(row => row.Level,
                row => WorldDropDistribution.Compile(row, options.ProbabilityBasis));
            Volatile.Write(ref levels, compiled.ToFrozenDictionary());
            return compiled.Count;
        }
    }

    public IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request)
    {
        var snapshot = Volatile.Read(ref levels)
            ?? throw new InvalidOperationException("World drop cache has not been initialized.");
        if (!snapshot.TryGetValue(request.MonsterLevel, out var distribution)) return [];
        var context = request.Context;
        var random = new DropRandom(DropRandom.SeedFor(context.Seed, context.MapId,
            context.Cell.X, context.Cell.Y, request.ActorSequence) ^ 0x574F_524C_4444_524FUL);
        var entry = distribution.Select(random.Next(options.ProbabilityBasis) - 1);
        return entry is null ? [] : [new RolledDrop(entry.ItemId, 1, entry.Rarity, entry.Kind, entry.Durability)];
    }
}

internal sealed record WorldDropDistribution(ImmutableArray<WorldDropEntry> Entries,
    ImmutableArray<int> Ends, int Basis)
{
    public static WorldDropDistribution Compile(WorldDropLevel row, int basis)
    {
        if (basis <= 0) throw new InvalidDataException("World drop probability basis must be positive.");
        var entries = ImmutableArray.CreateBuilder<WorldDropEntry>();
        var ends = ImmutableArray.CreateBuilder<int>();
        long total = 0;
        foreach (var entry in row.Entries)
        {
            if (entry.ItemId <= 0 || entry.Weight < 0)
                throw new InvalidDataException($"World drop level {row.Level}: invalid item/weight.");
            if (entry.Weight == 0) continue;
            total += entry.Weight;
            if (total > basis)
                throw new InvalidDataException($"World drop level {row.Level}: cumulative weight {total} exceeds basis {basis}.");
            entries.Add(entry);
            ends.Add((int)total);
        }
        return new(entries.ToImmutable(), ends.ToImmutable(), basis);
    }

    public WorldDropEntry? Select(int ticket)
    {
        if (ticket < 0 || ticket >= Basis) throw new ArgumentOutOfRangeException(nameof(ticket));
        var low = 0;
        var high = Ends.Length;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (ticket < Ends[mid]) high = mid;
            else low = mid + 1;
        }
        return low == Entries.Length ? null : Entries[low];
    }
}
