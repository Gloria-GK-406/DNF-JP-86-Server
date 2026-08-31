using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>What a kill reported against a run means.</summary>
internal sealed class MonsterKillDomainService(
    IDungeonRoomRuleService rooms,
    IExperienceRuleService experienceRules) : IMonsterKillDomainService
{
    public async ValueTask<KillIdentification> IdentifyAsync(
        DungeonRunState run, ushort actorSequence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        var lookup = await rooms.FindDeclaredActorAsync(run, actorSequence, cancellationToken)
            .ConfigureAwait(false);
        return lookup.Actor is not { } actor
            ? new KillIdentification(null, lookup.DeclaredCount)
            : new KillIdentification(
                new IdentifiedKill(
                    actor.Code,
                    run.Layout.LevelOf(actor.Level),
                    (MonsterRank)actor.ActorType,
                    experienceRules.IsMonster(actor.ActorType)),
                lookup.DeclaredCount);
    }

    public ValueTask<bool> IsCurrentRoomClearedAsync(
        DungeonRunState run, CancellationToken cancellationToken) =>
        rooms.IsCurrentRoomClearedAsync(run, cancellationToken);
}
