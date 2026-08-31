using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>
/// Reads what a room declares and answers what a run standing in it makes of that.
/// </summary>
/// <remarks>
/// Three things used to be written down wherever they were needed: the number a room's first actor
/// takes, where an actor type stops being a monster, and how a running number maps back to the
/// actor it names. The kill path and the entry path each derived the first of them from the other
/// slice's payload codec, which is a cross-slice reference the slice rules do not allow and which
/// only held because the two agreed by accident of being one call.
/// </remarks>
internal sealed class DungeonRoomRuleService(
    IDungeonRoomActorSource roomActors,
    IDungeonRoomDropSource roomDrops) : IDungeonRoomRuleService
{
    /// <summary>Where an actor type stops being a monster and starts being an AI character.</summary>
    /// <remarks>
    /// The reference server's own boundary, and it counts the two kinds separately: an AI
    /// character standing beside a monster is the first of its kind, not the second actor.
    /// </remarks>
    private const byte FirstAiCharacterActorType = 5;

    /// <summary>How many running numbers a room's actors may be given, as A21 bounds it.</summary>
    private const uint ActorSequenceRange = 60000;

    public async ValueTask<DungeonRoomContents> DescribeAsync(
        DungeonRunState run, bool firstEntry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (!firstEntry)
            return new DungeonRoomContents([], [], this.FirstActorSequenceFor(run));

        var declared = await roomActors.GetAsync(run.CurrentRoomMapId, cancellationToken)
            .ConfigureAwait(false);
        return new DungeonRoomContents(
            this.Describe(declared),
            await this.RemainingGroundDropsAsync(run, cancellationToken).ConfigureAwait(false),
            this.FirstActorSequenceFor(run));
    }

    public IReadOnlyList<RoomActorDescription> Describe(IReadOnlyList<DungeonRoomActor> declared)
    {
        ArgumentNullException.ThrowIfNull(declared);
        return [.. declared.Select(
            actor => new RoomActorDescription(actor, actor.ActorType >= FirstAiCharacterActorType))];
    }

    public async ValueTask<DeclaredActorLookup> FindDeclaredActorAsync(
        DungeonRunState run, ushort actorSequence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        var declared = await roomActors.GetAsync(run.CurrentRoomMapId, cancellationToken)
            .ConfigureAwait(false);
        var index = actorSequence - this.FirstActorSequenceFor(run);
        return index < 0 || index >= declared.Count
            ? new DeclaredActorLookup(null, declared.Count)
            : new DeclaredActorLookup(declared[index], declared.Count);
    }

    public async ValueTask<bool> IsCurrentRoomClearedAsync(
        DungeonRunState run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        var declared = await roomActors.GetAsync(run.CurrentRoomMapId, cancellationToken)
            .ConfigureAwait(false);
        return run.IsCleared(declared, this.FirstActorSequenceFor(run));
    }

    public async ValueTask<IReadOnlyList<GroundDrop>> RemainingGroundDropsAsync(
        DungeonRunState run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        return run.RemainingGroundDrops(
            await this.DeclaredDropsAsync(
                run.Layout.DungeonId, run.CurrentRoomMapId, cancellationToken)
                .ConfigureAwait(false));
    }

    public ValueTask<IReadOnlyList<DungeonRoomDrop>> DeclaredDropsAsync(
        int dungeonId, int mapId, CancellationToken cancellationToken) =>
        roomDrops.GetAsync(dungeonId, mapId, cancellationToken);

    /// <summary>
    /// The number the room's first actor is given, derived rather than drawn.
    /// </summary>
    /// <remarks>
    /// A21 draws it at random from 1 to 60000 per room, which the legacy server did to keep a
    /// re-entered room from colliding with actor identities the client still holds. Deriving it
    /// from the run's own seed and the room gets the same separation without a counter to keep:
    /// two runs of the same room differ because their seeds do, and one run re-sending the same
    /// room repeats itself, which is what a re-send should do.
    ///
    /// The mixing is not decoration. A dungeon's rooms carry consecutive map identifiers, so a
    /// derivation that only added the map to the seed put neighbouring rooms at neighbouring
    /// bases: the tutorial's first room took 30946 and 30947, and its second started at 30945 and
    /// ran over both. The client names an actor by this number, so a room that reuses one the
    /// player has already killed hands it a monster the client believes is dead. Two multiplies
    /// and two shifts scatter adjacent map identifiers across the range instead.
    /// </remarks>
    public ushort FirstActorSequenceFor(DungeonRunState run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var mixed = ((uint)run.CurrentRoomMapId * 2654435761u) ^ (uint)run.Layout.Seed;
        mixed ^= mixed >> 15;
        mixed *= 2246822519u;
        mixed ^= mixed >> 13;
        return (ushort)(1 + (mixed % ActorSequenceRange));
    }
}
