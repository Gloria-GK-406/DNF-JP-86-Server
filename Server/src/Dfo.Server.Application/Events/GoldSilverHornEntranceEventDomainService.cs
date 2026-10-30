using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

// Server configuration uses schedule 2332; A21 scheduled-dungeon projection uses dungeon 4123.
internal sealed class GoldSilverHornEntranceEventDomainService(IEventPersistence store, IGoldSilverHornRuleService rules) : EntranceEventDomainService(store)
{
    public override int EventId => rules.EventId;
    protected override string Key => "gold-silver-horn-entrance";
    protected override EventConfiguration Initial => rules.InitialActivity;
    protected override EventInfo[] Project(EventConfiguration c) => [Detailed(c, "")];
}
