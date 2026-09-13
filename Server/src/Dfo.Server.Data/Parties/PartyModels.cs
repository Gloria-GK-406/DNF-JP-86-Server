using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Parties;

public enum PartyCategory : byte
{
    Story = 0,
    Cycle = 1,
    Normal = 2,
}

/// <summary>An immutable, byte-exact A21 party information block.</summary>
public sealed class PartySettings : IEquatable<PartySettings>
{
    public const int WireLength = 12;
    private readonly byte[] wireBytes;

    public PartySettings(ReadOnlySpan<byte> wireBytes)
    {
        if (wireBytes.Length != WireLength)
            throw new ArgumentException($"Party settings must contain exactly {WireLength} bytes.", nameof(wireBytes));
        this.wireBytes = wireBytes.ToArray();
    }

    public byte Capacity => wireBytes[2];
    public bool IsReturnUserParty => wireBytes[1] != 0;
    public PartyCategory Category => (PartyCategory)wireBytes[9];
    public ushort ModeTargetRaw => BinaryPrimitives.ReadUInt16LittleEndian(wireBytes.AsSpan(10));

    public byte[] ToWireBytes() => (byte[])wireBytes.Clone();

    public PartySettings WithReturnUserParty(bool enabled)
    {
        var normalized = ToWireBytes();
        normalized[1] = enabled ? (byte)1 : (byte)0;
        return new PartySettings(normalized);
    }

    public bool Equals(PartySettings? other) =>
        other is not null && wireBytes.AsSpan().SequenceEqual(other.wireBytes);

    public override bool Equals(object? obj) => obj is PartySettings other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in wireBytes)
            hash.Add(value);
        return hash.ToHashCode();
    }
}

/// <summary>Opaque raw-dstring bytes kept separate from the canonical settings block.</summary>
public sealed class PartyTitle : IEquatable<PartyTitle>
{
    private readonly byte[] bytes;

    public PartyTitle(ReadOnlySpan<byte> bytes) => this.bytes = bytes.ToArray();

    public static PartyTitle Empty { get; } = new([]);

    public byte[] ToBytes() => (byte[])bytes.Clone();

    public bool Equals(PartyTitle? other) =>
        other is not null && bytes.AsSpan().SequenceEqual(other.bytes);

    public override bool Equals(object? obj) => obj is PartyTitle other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in bytes)
            hash.Add(value);
        return hash.ToHashCode();
    }
}

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
    PartySettings Settings,
    PartyTitle Title,
    ImmutableArray<PartyMemberSnapshot> Members);

public sealed record PartyInviteNotification(
    SelectedSessionIdentity Target,
    SelectedSessionIdentity Inviter,
    PartyInviteDisplayState Display);

public sealed record PartyAcceptanceNotification(
    SelectedSessionIdentity Inviter,
    SelectedSessionIdentity Invitee);

public enum PartySettingsChangeKind
{
    Created = 0,
    Updated,
}

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
    long? ClearPartyRevision = null,
    PartySettingsChangeKind? SettingsChange = null,
    ImmutableArray<SelectedSessionIdentity> TownObservers = default)
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
    NotPartyLeader,
    InvalidPartyCapacity,
    PartyCapacityBelowMemberCount,
    UnknownPartyCategory,
    InvalidPartyModeTarget,
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
