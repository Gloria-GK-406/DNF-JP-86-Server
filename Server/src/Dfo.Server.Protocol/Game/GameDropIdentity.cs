using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Protocol.Game;

/// <summary>
/// The value a piece on a floor carries as its own, on the wire.
/// </summary>
/// <remarks>
/// Three slices need it and they need the same one: the room that announces a piece, the kill that
/// drops one, and the pick-up that banks one. It sat in the dungeon-entry slice's payload codec and
/// the other two reached across for it, which the slice rules do not allow -- "切片内不跨协议引用。
/// 要共用就上提到 endpoint 层". This is that endpoint layer.
/// <para>
/// A working A21 session draws this value, and what the item ends up carrying once picked up is the
/// same value -- the reference session's dropped shoulder and the equipped one share it. So it
/// identifies the instance rather than counting anything, and what it must be is distinct within a
/// room and stable across a re-send of that room.
/// </para>
/// <para>
/// Kept inside a signed thirty-two bits, and away from zero. The picked-up piece is stored carrying
/// this same value, and the column it is stored in is signed: a value that only fits unsigned would
/// reach the wire as one number and the database as another.
/// </para>
/// </remarks>
internal static class GameDropIdentity
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
    /// value for both. The kill's frame announces it and the pick-up banks it, and those two are in
    /// different slices: until 2026-08-30 one of them derived it from the dead actor and the other
    /// from the piece's number, which agreed with itself everywhere and with the client nowhere.
    /// </remarks>
    public static uint ForKill(DungeonRunState run, ushort actorSequence) =>
        ForDeclaredObject(run, actorSequence);
}
