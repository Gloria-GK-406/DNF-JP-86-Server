using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Events;

namespace Dfo.Server.Application.GameplayParameters;

/// <summary>Read-through gameplay parameters. No derived state is stored or reconciled.</summary>
internal sealed class GameplayParametersDomainService(IEventPersistence sources,
    GameplayParameterRuleService rules, TimeProvider clock)
    : IFatigueConsumptionQuery
{
    public async ValueTask<bool> ShouldConsumeAsync(CancellationToken cancellationToken)
    {
        var at = clock.GetUtcNow();
        var configuration = await sources.ConfigurationAsync(rules.FatigueSourceId, cancellationToken).ConfigureAwait(false);
        return rules.ShouldConsume(configuration, at);
    }

}
