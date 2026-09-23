namespace Dfo.Server.Abstractions.Content;

public enum DungeonObjectSection { Passive, SpecialPassive }

// Declaration identity, not an A21 wire index. Identical templates may occur many times.
public readonly record struct DungeonObjectId(DungeonObjectSection Section, int Index)
{
    public int RandomIdentity => checked(this.Index * 2 + (int)this.Section);
}

public sealed record DungeonRoomObject(DungeonObjectId Id, int Code, int X, int Y, int Z);

public interface IDungeonRoomObjectSource
{
    ValueTask<IReadOnlyList<DungeonRoomObject>> GetObjectsAsync(int mapId, CancellationToken cancellationToken);
}
