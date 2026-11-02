using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

// Identity 7 is the drop ratio slot of the 70 server (CItemDropRatioEvent); A21 is only known to list it.
internal sealed class DropRateEventDomainService(IEventPersistence store) : EntranceEventDomainService(store)
{
    internal const int ActivityId = 7;
    public override int EventId => ActivityId;
    protected override string Key => "drop-rate";
    protected override EventConfiguration Initial => new(0, "initial", false, "爆率提升", "爆率提升",
        "活动期间地面掉落的抽取次数增加。", "", "", null, null);
    protected override EventInfo[] Project(EventConfiguration configuration) => [Detailed(configuration, "")];

    protected override EventConfiguration Configure(EventConfiguration c, EventChange change)
    {
        if (change.DrawMultipliers is not { } values) throw new ArgumentException("请配置掉落倍率。");
        foreach (var (key, multiplier) in values)
            if (!DropDrawEntries.TryParse(key, out _) || !DropDrawMultipliers.IsValid(multiplier))
                throw new ArgumentException($"掉落倍率无效：{key}={multiplier}。");
        return c with { DrawMultipliers = values };
    }
}

/// <remarks>
/// Writes every entry to its target state, so reapplying is harmless and an entry dropped from the
/// configuration returns to one. Like the 70 server's EndEvent, the end of the activity restores one.
/// </remarks>
internal sealed class DropRateActivationSubscriber(IDropDrawMultiplierControlDomainService multipliers, IEventPersistence store)
    : IDomainEventSubscriber<ManagedEventActivationChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<ManagedEventActivationChanged> message, CancellationToken ct)
    {
        if (message.Payload.EventId != DropRateEventDomainService.ActivityId) return;
        var configured = message.Payload.Active
            ? (await store.ConfigurationAsync(DropRateEventDomainService.ActivityId, ct).ConfigureAwait(false))?.DrawMultipliers
            : null;
        foreach (var entry in DropDrawEntries.All)
        {
            var target = configured?.GetValueOrDefault(DropDrawEntries.KeyOf(entry), DropDrawMultipliers.Minimum) ?? DropDrawMultipliers.Minimum;
            await multipliers.SetAsync(entry, target, ct).ConfigureAwait(false);
        }
    }
}
