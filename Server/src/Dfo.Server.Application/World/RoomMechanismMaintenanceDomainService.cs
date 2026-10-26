using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.DomainEvents;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Application.World;
internal sealed class RoomMechanismMaintenanceDomainService(IDungeonRunDomainService runs, IDungeonRoomMechanismRuleService rules, IEventService events, ILogger<RoomMechanismMaintenanceDomainService> logger, TimeProvider clock)
    : IRoomMechanismMaintenanceDomainService
{
    private readonly Dictionary<(DungeonRunId, long), DungeonRoomMechanismState[]> delivered = [];
    public async ValueTask<DateTimeOffset?> AdvanceAndProjectAsync(DateTimeOffset now, CancellationToken token)
    {
        GameplayWriteAssertion.Writing(nameof(RoomMechanismMaintenanceDomainService) + ".AdvanceAndProjectAsync");
        var scan = Advance(now); var next = scan.Next;
        foreach (var view in scan.Views)
        {
            if (!NeedsProjection(view)) continue;
            try
            {
                await events.DirectAsync(new DungeonRoomMechanismsChanged(view), token).ConfigureAwait(false);
                MarkProjected(view);
                logger.LogInformation("Room mechanism state run={RunId} roomVersion={RoomVersion} states={States}", view.RunId, view.RoomVersion,
                    string.Join(',', view.States.Select(state => $"{state.Definition.Kind}:{state.Stage}:{state.Outcome}")));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is not OperationCanceledException && !DomainEventScopeCleanupException.Contains(error) && !TransactionOutcomeUncertainException.Contains(error))
            {
                GameplayJob.DemandHealthy();
                logger.LogWarning(error, "Room mechanism projection failed run={RunId}", view.RunId);
                var retry = clock.GetUtcNow().AddSeconds(1);
                if (next is null || retry < next) next = retry;
            }
        }
        return next;
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
