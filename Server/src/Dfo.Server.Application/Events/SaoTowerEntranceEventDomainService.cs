using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

// A21 filters [tower of despair sao] entrances unless EVENT_INFO contains the SAO activity.
// This service owns that activity switch only; tower admission, progress and rewards are separate work.
internal sealed class SaoTowerEntranceEventDomainService(IEventPersistence store, ISaoTowerRuleService rules) : EntranceEventDomainService(store)
{
    public override int EventId => rules.EventId;
    protected override string Key => "sao-tower-entrance";
    protected override EventConfiguration Initial => new(0, "initial", false, "艾恩葛朗特", "SAO塔", "SAO塔入口，玩法尚未实现。", "", "", null, null);
    protected override EventInfo[] Project(EventConfiguration c) => [Detailed(c, "sao aincrad event/0/0")];
}
