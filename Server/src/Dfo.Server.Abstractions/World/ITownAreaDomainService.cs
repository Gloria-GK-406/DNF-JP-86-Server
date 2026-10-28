using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

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

/// <summary>One character standing in a town area, as the session playing it.</summary>
/// <remarks>
/// The session is the one whose record this is, so a member compared with a session answers
/// whether that session -- not an earlier or later one of the same character -- is standing here.
/// </remarks>
public sealed record TownAreaMember
{
    public TownAreaMember(
        SelectedSessionIdentity session,
        TownAreaKey area,
        CharacterPositionState position,
        bool sharesTownPresence)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(position);
        if (position.TownId != area.TownId || position.AreaId != area.AreaId)
        {
            throw new ArgumentException(
                "The member position must belong to the supplied town area.", nameof(position));
        }

        Session = session;
        Area = area;
        Position = position;
        SharesTownPresence = sharesTownPresence;
    }

    public SelectedSessionIdentity Session { get; }
    public CharacterId CharacterId => Session.CharacterId;
    public TownAreaKey Area { get; }
    public CharacterPositionState Position { get; }
    public bool SharesTownPresence { get; }
}

/// <summary>An immutable, deterministic view of one area after a committed change.</summary>
public sealed record TownAreaSnapshot(
    TownAreaKey Key,
    long MembershipRevision,
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
    TownAreaSnapshot? PreviousArea,
    TownAreaSnapshot? CurrentArea,
    TownAreaDepartureReason? DepartureReason = null);

/// <summary>The immutable result emitted by one committed in-area movement.</summary>
public sealed record TownAreaMovement(
    CharacterId CharacterId,
    CharacterPositionState Position,
    TownAreaSnapshot Area);

/// <summary>Optional filters over the currently materialized area entities.</summary>
public sealed record TownAreaFilter(
    int? ChannelId = null,
    byte? TownId = null,
    byte? AreaId = null);

/// <summary>
/// Who stands in which town area, and where. Read only: every change is made by the session
/// playing the character, through <c>IOnlineCharacterRuntimeDomainService</c>.
/// </summary>
/// <remarks>
/// A character away from town -- choosing or running a dungeon -- still has a place it left, but
/// it stands in no area, so it is in no snapshot and <see cref="Find(CharacterId)"/> does not
/// answer for it.
/// </remarks>
public interface ITownAreaDomainService
{
    /// <summary>Where a character stands in town, or null when it is not standing in any area.</summary>
    TownAreaMember? Find(CharacterId characterId);

    TownAreaSnapshot? Find(TownAreaKey key);

    IReadOnlyList<TownAreaSnapshot> Query(TownAreaFilter filter);
}
