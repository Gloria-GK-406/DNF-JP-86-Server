using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

// Server-only identity. It must never be projected as an unverified A21 EVENT_INFO identity.
internal sealed class UnlimitedFatigueEventDomainService(IEventPersistence store) : EntranceEventDomainService(store)
{
    internal const int ActivityId = 1_000_230;
    public override int EventId => ActivityId;
    protected override string Key => "unlimited-fatigue";
    protected override EventConfiguration Initial => new(0, "initial", false, "无限疲劳值", "免疲劳",
        "活动期间进图、换房及竞技地下城每轮不扣疲劳；零疲劳也可进入，结束后恢复扣除。", "", "", null, null);
    protected override EventInfo[] Project(EventConfiguration configuration) => [];
}

internal sealed class UnlimitedFatigueActivationSubscriber(IFatigueConsumptionControlDomainService fatigue)
    : IDomainEventSubscriber<ManagedEventActivationChanged>
{
    public ValueTask HandleAsync(DomainEventEnvelope<ManagedEventActivationChanged> message, CancellationToken ct) =>
        message.Payload.EventId == UnlimitedFatigueEventDomainService.ActivityId
            ? fatigue.SetSuspendedAsync(message.Payload.Active, ct) : ValueTask.CompletedTask;
}
