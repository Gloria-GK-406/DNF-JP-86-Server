using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>The logical identity of one lazily materialized town area.</summary>
public readonly record struct TownAreaKey
{
    public TownAreaKey(int channelId, byte townId, byte areaId, int instance = 0)
    {
        if (channelId <= 0)
            throw new ArgumentOutOfRangeException(nameof(channelId));
        ChannelId = channelId;
        TownId = townId;
        AreaId = areaId;
        if (instance < 0) throw new ArgumentOutOfRangeException(nameof(instance));
        Instance = instance;
    }

    public int ChannelId { get; }
    public byte TownId { get; }
    public byte AreaId { get; }
    public int Instance { get; }
}

/// <summary>
/// An opaque generation that owns one character's current area membership.
/// </summary>
/// <remarks>
/// It deliberately carries no connection or session semantics. The application boundary maps a
/// selected-character generation to this lease; the area only uses it for compare-and-remove.
/// </remarks>
public readonly record struct TownAreaMembershipLease
{
    public TownAreaMembershipLease(long value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public long Value { get; }
}

/// <summary>One character as known by a town area.</summary>
public sealed record TownAreaMember
{
    public TownAreaMember(
        CharacterId characterId,
        TownAreaMembershipLease lease,
        TownAreaKey area,
        CharacterPositionState position,
        bool sharesTownPresence)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (position.TownId != area.TownId || position.AreaId != area.AreaId)
        {
            throw new ArgumentException(
                "The member position must belong to the supplied town area.", nameof(position));
        }

        CharacterId = characterId;
        Lease = lease;
        Area = area;
        Position = position;
        SharesTownPresence = sharesTownPresence;
    }

    public CharacterId CharacterId { get; }
    public TownAreaMembershipLease Lease { get; }
    public TownAreaKey Area { get; }
    public CharacterPositionState Position { get; }
    public bool SharesTownPresence { get; }
}

/// <summary>An immutable, deterministic view of one area after a committed change.</summary>
public sealed record TownAreaSnapshot(
    TownAreaKey Key,
    long MembershipRevision,
    long MovementRevision,
    IReadOnlyList<TownAreaMember> Members)
{
    /// <summary>The members this viewer is allowed to project in town.</summary>
    public IReadOnlyList<TownAreaMember> VisibleTo(CharacterId viewer)
    {
        var self = Members.SingleOrDefault(member => member.CharacterId == viewer);
        if (self is null)
            return [];
        if (!self.SharesTownPresence)
            return [self];
        return [.. Members.Where(member => member.SharesTownPresence)];
    }
}

public enum TownAreaMembershipChangeKind
{
    Unchanged,
    Joined,
    Left,
    Transferred,
    Replaced,
}

public enum TownAreaDepartureReason
{
    AreaTransfer,
    DungeonSelection,
    DungeonEntry,
    SessionEnded,
}

/// <summary>The immutable result emitted by one committed membership mutation.</summary>
public sealed record TownAreaMembershipChange(
    TownAreaMembershipChangeKind Kind,
    CharacterId CharacterId,
    TownAreaMembershipLease Lease,
    TownAreaSnapshot? PreviousArea,
    TownAreaSnapshot? CurrentArea,
    TownAreaDepartureReason? DepartureReason = null);

/// <summary>The immutable result emitted by one committed in-area movement.</summary>
public sealed record TownAreaMovement(
    CharacterId CharacterId,
    TownAreaMembershipLease Lease,
    CharacterPositionState Position,
    TownAreaSnapshot Area);

/// <summary>Optional filters over the currently materialized area entities.</summary>
public sealed record TownAreaFilter(
    int? ChannelId = null,
    byte? TownId = null,
    byte? AreaId = null);

/// <summary>
/// Owns every live town-area entity and the reverse index from character to membership.
/// </summary>
public interface ITownAreaDomainService
{
    TownAreaMembershipChange Enter(TownAreaMember member);

    TownAreaMembershipChange? LeaveExact(
        CharacterId characterId,
        TownAreaMembershipLease lease,
        TownAreaDepartureReason reason);

    TownAreaMembershipChange? TransferExact(
        CharacterId characterId,
        TownAreaMembershipLease lease,
        TownAreaKey destination,
        CharacterPositionState position,
        bool sharesTownPresence);

    TownAreaMovement? MoveExact(
        CharacterId characterId,
        TownAreaMembershipLease lease,
        CharacterPositionState position);

    TownAreaMember? Find(CharacterId characterId);

    TownAreaSnapshot? Find(TownAreaKey key);

    IReadOnlyList<TownAreaSnapshot> Query(TownAreaFilter filter);
}
