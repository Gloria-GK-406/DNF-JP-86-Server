using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// One actor of a room, with what its declared type means already read.
/// </summary>
/// <remarks>
/// The flag travels with the actor because the frame that draws the room needs it per actor and has
/// no business knowing where the boundary between a monster and an ally sits. Which type is which
/// is a fact about the game, and the rules own it.
/// </remarks>
public readonly record struct RoomActorDescription(DungeonRoomActor Actor, bool IsAiCharacter)
{
    public byte? ObjectTemplateOwner { get; init; }
    public int ObjectTemplateIndex { get; init; }
    public bool IsHellTemplate { get; init; }
    public ushort HellOrder { get; init; }
    public int? HellGroupIndex { get; init; }
}

/// <summary>
/// What the room a run is standing in holds, as the room is about to be described.
/// </summary>
/// <param name="GroundDrops">
/// What the room still declares and nobody has taken, each under the number the client names it by.
/// </param>
/// <param name="FirstActorSequence">
/// The running number the room's first actor is given. Every later actor takes the next one.
/// </param>
public sealed record DungeonRoomContents(
    IReadOnlyList<RoomActorDescription> Actors,
    IReadOnlyList<GroundDrop> GroundDrops,
    ushort FirstActorSequence)
{
    public IReadOnlyList<DungeonRoomObject> Objects { get; init; } = [];
}

/// <summary>
/// Resolves the static room inputs used while a dungeon Cell is being created.
/// </summary>
/// <remarks>
/// The archive says what the map starts with; the Cell factory supplies the unpublished creation context
/// needed for stable identities. Once the Cell is published, runtime actor, floor and clear queries
/// use its snapshot and never return to this service.
/// <para>
/// The creation context is handed in. Nothing here looks a run up, so this reads no world state.
/// </para>
/// </remarks>
public interface IDungeonRoomRuleService
{
    /// <summary>
    /// What the not-yet-published Cell starts with.
    /// </summary>
    ValueTask<DungeonRoomContents> DescribeAsync(
        DungeonCellCreationContext context, CancellationToken cancellationToken);

    /// <summary>
    /// What each of these declared actors is, as the frame that draws the room needs it.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="DescribeAsync"/> so that the boundary between a monster and an ally
    /// can be asked for on its own, by a caller that already holds the actors.
    /// </remarks>
    IReadOnlyList<RoomActorDescription> Describe(IReadOnlyList<DungeonRoomActor> declared);

    /// <summary>The number this run and Cell identity gives its first actor.</summary>
    ushort FirstActorSequenceFor(DungeonCellCreationContext context);
}
