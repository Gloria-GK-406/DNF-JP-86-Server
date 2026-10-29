namespace Dfo.Server.Data.World;

/// <summary>The archive town reserved for private couple rooms.</summary>
/// <remarks>
/// A room there exists only as a per-relation runtime instance: it is never a saved position,
/// an inn or a channel's arrival town, and a member standing in it is visible to no one else.
/// </remarks>
public static class CoupleRoomTown
{
    public const byte Id = 101;

    public static bool Is(int townId) => townId == Id;
}
