using System.Collections.Immutable;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Events;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.GameplayParameters;

/// <summary>Interprets parameter sources inside the neutral query component.</summary>
internal sealed class GameplayParameterRuleService(IEventPeriodRuleService periods)
{
    public int FatigueSourceId => UnlimitedFatigueEventDomainService.ActivityId;
    public int DropSourceId => DropRateEventDomainService.ActivityId;

    public bool ShouldConsume(EventConfiguration? configuration, DateTimeOffset at) =>
        !periods.IsActive(configuration, at);

    public DropDrawMultipliers ComposeDropMultipliers(ImmutableArray<DropDrawMultiplierBenefit> effects) =>
        effects.Length switch
        {
            0 => DropDrawMultipliers.Default,
            1 => effects[0].Multipliers,
            _ => new(DropDrawEntries.All.ToDictionary(entry => entry,
                entry => effects.Aggregate(DropDrawMultipliers.Minimum, (result, effect) =>
                    Math.Min(DropDrawMultipliers.Maximum, result * effect.Multipliers.For(entry))))),
        };

    public DropDrawMultipliers Multipliers(EventConfiguration? configuration, DateTimeOffset at)
    {
        if (!periods.IsActive(configuration, at)) return DropDrawMultipliers.Default;
        var values = new Dictionary<DropDrawEntry, int>();
        foreach (var entry in DropDrawEntries.All)
        {
            var multiplier = configuration!.DrawMultipliers?.GetValueOrDefault(
                DropDrawEntries.KeyOf(entry), DropDrawMultipliers.Minimum) ?? DropDrawMultipliers.Minimum;
            if (!DropDrawMultipliers.IsValid(multiplier))
                throw new InvalidDataException($"Invalid configured draw multiplier for {entry}: {multiplier}.");
            values.Add(entry, multiplier);
        }
        return new(values);
    }
}
