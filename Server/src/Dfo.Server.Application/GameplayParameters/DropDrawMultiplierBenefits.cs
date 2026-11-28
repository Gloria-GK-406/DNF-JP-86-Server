using System.Collections.Immutable;
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

internal sealed class DropDrawMultiplierDomainService(
    GameplayBenefitsQuery<GlobalDropBenefitContext> benefits, GameplayParameterRuleService rules)
    : IDropDrawMultiplierDomainService
{
    public async ValueTask<DropDrawMultipliers> CurrentAsync(CancellationToken ct)
    {
        var declared = await benefits.ReadAsync(new(), ct, GameplayBenefitScope.Global).ConfigureAwait(false);
        return rules.ComposeDropMultipliers(declared.Effects<DropDrawMultiplierBenefit>().ToImmutableArray());
    }
}
