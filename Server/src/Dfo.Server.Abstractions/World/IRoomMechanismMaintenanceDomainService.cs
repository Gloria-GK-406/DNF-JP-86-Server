namespace Dfo.Server.Abstractions.World;
public sealed record RoomMechanismMaintenanceScan(IReadOnlyList<DungeonRoomMechanismView> Views, DateTimeOffset? Next);
public interface IRoomMechanismMaintenanceDomainService
{
    ValueTask ProjectAsync(DungeonRoomMechanismView view, CancellationToken token);
    RoomMechanismMaintenanceScan Advance(DateTimeOffset now);
    bool NeedsProjection(DungeonRoomMechanismView view);
    void MarkProjected(DungeonRoomMechanismView view);
}
