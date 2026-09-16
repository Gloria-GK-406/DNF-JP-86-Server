using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.OnlineUsers;

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

/// <summary>The public party-directory partition owned by a character's current location.</summary>
public sealed record PartyDirectoryScope(
    int ChannelId,
    byte TownId,
    byte AreaId);

/// <summary>Composable filters over the process-local party directory.</summary>
public sealed record PartyDirectoryQuery(
    PartyDirectoryScope Scope,
    PartyCategory? Category = null,
    ushort? ModeTargetRaw = null);

/// <summary>A transport-free character view consumed by party behavior.</summary>
public sealed record PartyCharacterSnapshot(
    AccountId AccountId,
    CharacterId CharacterId,
    ushort WireUserId,
    PartyDirectoryScope? DirectoryScope);

public sealed record PartyMemberSnapshot(
    PartyCharacterSnapshot Character,
    byte Slot,
    SessionEndpoint? Endpoint,
    byte HpPercent)
{
    public CharacterId CharacterId => Character.CharacterId;
    public ushort WireUserId => Character.WireUserId;
}

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
    ImmutableArray<PartyMemberSnapshot> Members,
    bool IsVirtual = false)
{
    /// <summary>Builds a non-persistent view for an ungrouped character.</summary>
    public static PartySnapshot Virtual(PartyCharacterSnapshot character, PartySettings settings) =>
        new(0, 0, 0, character.WireUserId, settings, PartyTitle.Empty,
            [new PartyMemberSnapshot(character, 0, null, 100)], IsVirtual: true);
}

public sealed record PartyInviteNotification(
    CharacterId TargetCharacterId,
    ushort InviterWireUserId,
    PartyInviteDisplayState Display);

public sealed record PartyAcceptanceNotification(
    CharacterId InviterCharacterId,
    CharacterId InviteeCharacterId,
    ushort InviterWireUserId,
    ushort InviteeWireUserId);

/// <summary>The directory partitions affected by one atomic party mutation.</summary>
public sealed record PartyDirectoryChange(
    PartyDirectoryScope? Previous,
    PartyDirectoryScope? Current);

public enum PartySettingsChangeKind
{
    Created = 0,
    Updated,
}

/// <summary>
/// Protocol-independent audiences and one immutable state generation for downstream notifications.
/// </summary>
public sealed record PartyNotificationPlan(
    ImmutableArray<CharacterId> ClearAudience,
    PartySnapshot? Snapshot = null,
    PartyInviteNotification? Invitation = null,
    PartyAcceptanceNotification? Acceptance = null,
    ushort? ClearPartyId = null,
    long? ClearPartyGeneration = null,
    long? ClearPartyRevision = null,
    PartySettingsChangeKind? SettingsChange = null,
    PartyDirectoryChange? DirectoryChange = null)
{
    public static PartyNotificationPlan Empty { get; } = new([]);
}

public enum PartyFailureReason
{
    None = 0,
    InvalidEndpoint,
    CallerNotCurrent,
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
