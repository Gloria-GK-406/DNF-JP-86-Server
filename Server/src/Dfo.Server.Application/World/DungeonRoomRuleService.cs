using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>
/// Reads the static room declaration used to create one complete Cell seed.
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
    IDungeonRoomDropSource roomDrops,
    IObjectDropRuleService objectDrops,
    IDungeonRoomObjectSource roomObjects,
    IDungeonRoomMechanismRuleService? mechanisms = null) : IDungeonRoomRuleService
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
        DungeonCellCreationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var declared = await roomActors.GetAsync(context.MapId, cancellationToken)
            .ConfigureAwait(false);
        var resolved = declared.Select(actor => actor.ActorType < FirstAiCharacterActorType
            ? actor with
            {
                Level = context.Layout.LevelOf(actor.Level, actor.BasisLevelOffset),
                BasisLevelOffset = null,
            }
            : actor).ToArray();
        IReadOnlyList<RoomActorDescription> described = this.Describe(resolved)
            .Select(actor => actor with
            {
                IsNamedMonster = actor.IsNamedMonster || context.Layout.NamedMonsterCodes.Contains(actor.Actor.Code),
            }).ToArray();
        if (context.Layout.Hell is { } hell && context.Cell == hell.Cell)
            described = described.Concat(hell.Actors.Select(actor => new RoomActorDescription(actor.Actor,
                actor.Actor.ActorType >= FirstAiCharacterActorType)
                { IsHellTemplate = true, HellOrder = actor.Order, HellGroupIndex = actor.GroupIndex })).ToArray();
        var objects = await roomObjects.GetObjectsAsync(context.MapId, cancellationToken).ConfigureAwait(false);
        return new DungeonRoomContents(
            described,
            [.. objectDrops.Draw(await roomDrops.GetAsync(
                    context.Layout.DungeonId, context.MapId, cancellationToken)
                .ConfigureAwait(false), context)
                .Select((drop, index) => new GroundDrop(checked((ushort)(index + 1)), drop))],
            this.FirstActorSequenceFor(context))
        {
            Objects = objects,
            Mechanisms = objects.Where(obj => obj.Mechanism is not null).Select(obj => obj.Mechanism!.Value)
                .Distinct().Select(kind => (mechanisms ?? new DungeonRoomMechanismRuleService()).Define(kind)).ToArray(),
        };
    }

    public IReadOnlyList<RoomActorDescription> Describe(IReadOnlyList<DungeonRoomActor> declared)
    {
        ArgumentNullException.ThrowIfNull(declared);
        return [.. declared.Select(actor =>
        {
            var row = actor.Declaration;
            return new RoomActorDescription(actor, actor.ActorType >= FirstAiCharacterActorType)
            {
                IsNamedMonster = IsNamedMonster(actor.Code),
                ObjectTemplateOwner = row?.Source == DungeonActorSpawnSource.SpecialPassiveObject
                    ? checked((byte)(row.ParentObjectIndex ?? throw new InvalidDataException("Object template has no parent index.")))
                    : null,
                ObjectTemplateIndex = row?.ChildIndex ?? 0,
            };
        })];
    }

    // A21 client RVA 0x02791EF0; dungeon declarations extend these built-in ranges.
    private static bool IsNamedMonster(int code) =>
        code is >= 50000 and <= 51000 or >= 56000 and <= 60000 or >= 110900 and <= 110999;

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
    public ushort FirstActorSequenceFor(DungeonCellCreationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var mixed = ((uint)context.MapId * 2654435761u) ^ (uint)context.Layout.Seed;
        // One resource may be selected by several cells in the same run. Identity belongs to
        // the cell, so reused MAP resources must not recreate previously killed actors.
        mixed ^= ((uint)context.Cell.X << 8) | context.Cell.Y;
        mixed ^= mixed >> 15;
        mixed *= 2246822519u;
        mixed ^= mixed >> 13;
        return (ushort)(1 + (mixed % ActorSequenceRange));
    }
}
