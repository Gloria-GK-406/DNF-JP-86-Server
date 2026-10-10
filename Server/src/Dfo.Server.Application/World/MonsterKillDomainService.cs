using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>What a kill reported against the current Cell snapshot means.</summary>
internal sealed class MonsterKillDomainService : IMonsterKillDomainService
{
    public ValueTask<KillIdentification> IdentifyAsync(
        DungeonRunState run, ushort actorSequence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();

        var cell = run.CurrentCellState;
        var actor = cell.FindActor(actorSequence);
        return ValueTask.FromResult(actor is null
            ? new KillIdentification(null, cell.Actors.Count)
            : new KillIdentification(
                new IdentifiedKill(
                    actor.Actor.Code,
                    run.Layout.LevelOf(actor.Actor.Level),
                    (MonsterRank)actor.Actor.ActorType,
                    actor.IsMonster, actor.IsNamedMonster),
                cell.Actors.Count));
    }

    public ValueTask<bool> IsCurrentRoomClearedAsync(
        DungeonRunState run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(run.CurrentCellState.IsCleared);
    }
}
