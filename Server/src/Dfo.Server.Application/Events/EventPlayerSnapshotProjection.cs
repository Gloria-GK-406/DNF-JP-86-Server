using Dfo.Server.Application.World;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

internal static class EventPlayerSnapshotProjection
{
    public static EventPlayerSnapshot WithScheduledDungeons(EventPlayerSnapshot snapshot) => snapshot with
    {
        ScheduledDungeons = [new(SouthernDaleRuleService.DungeonId,
            snapshot.Information.Any(e => e.EventId == SouthernDaleRuleService.ActivityId)),
            new(TauKingdomRuleService.DungeonId,
            snapshot.Information.Any(e => e.EventId == TauKingdomRuleService.ActivityId)),
            new(HerasRuleService.DungeonId, snapshot.Information.Any(e => e.EventId == HerasRuleService.ActivityId)),
            new(SealedTreasureRuleService.DungeonId, snapshot.Information.Any(e => e.EventId == SealedTreasureRuleService.ActivityId))],
        ScheduledQuests = [new(Dfo.Server.Abstractions.World.ISouthernDaleQuestDomainService.QuestId,
            snapshot.Information.Any(e => e.EventId == SouthernDaleRuleService.ActivityId))]
    };
}
