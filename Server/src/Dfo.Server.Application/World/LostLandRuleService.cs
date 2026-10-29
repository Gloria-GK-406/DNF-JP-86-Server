using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class LostLandRuleService(ILostLandContentSource source,
    IDungeonStructureRuleService structures, LostLandRuntimePolicy policy) : ILostLandRuleService
{
    public bool Supports(int dungeonId) => dungeonId == 4000;
    public ValueTask<LostLandDefinition> DefinitionAsync(CancellationToken cancellationToken) => source.ReadAsync(cancellationToken);
    public async ValueTask<int> MaximumEntriesAsync(int consumedFatigue, CancellationToken cancellationToken)
    {
        if (consumedFatigue < 0) throw new InvalidDataException("Invalid consumed fatigue for LostLand.");
        var definition = await DefinitionAsync(cancellationToken).ConfigureAwait(false);
        return checked(definition.BaseEntryCount + consumedFatigue / 100);
    }

    public async ValueTask<DungeonLayout> LayoutAsync(DungeonEntryFacts facts, int seed, CancellationToken cancellationToken)
    {
        var definition = await DefinitionAsync(cancellationToken).ConfigureAwait(false);
        var topology = await structures.ResolveTopologyAsync(4000, facts, seed, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("LostLand has no eligible exploration maze.");
        // The PVF keeps a boss marker without a MAP. Preserve it for the minimap, while
        // refusing generation/movement there. It is not an ordinary boss encounter.
        if (topology.Cells.Any(pair => pair.Value.MapCandidates.Count == 0 && pair.Key != topology.BossCell)
            || topology.Cells[topology.StartCell].MapCandidates.Count == 0)
            throw new InvalidDataException("LostLand exploration rooms require explicit MAP bindings.");
        var limit = policy.LimitSecondsOverride ?? definition.LimitTimeParameters[0];
        if (limit is <= 0 or > 86400) throw new InvalidDataException("Invalid LostLand exploration limit.");
        var plan = new LostLandEntryPlan(limit, definition.LimitTimeParameters[1],
            TimeSpan.FromMilliseconds(definition.FirstPeriodMilliseconds),
            TimeSpan.FromMilliseconds(definition.SecondPeriodMilliseconds), definition.WarpConditions, definition.Patrols)
        {
            FeverDuration = TimeSpan.FromSeconds(definition.FeverParameters[0]),
        };
        return await structures.PrepareLayoutAsync(topology with
        {
            Mode = new DungeonRunMode.LostLand(plan), ConsumesFatigue = false, NoGiveupPenalty = true,
        }, cancellationToken).ConfigureAwait(false);
    }
}
