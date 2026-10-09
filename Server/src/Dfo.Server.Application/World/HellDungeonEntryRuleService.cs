using System.Collections.Immutable;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.World;

internal sealed class HellDungeonEntryRuleService(IHellDungeonDefinitionSource source, IDungeonRoomActorSource roomActors) : IHellDungeonEntryRuleService
{
    public ValueTask<HellDungeonDefinition?> GetRulesAsync(int dungeonId, CancellationToken token) =>
        source.GetAsync(dungeonId, token);

    public IReadOnlyList<HellTicketAlternative> TicketAlternatives(HellDungeonDefinition rules, byte minimumLevel)
    {
        // Upstream admission convention, not a PVF-defined count. See Document/tasks/manual-hell-entry/implementation.md.
        var count = minimumLevel <= 44 ? 0 : (40 * minimumLevel - 1800) / 100 + 10;
        return count <= 0 ? [] : rules.FreePasses.Concat(rules.TicketIds.Select(id =>
            new HellTicketAlternative(id, count))).Where(x => x.ItemId > 0 && x.Count > 0).ToImmutableArray();
    }

    public async ValueTask<HellEntryPlan?> PrepareAsync(HellDungeonDefinition rules, DungeonLayout layout,
        int seed, CancellationToken token)
    {
        if (!rules.Enabled || !rules.Rooms.TryGetValue(layout.MazeIndex, out var room)
            || !layout.Cells.ContainsKey(room.Cell) || room.MapId <= 0) return null;
        var party = await source.GetPartyAsync(token).ConfigureAwait(false);
        var random = new Random(seed);
        var difficulties = party.ManualDifficulties.Where(x => x.Weight > 0).ToArray();
        if (difficulties.Length == 0) return null;
        var difficulty = Pick(difficulties, x => x.Weight, random).Difficulty;
        var groups = party.Groups.Where(x => x.Difficulty == difficulty && x.Actors.Count > 0)
            .ToDictionary(x => x.Index);
        var actors = ImmutableArray.CreateBuilder<HellEntryActor>();
        foreach (var wave in room.Groups.Where(x => x.Weight > 0 && groups.ContainsKey(x.GroupIndex))
                     .GroupBy(x => x.Order).OrderBy(x => x.Key))
        {
            var picked = Pick(wave.ToArray(), x => x.Weight, random);
            actors.AddRange(groups[picked.GroupIndex].Actors.Select(actor => new HellEntryActor(picked.Order,
                picked.GroupIndex, actor with { Level = layout.LevelOf(actor.Level) })));
        }
        var ordinaryCount = (await roomActors.GetAsync(room.MapId, token).ConfigureAwait(false)).Count;
        return actors.Count == 0 || ordinaryCount + actors.Count > byte.MaxValue ? null
            : new HellEntryPlan(difficulty, room.Cell, room.MapId, actors.ToImmutable());
    }

    private static T Pick<T>(IReadOnlyList<T> values, Func<T, int> weight, Random random)
    {
        var draw = random.NextInt64(values.Sum(x => (long)weight(x)));
        foreach (var value in values)
        {
            draw -= weight(value);
            if (draw < 0) return value;
        }
        throw new InvalidOperationException("Positive weighted choices must select a value.");
    }
}
