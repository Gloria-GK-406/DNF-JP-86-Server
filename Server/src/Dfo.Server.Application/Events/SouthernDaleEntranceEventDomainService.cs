using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

// Southern Dale shares the native 69/75 gate with Tau Kingdom.
internal sealed class SouthernDaleEntranceEventDomainService(IEventPersistence store, ISouthernDaleRuleService rules) : EntranceEventDomainService(store)
{
    public override int EventId => rules.EventId;
    protected override string Key => "southern-dale-entrance";
    protected override EventConfiguration Initial => rules.InitialActivity;
    protected override EventInfo[] Project(EventConfiguration c) => [Detailed(c, rules.ActivityLink),
        // The companion is projected with its owner; it has no independently editable GM configuration.
        new(69, "", "", "", "", "", c.StartsAt!.Value, c.EndsAt!.Value, "") { HasDetail = false }];
}
