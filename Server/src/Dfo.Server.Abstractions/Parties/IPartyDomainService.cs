using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Parties;

/// <summary>The sole writer for process-local party, invitation and member-endpoint state.</summary>
public interface IPartyDomainService
{
    PartyOperationResult CreateOrUpdateSettingsExact(
        SelectedSessionIdentity session,
        PartySettings settings,
        PartyTitle title);

    PartyOperationResult UpdateEndpoint(SelectedSessionIdentity session, PartyEndpoint endpoint);

    PartyOperationResult RequestInvite(
        SelectedSessionIdentity inviter,
        ushort targetWireUserId,
        PartyInviteDisplayState display);

    PartyOperationResult AcceptInvite(
        SelectedSessionIdentity invitee,
        ushort inviterWireUserId);

    PartyOperationResult LeavePartyExact(SelectedSessionIdentity session);

    /// <summary>Withdraws every artifact owned by exactly this selected-session generation.</summary>
    PartyOperationResult CleanupSessionExact(SelectedSessionIdentity session);

    PartySnapshot? FindParty(SelectedSessionIdentity session);

    PartyEndpoint? FindEndpoint(SelectedSessionIdentity session);
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
        SelectedSessionIdentity session,
        CancellationToken cancellationToken);

    byte CurrentHpPercentOf(SelectedSessionIdentity session);
}
