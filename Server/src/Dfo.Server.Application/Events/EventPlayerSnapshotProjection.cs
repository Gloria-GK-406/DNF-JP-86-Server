using Dfo.Server.Application.World;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Events;

internal static class EventPlayerSnapshotProjection
{
    public static EventPlayerSnapshot WithScheduledDungeons(EventPlayerSnapshot snapshot) => snapshot with
    {
        ScheduledDungeons = [new(TauKingdomRuleService.DungeonId,
            snapshot.Information.Any(e => e.EventId == TauKingdomRuleService.ActivityId))]
    };
}
