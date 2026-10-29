using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.UsageQuotas;

namespace Dfo.Server.Application.World;

internal sealed class LostLandRuleService(ILostLandContentSource source,
    IDungeonStructureRuleService structures, LostLandRuntimePolicy policy) : ILostLandRuleService
{
    public bool Supports(int dungeonId) => dungeonId == 4000;
    public ValueTask<LostLandDefinition> DefinitionAsync(CancellationToken cancellationToken) => source.ReadAsync(cancellationToken);
    public async ValueTask<LostLandQuotaDecision> QuotaAsync(DateOnly period, int consumedFatigue, int used,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (consumedFatigue < 0 || used < 0) throw new InvalidDataException("Invalid LostLand entry counters.");
        // The virtual acceptance view never changes or retroactively charges the saved ledger.
        if (policy.UnlimitedEntriesForTesting) return new(new(period, 1, consumedFatigue, 0), null);
        var definition = await DefinitionAsync(cancellationToken).ConfigureAwait(false);
        var maximum = checked(definition.BaseEntryCount + consumedFatigue / 100);
        return new(new(period, maximum, consumedFatigue, used),
            new(UsageQuotaFamily.LostLand, period, maximum, 4000));
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
