using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

// A21 checks 2215 for dungeon 3700 and 69/75 for the earlier generic entry gate.
internal sealed class TauKingdomEntranceEventDomainService(IEventPersistence store, ITauKingdomRuleService rules) : EntranceEventDomainService(store)
{
    public override int EventId => rules.EventId;
    protected override string Key => "tau-kingdom-entrance";
    protected override EventConfiguration Initial => rules.InitialActivity;
    protected override EventInfo[] Project(EventConfiguration c) => [Detailed(c, rules.ActivityLink),
        // The companion is projected with its owner; it has no independently editable GM configuration.
        new(69, "", "", "", "", "", c.StartsAt!.Value, c.EndsAt!.Value, "") { HasDetail = false }];
}
