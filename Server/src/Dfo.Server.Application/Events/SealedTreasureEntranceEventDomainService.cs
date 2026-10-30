using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

// Server configuration uses schedule 2331; A21 scheduled-dungeon projection uses dungeon 3557.
internal sealed class SealedTreasureEntranceEventDomainService(IEventPersistence store, ISealedTreasureRuleService rules) : EntranceEventDomainService(store)
{
    public override int EventId => rules.EventId;
    protected override string Key => "sealed-treasure-entrance";
    protected override EventConfiguration Initial => rules.InitialActivity;
    protected override EventInfo[] Project(EventConfiguration c) => [Detailed(c, "")];
}
