using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;

namespace Dfo.Server.Protocol.Game;

/// <summary>
/// A tower's departures awaiting their party projection: selected-session cleanup enqueues each
/// committed departure, and that tower's sweep claims them in order.
/// </summary>
public abstract class TowerDepartureProjectionQueue<TAction> where TAction : class, ITowerActionOutcome
{
    private sealed record Entry(TowerDepartureEntryId Id, TAction Action);
    private readonly ConcurrentQueue<Entry> pending = new();

    public void Enqueue(TAction action)
    {
        GameplayWriteAssertion.Writing(nameof(TowerDepartureProjectionQueue<TAction>) + "." + nameof(Enqueue));
        Add(action);
    }

    public ImmutableArray<TowerDepartureEntryId> SnapshotPending()
    {
        GameplayJob.DemandHealthy();
        return pending.Select(entry => entry.Id).ToImmutableArray();
    }

    public bool TryClaim(TowerDepartureEntryId expected, out TAction? action)
    {
        GameplayWriteAssertion.Writing(nameof(TowerDepartureProjectionQueue<TAction>) + "." + nameof(TryClaim));
        action = null;
        if (!pending.TryPeek(out var head) || head.Id != expected) return false;
        if (!pending.TryDequeue(out var claimed) || !ReferenceEquals(head, claimed))
            throw new InvalidOperationException("The departure queue changed outside its gameplay owner.");
        action = claimed.Action;
        return true;
    }

    public ValueTask DepartedAsync(TAction action, CancellationToken token)
    {
        GameplayWriteAssertion.Writing(nameof(TowerDepartureProjectionQueue<TAction>) + "." + nameof(DepartedAsync));
        Add(action);
        return ValueTask.CompletedTask;
    }

    private void Add(TAction action) => pending.Enqueue(new(new(Guid.NewGuid()), action));
}

public sealed class BossTowerDepartureProjectionQueue : TowerDepartureProjectionQueue<BossTowerActionResult>,
    IBossTowerDepartureProjectionDomainService, IBossTowerDepartureSink;

public sealed class IllusionTowerDepartureProjectionQueue : TowerDepartureProjectionQueue<TowerActionResult>,
    IIllusionTowerDepartureProjectionDomainService, IIllusionTowerDepartureSink;
