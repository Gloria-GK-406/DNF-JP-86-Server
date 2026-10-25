using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;

namespace Dfo.Server.Protocol.Game;

public sealed class IllusionTowerDepartureProjectionQueue : IIllusionTowerDepartureProjectionDomainService
{
    private sealed record Entry(TowerDepartureEntryId Id, TowerActionResult Action);
    private readonly ConcurrentQueue<Entry> pending = new();
    public void Enqueue(TowerActionResult action)
    {
        GameplayWriteAssertion.Writing(nameof(IllusionTowerDepartureProjectionQueue) + "." + nameof(Enqueue));
        pending.Enqueue(new(new(Guid.NewGuid()), action));
    }
    public ImmutableArray<TowerDepartureEntryId> SnapshotPending()
    {
        GameplayJob.DemandHealthy();
        return pending.Select(entry => entry.Id).ToImmutableArray();
    }
    public bool TryClaim(TowerDepartureEntryId expected, out TowerActionResult? action)
    {
        GameplayWriteAssertion.Writing(nameof(IllusionTowerDepartureProjectionQueue) + "." + nameof(TryClaim));
        action = null;
        if (!pending.TryPeek(out var head) || head.Id != expected) return false;
        if (!pending.TryDequeue(out var claimed) || !ReferenceEquals(head, claimed))
            throw new InvalidOperationException("The departure queue changed outside its gameplay owner.");
        action = claimed.Action;
        return true;
    }
}
public sealed class IllusionTowerDepartureProjectionSink(IllusionTowerDepartureProjectionQueue queue) : IIllusionTowerDepartureSink
{
    public ValueTask DepartedAsync(TowerActionResult action, CancellationToken token)
    {
        queue.Enqueue(action);
        return ValueTask.CompletedTask;
    }
}
