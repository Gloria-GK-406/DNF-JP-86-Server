using Dfo.Server.Abstractions.Benefits;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Benefits;

namespace Dfo.Server.Application.GameplayParameters;

internal readonly record struct GlobalDropBenefitContext;

internal sealed class DropDrawMultiplierBenefits(IEventPersistence sources, GameplayParameterRuleService rules)
    : IGameplayBenefitSource<GlobalDropBenefitContext>
{
    public async ValueTask<GameplayBenefitBatch> DescribeAsync(
        GlobalDropBenefitContext context, DateTimeOffset at, CancellationToken ct)
    {
        var configuration = await sources.ConfigurationAsync(rules.DropSourceId, ct).ConfigureAwait(false);
        var multipliers = rules.Multipliers(configuration, at);
        return new("drop-rate-event", GameplayBenefitScope.Global,
            ReferenceEquals(multipliers, DropDrawMultipliers.Default) ? []
                : [new("draw-multipliers", "drop rate event", 0, new DropDrawMultiplierBenefit(multipliers),
                    configuration?.StartsAt, configuration?.EndsAt)]);
    }
}

internal sealed class DropDrawMultiplierDomainService(GameplayBenefitsQuery<GlobalDropBenefitContext> benefits)
    : IDropDrawMultiplierDomainService
{
    public async ValueTask<DropDrawMultipliers> CurrentAsync(CancellationToken ct)
    {
        var declared = await benefits.ReadAsync(new(), ct, GameplayBenefitScope.Global).ConfigureAwait(false);
        var multipliers = declared.Effects<DropDrawMultiplierBenefit>().Select(effect => effect.Multipliers).ToArray();
        return multipliers.Length switch
        {
            0 => DropDrawMultipliers.Default,
            1 => multipliers[0],
            _ => new(DropDrawEntries.All.ToDictionary(entry => entry, entry => multipliers.Aggregate(1,
                (result, multiplier) => Math.Min(DropDrawMultipliers.Maximum, result * multiplier.For(entry))))),
        };
    }
}
