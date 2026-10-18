using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Game;

/// <summary>World-owned answers resolved for one completed entry preparation.</summary>
/// <param name="Subject">
/// What <paramref name="Content"/> was computed from. Kept because two things change those inputs
/// without a re-entry -- taking a quest on and handing one in -- and the answer has to be worked
/// out again from them rather than edited. Editing it can only remove; a completion that satisfies
/// another quest's prerequisite has to ADD one, and a character with no next quest and no way to
/// reach a dungeon is stuck until they log out.
/// </param>
public sealed record GameWorldEntryState(
    DateTimeOffset PreparedAt,
    WorldContentEntryState Content,
    ChannelEntryPolicyState Channel,
    WorldContentSubject? Subject = null);

/// <summary>Runtime session state associated with the selected account and character.</summary>
public sealed record CharacterSessionEntryState(
    AccountId AccountId,
    CharacterId CharacterId,
    SessionAdjacentEntryState Adjacent);

/// <summary>
/// Application response envelope for the Select Character use case.
/// </summary>
/// <remarks>
/// Each member remains owned by its source domain. This type combines them for the
/// application and networking boundaries without making them character state.
/// </remarks>
public sealed record SelectCharacterResult(
    ActiveCharacterState Character,
    CharacterEntryState CharacterEntry,
    CharacterInventoryEntryState CharacterInventory,
    AccountInventoryEntryState AccountInventory,
    AccountEntryState Account,
    GameWorldEntryState World,
    CharacterSessionEntryState Session,
    AccountCharacterRoster? Roster = null,
    CharacterRosterEntryState? RosterEntry = null, Dfo.Server.Data.Events.EventPlayerSnapshot? Events = null,
    IReadOnlyList<EpicPieceBalance>? EpicPieces = null,
    Dfo.Server.Data.Marriage.MarriageEngagementSnapshot? Engagement = null)
{
    public Dfo.Server.Data.Marriage.MarriageCapsuleSnapshot? Capsules { get; init; }
    public Dfo.Server.Data.Marriage.HousingSnapshot? Housing { get; init; }
}

public sealed record SelectCharacterQuestSynchronization(
    SelectCharacterResult Result,
    AcceptedQuestSynchronizationState State);

public sealed record SelectCharacterPetRevival(
    SelectCharacterResult Result,
    PetRevivalState State);
