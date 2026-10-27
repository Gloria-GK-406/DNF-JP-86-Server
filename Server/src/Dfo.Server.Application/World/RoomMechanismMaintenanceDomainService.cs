using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.DomainEvents;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Application.World;
internal sealed class RoomMechanismMaintenanceDomainService(IDungeonRunDomainService runs, IDungeonRoomMechanismRuleService rules, IEventService events, ILogger<RoomMechanismMaintenanceDomainService> logger)
    : IRoomMechanismMaintenanceDomainService
{
    private readonly Dictionary<(DungeonRunId, long), DungeonRoomMechanismState[]> delivered = [];
    public async ValueTask ProjectAsync(DungeonRoomMechanismView view, CancellationToken token)
    {
        GameplayWriteAssertion.Writing(nameof(RoomMechanismMaintenanceDomainService) + ".ProjectAsync");
        var current = runs.Find(view.RunId);
        if (current is null || current.RoomVersion != view.RoomVersion || current.CurrentCell != view.Cell
            || !current.CurrentCellState.Mechanisms.SequenceEqual(view.States) || !NeedsProjection(view)) return;
        await events.DirectAsync(new DungeonRoomMechanismsChanged(view), token).ConfigureAwait(false);
        MarkProjected(view);
        logger.LogInformation("Room mechanism state run={RunId} roomVersion={RoomVersion} states={States}", view.RunId, view.RoomVersion,
            string.Join(',', view.States.Select(state => $"{state.Definition.Kind}:{state.Stage}:{state.Outcome}")));
    }
    public RoomMechanismMaintenanceScan Advance(DateTimeOffset now)
    {
        GameplayWriteAssertion.Writing(nameof(RoomMechanismMaintenanceDomainService) + ".Advance");
        var views = runs.AdvanceRoomMechanisms(now);
        var active = views.Select(view => (view.RunId, view.RoomVersion)).ToHashSet();
        foreach (var key in delivered.Keys.Where(key => !active.Contains(key)).ToArray()) delivered.Remove(key);
        DateTimeOffset? next = null;
        foreach (var view in views)
            foreach (var state in view.States)
                if (rules.NextDeadline(state) is {} deadline && (next is null || deadline < next)) next = deadline;
        return new(views, next);
    }
    public bool NeedsProjection(DungeonRoomMechanismView view) =>
        !delivered.TryGetValue((view.RunId, view.RoomVersion), out var previous) || !previous.SequenceEqual(view.States);
    public void MarkProjected(DungeonRoomMechanismView view)
    {
        GameplayWriteAssertion.Writing(nameof(RoomMechanismMaintenanceDomainService) + ".MarkProjected");
        delivered[(view.RunId, view.RoomVersion)] = view.States.ToArray();
    }
}
