namespace Dfo.Server.Abstractions.World;

/// <summary>
/// The instance value a piece on a floor carries as its own.
/// </summary>
/// <remarks>
/// The room that announces a piece, the kill that drops one and the pick-up that banks one need the
/// same value. A working A21 session draws this value, and what the item ends up carrying once
/// picked up is the same value -- the reference session's dropped shoulder and the equipped one
/// share it. So it identifies the instance rather than counting anything, and what it must be is
/// distinct within a room and stable across a re-send of that room.
/// <para>
/// Kept inside a signed thirty-two bits, and away from zero. The picked-up piece is stored carrying
/// this same value, and the column it is stored in is signed: a value that only fits unsigned would
/// reach the wire as one number and the database as another.
/// </para>
/// </remarks>
public static class GroundDropInstance
{
    /// <summary>What the piece a room's breakable object declares carries.</summary>
    public static uint ForDeclaredObject(DungeonRunState run, int objectIndex)
    {
        ArgumentNullException.ThrowIfNull(run);

        var mixed = ((uint)run.CurrentRoomMapId * 2246822519u)
                    ^ ((uint)run.Layout.Seed + (uint)objectIndex);
        mixed ^= mixed >> 13;
        mixed *= 2654435761u;
        var instance = (mixed ^ (mixed >> 16)) & int.MaxValue;
        return instance == 0 ? 1u : instance;
    }

    /// <summary>
    /// What every piece one kill dropped carries.
    /// </summary>
    /// <remarks>
    /// Keyed by the actor rather than by the piece, which is what makes every piece of one kill
    /// carry one value -- the captured frames show a kill that dropped twice announcing the same
    /// value for both. Deriving it from the piece's number instead would put a different value in
    /// the bag than the one the player was shown on the floor, as it did until 2026-08-30.
    /// </remarks>
    public static uint ForKill(DungeonRunState run, ushort actorSequence) =>
        ForDeclaredObject(run, actorSequence);

    /// <summary>The value a piece on the current floor was announced under, from its origin.</summary>
    public static uint For(DungeonRunState run, GroundPiece piece) =>
        piece.FromKillActorSequence is { } actorSequence
            ? ForKill(run, actorSequence)
            : ForDeclaredObject(run, piece.ObjectIndex!.Value);
}
