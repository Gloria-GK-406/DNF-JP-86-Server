using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Quests;

internal sealed class GiantDragonCrystalExchangeDomainService(IUsageQuotaDomainService quotas,
    IDailyResetRuleService reset, TimeProvider clock, IEventService events)
    : IGiantDragonCrystalExchangeDomainService
{
    public async ValueTask<bool> CanAcceptAsync(CharacterId character, CancellationToken ct) =>
        (await quotas.ReadAsync(character, UsageQuotaKey.GiantDragonCrystalExchange(reset.PeriodStart(clock.GetUtcNow())), ct)
            .ConfigureAwait(false))?.UsedCount is not > 0;

    public async ValueTask<bool> TryCompleteAsync(CharacterId character, CancellationToken ct)
    {
        var period = reset.PeriodStart(clock.GetUtcNow());
        var previous = await quotas.ReadAsync(character, UsageQuotaKey.GiantDragonCrystalExchange(period), ct).ConfigureAwait(false);
        if (previous?.UsedCount > 0) return false;
        await events.DirectAsync(new GiantDragonCrystalExchangeRecorded(character, period, previous), ct).ConfigureAwait(false);
        return true;
    }
}
