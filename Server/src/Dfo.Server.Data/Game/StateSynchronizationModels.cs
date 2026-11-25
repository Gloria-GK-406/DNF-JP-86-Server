using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Data.Game;

/// <summary>Committed inventory facts for one bounded inventory update.</summary>
public sealed record CommittedInventoryUpdateState(
    InventoryContainerType ContainerType,
    IReadOnlyList<InventoryItemState> Items,
    DateTimeOffset PreparedAt);

/// <summary>Current domain state required to publish one PvP participant.</summary>
public sealed record PvpParticipantEntryState(
    ActiveCharacterState Character,
    CharacterPublicEntryState Public,
    IReadOnlyList<InventoryItemState> Equipment,
    IReadOnlyList<CharacterPetRuntimeState> Pets);

public enum PvpRoomSeatOccupancy : byte
{
    Open,
    Closed,
    Taken,
}

/// <summary>One seat of a room as the lobby shows it.</summary>
/// <param name="Team">The side number of a taken seat: 0 for an individual fighter, 1 or 2 for a team.</param>
/// <param name="UserId">The wire user id of a taken seat's session; 0 otherwise.</param>
public sealed record PvpRoomSeatState(PvpRoomSeatOccupancy Occupancy, byte State, ushort UserId, bool Ready);

/// <summary>One committed duel room as the lobby shows it; the wire layout is the codec's.</summary>
/// <param name="Waiting">Whether the room is waiting for its next battle rather than running one.</param>
public sealed record PvpRoomSnapshotState(
    ushort RoomNumber,
    byte NameType,
    byte[] Name,
    bool Waiting,
    byte OwnerSeat,
    short MapIndex,
    byte BattleMode,
    IReadOnlyList<PvpRoomSeatState> Seats,
    bool HasPassword);

public sealed record PvpLobbySynchronizationState(
    IReadOnlyList<PvpParticipantEntryState> OrderedIdentities,
    IReadOnlyList<PvpRoomSnapshotState> OrderedRooms,
    PvpParticipantEntryState Newcomer);

/// <summary>Who should be told a newcomer joined the lobby, and what to tell them about.</summary>
/// <remarks>
/// Characters, not connections. Working out where those characters are is not this plan's
/// business, and used to be: the version of this that carried a connection each forced the
/// domain service that built it to go looking for sockets.
/// </remarks>
public sealed record PvpParticipantPublicationPlan(
    IReadOnlyList<CharacterId> Audience,
    PvpParticipantEntryState Newcomer);

public sealed record AcceptedQuestProgressState(ushort QuestId, uint TriggerValue);

public sealed record AcceptedQuestSynchronizationState(
    IReadOnlyList<AcceptedQuestProgressState> Quests);

public sealed record PetRevivalState(ushort UserId, int CreatureKey, int StateValue);

public sealed record CharacterRosterAppearanceState(
    byte Slot,
    int DisplayItemId,
    int ExpansionLength,
    byte[] ExpansionData,
    byte State,
    int LinkItemId,
    uint EnchantValue,
    byte Flag20);

public sealed record CharacterRosterMemberState(
    CharacterId CharacterId,
    ushort SlotIndex,
    byte[] Name,
    byte Job,
    byte GrowType,
    byte Level,
    uint HonorLevel,
    uint HonorExperience,
    ushort HonorReserved,
    IReadOnlyList<CharacterRosterAppearanceState> Appearances,
    uint CloneTitleItemId)
{
    public Dfo.Server.Data.Characters.CharacterAppearance? Display { get; init; }
}

public sealed record CharacterRosterEntryState(
    ushort SlotLimit,
    ushort SecondarySlotLimit,
    byte ManageLevel,
    int AdventurePoint,
    ushort Compatibility16,
    int Compatibility32,
    IReadOnlyList<CharacterRosterMemberState> Characters);
