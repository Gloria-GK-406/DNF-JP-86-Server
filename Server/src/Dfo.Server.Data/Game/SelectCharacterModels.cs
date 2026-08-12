using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Game;

/// <summary>World-owned answers resolved for one completed entry preparation.</summary>
public sealed record GameWorldEntryState(
    DateTimeOffset PreparedAt,
    WorldContentEntryState Content,
    ChannelEntryPolicyState Channel);

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
    CharacterSessionEntryState Session);
