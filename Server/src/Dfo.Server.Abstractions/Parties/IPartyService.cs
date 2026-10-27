using Dfo.Server.Data.Parties;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Abstractions.Parties;

/// <summary>Narrow ordered presence projection consumed by the session lifecycle.</summary>
public interface IPartyDirectoryPresenceSink
{
    void UpdateDirectoryPresence(CharacterId characterId, PartyDirectoryScope? scope);
}

/// <summary>The sole writer and query boundary for process-local party state.</summary>
public interface IPartyService : IPartyDirectoryPresenceSink
{
    IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query);

    PartyOperationResult CreateOrUpdateSettings(
        PartyCharacterSnapshot character,
        PartySettings settings,
        PartyTitle title);

    PartyOperationResult RequestInvite(
        PartyCharacterSnapshot inviter,
        PartyCharacterSnapshot target,
        PartyInviteDisplayState display);

    PartyOperationResult AcceptInvite(
        PartyCharacterSnapshot invitee,
        PartyCharacterSnapshot inviter);

    PartyOperationResult TransferLeadership(
        CharacterId currentLeaderCharacterId,
        CharacterId nextLeaderCharacterId);

    PartyOperationResult RemoveMember(
        CharacterId currentLeaderCharacterId,
        CharacterId targetCharacterId);

    PartyOperationResult LeaveParty(CharacterId characterId);

    /// <summary>Withdraws every party artifact owned by this character.</summary>
    PartyOperationResult CleanupCharacter(CharacterId characterId);

    /// <summary>Returns a real party or a transient, one-member virtual party view.</summary>
    PartySnapshot FindParty(PartyCharacterSnapshot character);

    /// <summary>Finds current members through a stable real or virtual party identity.</summary>
    PartyMembershipSnapshot? FindByKey(PartyKey key);

    /// <summary>Returns the character's real party key, or its virtual party key outside a real party.</summary>
    PartyKey KeyOf(CharacterId characterId);
}

/// <summary>Resolves a party member's endpoint through that character's current session.</summary>
/// <remarks>
/// This is a projection query, not character-owned endpoint state. The implementation must bridge
/// character to selected session, selected session to game session, and game session to endpoint.
/// </remarks>
public interface IPartyMemberSessionEndpointSource
{
    SessionEndpoint? FindThroughCurrentSession(CharacterId characterId);
}

public enum PartyModeRuleFailure
{
    None = 0,
    UnknownCategory,
    InvalidCapacity,
    CapacityBelowMemberCount,
    InvalidModeTarget,
}

/// <summary>The single source for default party settings and shared setting invariants.</summary>
public interface IPartyModeRuleService
{
    byte MaximumCapacity { get; }

    PartySettings DefaultSettings { get; }

    PartySettings Normalize(PartySettings settings);

    PartyModeRuleFailure Validate(PartySettings settings, int memberCount);

    PartyModeRuleFailure ValidateAdmission(PartySettings settings, int currentMemberCount);
}

/// <summary>
/// Builds the short-lived character values rendered by native party UI.
/// </summary>
/// <remarks>
/// The values remain owned by inventory, fatigue and the selected-session runtime. This service
/// only takes an immutable display snapshot for a party command; it does not duplicate their
/// mutation or persistence rules in the party aggregate.
/// </remarks>
public interface IPartyMemberStatusDomainService
{
    ValueTask<PartyInviteDisplayState> InvitationOfAsync(
        PartyCharacterSnapshot character,
        CancellationToken cancellationToken);

    byte CurrentHpPercentOf(CharacterId characterId);
}
