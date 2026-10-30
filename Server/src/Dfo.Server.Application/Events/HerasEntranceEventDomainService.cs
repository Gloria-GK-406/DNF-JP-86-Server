using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

// Heras has no confirmed detail-string parser; project only its event identity.
internal sealed class HerasEntranceEventDomainService(IEventPersistence store, IHerasRuleService rules) : EntranceEventDomainService(store)
{
    public override int EventId => rules.EventId;
    protected override string Key => "heras-entrance";
    protected override EventConfiguration Initial => rules.InitialActivity;
    protected override EventInfo[] Project(EventConfiguration c) => [new(EventId, "", "", "", "", "", c.StartsAt!.Value, c.EndsAt!.Value, "") { HasDetail = false }];
}
