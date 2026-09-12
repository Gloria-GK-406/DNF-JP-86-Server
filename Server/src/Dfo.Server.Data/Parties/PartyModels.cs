using System.Collections.Immutable;
using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Parties;

public readonly record struct PartyIpv4Address(byte A, byte B, byte C, byte D)
{
    public bool IsUsableUnicast => A is > 0 and < 224 and not 127;
}

public sealed record PartyEndpoint(
    byte NatType,
    PartyIpv4Address InnerAddress,
    PartyIpv4Address OuterAddress,
    ushort Port,
    uint Mtu)
{
    public const uint MinimumMtu = 576;
    public const uint MaximumMtu = ushort.MaxValue;

    public bool IsValid =>
        InnerAddress.IsUsableUnicast
        && OuterAddress.IsUsableUnicast
        && Port != 0
        && Mtu is >= MinimumMtu and <= MaximumMtu;
}

public sealed record PartyMemberSnapshot(
    SelectedSessionIdentity Session,
    byte Slot,
    PartyEndpoint? Endpoint,
    byte HpPercent);

/// <summary>
/// The inviter values consumed by A21's native party-invitation window.
/// </summary>
/// <remarks>
/// This is a point-in-time display projection, not party-owned gameplay state. The party keeps
/// neither an inventory balance nor fatigue accounting; the command that issues the invitation
/// obtains those values from their owning domains and freezes them into the notification.
/// </remarks>
public sealed record PartyInviteDisplayState(
    int ReviveCoinCount,
    ushort RemainingFatigue,
    ushort Stamina,
    ushort AntiMagic);

/// <summary>A detached generation of one party, safe to project without re-querying mutable state.</summary>
public sealed record PartySnapshot(
    ushort PartyId,
    long Generation,
    long Revision,
    ushort LeaderWireUserId,
    ImmutableArray<PartyMemberSnapshot> Members);

public sealed record PartyInviteNotification(
    SelectedSessionIdentity Target,
    SelectedSessionIdentity Inviter,
    PartyInviteDisplayState Display);

public sealed record PartyAcceptanceNotification(
    SelectedSessionIdentity Inviter,
    SelectedSessionIdentity Invitee);

/// <summary>
/// Protocol-independent audiences and one immutable state generation for downstream notifications.
/// </summary>
public sealed record PartyNotificationPlan(
    ImmutableArray<SelectedSessionIdentity> ClearAudience,
    PartySnapshot? Snapshot = null,
    PartyInviteNotification? Invitation = null,
    PartyAcceptanceNotification? Acceptance = null,
    ushort? ClearPartyId = null,
    long? ClearPartyGeneration = null,
    long? ClearPartyRevision = null)
{
    public static PartyNotificationPlan Empty { get; } = new([]);
}

public enum PartyFailureReason
{
    None = 0,
    InvalidEndpoint,
    SessionNotCurrent,
    TargetNotFound,
    SelfInvite,
    NotStandingInTown,
    NotInSameGameChannel,
    NotInSameTownArea,
    InviterNotLeader,
    PartyFull,
    TargetAlreadyInParty,
    InviteNotFoundOrStale,
    InviteExpired,
    PartyGenerationChanged,
    NotInParty,
}

public sealed record PartyOperationResult(
    PartyFailureReason Failure,
    PartyNotificationPlan Notifications)
{
    public bool Succeeded => Failure == PartyFailureReason.None;

    public static PartyOperationResult Success(PartyNotificationPlan? notifications = null) =>
        new(PartyFailureReason.None, notifications ?? PartyNotificationPlan.Empty);

    public static PartyOperationResult Reject(PartyFailureReason failure) =>
        failure == PartyFailureReason.None
            ? throw new ArgumentOutOfRangeException(nameof(failure))
            : new(failure, PartyNotificationPlan.Empty);
}
