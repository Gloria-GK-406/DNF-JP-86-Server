using Dfo.Server.Data.Characters;

namespace Dfo.Server.Data.Game;

public interface ISelectCharacterSupplementalProjection;

/// <summary>Packet-ready values for the post-selection projections. They contain no frames, recipients or services.</summary>
public sealed record SelectCharacterDarkKnightComboProjection(byte[] Body) : ISelectCharacterSupplementalProjection;

public sealed record SelectCharacterVisibilityProjection(ushort UserId, byte UserStateBits) : ISelectCharacterSupplementalProjection;

public sealed record SelectCharacterCloneTitleProjection(uint ItemId) : ISelectCharacterSupplementalProjection;

public sealed record SelectCharacterHonorProjection(uint Level, uint Experience) : ISelectCharacterSupplementalProjection;

public sealed record SelectCharacterGrowthCapsuleProjection(
    byte Level,
    uint TotalExperience,
    ushort Page0Sp,
    ushort Page1Sp,
    ushort Page0Tp,
    ushort Page1Tp,
    uint PvpVictoryPoints,
    uint GrowthCapsuleExperience,
    uint HonorLevel,
    uint HonorExperience) : ISelectCharacterSupplementalProjection;

/// <summary>Committed inventory facts for one UPDATE_ITEM_LIST group. Domain facts only: the body
/// encoding belongs to whoever composes the projection, not to this record.</summary>
public sealed record SelectCharacterInventoryUpdateProjection(
    Inventory.InventoryContainerType ContainerType,
    IReadOnlyList<Inventory.InventoryItemState> Items,
    DateTimeOffset PreparedAt) : ISelectCharacterSupplementalProjection;

public sealed record SelectCharacterAdmissionRejectionProjection(byte ErrorCode, string Message, byte NoticeMode = 0) : ISelectCharacterSupplementalProjection;

/// <summary>A PvP wire identity. This record carries the identity only; its subtype-0 body is
/// produced by the projection composer, not here.</summary>
public sealed record SelectCharacterPvpIdentityProjection(
    ushort UserId,
    SelectCharacterResult Result) : ISelectCharacterSupplementalProjection;

public sealed record SelectCharacterPvpRoomSeatProjection(byte State, ushort UserId);

public sealed record SelectCharacterPvpRoomProjection(
    ushort RoomId,
    byte NameType,
    byte[] Name,
    byte RoomState,
    byte ManagerSeat,
    short MapIndex,
    byte BattleMode,
    IReadOnlyList<SelectCharacterPvpRoomSeatProjection> Seats,
    bool HasPassword,
    int MatchingType);

public sealed record SelectCharacterPvpLobbyProjection(
    IReadOnlyList<SelectCharacterPvpIdentityProjection> OrderedIdentities,
    IReadOnlyList<SelectCharacterPvpRoomProjection> OrderedRooms,
    SelectCharacterPvpIdentityProjection Newcomer) : ISelectCharacterSupplementalProjection;

public sealed record SelectCharacterAcceptedQuestProjection(ushort QuestId, uint TriggerValue);

public sealed record SelectCharacterAcceptedQuestsProjection(
    IReadOnlyList<SelectCharacterAcceptedQuestProjection> Quests) : ISelectCharacterSupplementalProjection;

public sealed record SelectCharacterPetRevivalProjection(ushort UserId, int CreatureKey, int StateValue) : ISelectCharacterSupplementalProjection;

public sealed record SelectCharacterDungeonRejoinProjection(int PartyId, IReadOnlyList<ushort> ParticipantUserIds) : ISelectCharacterSupplementalProjection;

public sealed record SelectCharacterRosterAppearanceProjection(
    byte Slot,
    int DisplayItemId,
    int ExpansionLength,
    byte[] ExpansionData,
    byte State,
    int LinkItemId,
    uint EnchantValue,
    byte Flag20);

public sealed record SelectCharacterRosterCharacterProjection(
    CharacterId CharacterId,
    ushort SlotIndex,
    byte[] Name,
    byte Job,
    byte GrowType,
    byte Level,
    uint HonorLevel,
    uint HonorExperience,
    ushort HonorReserved,
    IReadOnlyList<SelectCharacterRosterAppearanceProjection> Appearances,
    uint CloneTitleItemId);

public sealed record SelectCharacterRosterProjection(
    ushort SlotLimit,
    ushort SecondarySlotLimit,
    byte ManageLevel,
    int AdventurePoint,
    ushort Compatibility16,
    int Compatibility32,
    IReadOnlyList<SelectCharacterRosterCharacterProjection> Characters) : ISelectCharacterSupplementalProjection;

public sealed record SelectCharacterTrailingEquipmentLocksProjection(
    SelectCharacterResult Result) : ISelectCharacterSupplementalProjection;

public sealed record SelectCharacterExpertStoreCloseProjection(ushort OwnerUserId)
    : ISelectCharacterSupplementalProjection;

public sealed record SelectCharacterTownLeaveProjection(ushort UserId)
    : ISelectCharacterSupplementalProjection;
