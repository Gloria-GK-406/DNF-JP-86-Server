using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

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
/// Which of the two it was is carried because the instance value that identifies the piece is
/// derived from its origin; see <see cref="GroundDropInstance"/>.
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

    /// <summary>
    /// Claims the piece from the current Cell, banks it -- gold onto the character, an epic piece
    /// into the book, anything else into the bag -- reports the holdings change, and stages the
    /// claim. Runs inside the caller's write unit; a refusal or failure releases the claim.
    /// </summary>
    ValueTask<GroundPickupOutcome> PickUpAsync(GroundPickupRequest request, CancellationToken cancellationToken);
}

/// <summary>One character taking one piece off the floor of the room they are standing in.</summary>
public sealed record GroundPickupRequest(
    AccountId AccountId,
    CharacterId CharacterId,
    ushort GroundDropNumber,
    DungeonRunId ExpectedRunId = default,
    long? ExpectedRoomVersion = null);

/// <summary>What a pick-up did, or why it did not.</summary>
/// <param name="Granted">The bag addition, for a piece that went into a bag.</param>
/// <param name="SlotIndex">Where in <paramref name="Granted"/>'s container the piece landed.</param>
/// <param name="Amount">How much gold was taken; zero for anything else.</param>
/// <param name="Total">The gold the character carries afterwards.</param>
public sealed record GroundPickupOutcome(
    GroundPickupStatus Status,
    int ItemId,
    BusinessItemAdditionResult? Granted = null,
    int SlotIndex = 0,
    int Amount = 0,
    int Total = 0,
    DungeonPickupChange? Change = null,
    QuestProgressSnapshot? QuestProgress = null,
    DungeonPickupCommit? PendingCommit = null,
    EpicPieceAddition? EpicPiece = null)
{
    public static GroundPickupOutcome Refused(GroundPickupStatus status, int itemId = 0) =>
        new(status, itemId);
}

/// <summary>Why a piece was not picked up, in the server's own terms.</summary>
/// <remarks>
/// Named separately from the wire's single refusal because only one of these is a refusal the
/// player caused. The rest are ways for the two sides to disagree about what is on the floor, and
/// the log has to be able to say which one happened.
/// </remarks>
public enum GroundPickupStatus
{
    PickedUp = 0,

    /// <summary>The character is not in a dungeon run.</summary>
    NotInARun,

    /// <summary>This room has nothing lying under that number, or no longer has.</summary>
    NothingThere,

    /// <summary>Another request took it first.</summary>
    AlreadyTaken,

    /// <summary>The character is not bound to a main container.</summary>
    NoBoundContainer,

    /// <summary>The item's band of the inventory is full.</summary>
    InventoryFull,

    /// <summary>The archive declares nothing holdable under that identifier.</summary>
    UnknownItem,

    /// <summary>The piece belongs to another participant.</summary>
    NotOwner,

    /// <summary>Gold, which goes onto the character rather than into a bag.</summary>
    Banked,
    EpicPieceCollected,
}

