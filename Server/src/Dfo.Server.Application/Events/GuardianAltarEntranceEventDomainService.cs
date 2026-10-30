using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

// Managed event 119 owns enablement; A21 entrance projection uses common schedule category 4, content 10.
internal sealed class GuardianAltarEntranceEventDomainService(IEventPersistence store, IGuardianAltarRuleService rules) : EntranceEventDomainService(store)
{
    public override int EventId => rules.EventId;
    protected override string Key => "guardian-altar-entrance";
    protected override EventConfiguration Initial => rules.InitialActivity;
    protected override EventInfo[] Project(EventConfiguration c) => [Detailed(c, "")];
}
