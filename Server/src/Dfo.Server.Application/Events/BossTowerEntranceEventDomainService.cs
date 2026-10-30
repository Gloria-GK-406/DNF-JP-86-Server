using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

// Server configuration uses schedule 2329; A21 scheduled-dungeon projection uses dungeon 3505.
internal sealed class BossTowerEntranceEventDomainService(IEventPersistence store, IBossTowerRuleService rules) : EntranceEventDomainService(store)
{
    public override int EventId => rules.EventId;
    protected override string Key => "boss-tower-entrance";
    protected override EventConfiguration Initial => rules.InitialActivity;
    protected override EventInfo[] Project(EventConfiguration c) => [Detailed(c, "")];
}
