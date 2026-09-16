using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// One thing lying under a number on a room's floor.
/// </summary>
/// <param name="ObjectIndex">
/// Which of the room's breakable objects declared it, when the room did. Null for a piece a kill
/// put there.
/// </param>
/// <param name="FromKillActorSequence">
/// Which kill produced it, when a kill did. Null for a piece the room declares.
/// </param>
/// <remarks>
/// Every source is stored in the current Cell's one ground-item collection. The stable number is
/// allocated once when the item enters that collection; pickup never rereads room content or a
/// separate monster-drop collection to reconstruct it.
/// <para>
/// Which of the two it was is carried rather than resolved here, because what identifies the piece
/// to the client is derived from its origin and that derivation belongs to the wire.
/// </para>
/// </remarks>
public sealed record GroundPiece(
    int ItemId,
    int Count,
    ushort Durability,
    bool IsEquipment,
    byte? ObjectIndex,
    ushort? FromKillActorSequence,
    CharacterId? OwnerCharacterId = null);

/// <summary>What a character can take off the floor of the room they are standing in.</summary>
public interface IGroundPickupDomainService
{
    /// <summary>
    /// What lies under that number, declared or dropped, or nothing when the room has nothing
    /// there.
    /// </summary>
    ValueTask<GroundPiece?> FindOnFloorAsync(
        DungeonRunState run, ushort groundDropNumber, CancellationToken cancellationToken);
}
