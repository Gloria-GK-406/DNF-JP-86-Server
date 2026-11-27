using Dfo.Server.Abstractions.Events;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

internal sealed class GuildRedPacketEntranceEventDomainService(IEventPersistence store, IEventPeriodRuleService periods)
    : EntranceEventDomainService(store, periods)
{
    public override int EventId => 2378;
    protected override string Key => "guild-red-packet-entrance";
    protected override EventConfiguration Initial => new(0, "initial", false, "公会红包", "公会红包",
        "在公会窗口购买、查看和领取红包。", "", "", null, null);

    // A21 looks up the event identity when it opens the guild window.
    protected override EventInfo[] Project(EventConfiguration c) => [Detailed(c, "") with { HasDetail = false }];
}
