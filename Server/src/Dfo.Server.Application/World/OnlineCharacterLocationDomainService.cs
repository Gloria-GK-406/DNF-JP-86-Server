using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

/// <summary>
/// Where every character being played is: standing in a town area, or away from town with the
/// place it left. The one record of a played character's position.
/// </summary>
/// <remarks>
/// Each record belongs to the session generation that began it. A write naming another session is
/// ignored, so a replaced session cannot move its successor and nothing can re-create a record after
/// its session ended. Only <see cref="Characters.OnlineCharacterRuntimeDomainService"/> writes here;
/// everything else reads areas through <see cref="ITownAreaDomainService"/> and a character's own
/// position through the runtime.
/// <para>
/// Away from town -- choosing or running a dungeon -- a character stands in no area and is in no
/// snapshot, but its record keeps where it left: the return lands it there and the session's
/// teardown stores it.
/// </para>
/// </remarks>
/// <param name="parties">
/// Taken lazily: the party service reaches back here through the online runtime, so resolving it
/// while this service is being built would close a cycle the container cannot see through factories.
/// </param>
internal sealed class OnlineCharacterLocationDomainService(Func<IPartyDirectoryPresenceSink?> parties)
    : ITownAreaDomainService
{
    public OnlineCharacterLocationDomainService(IPartyDirectoryPresenceSink? parties = null)
        : this(() => parties)
    {
    }

    private readonly object sync = new();
    private readonly Dictionary<TownAreaKey, TownArea> areas = [];
    private readonly Dictionary<CharacterId, Location> byCharacter = [];

    /// <summary>
    /// Starts the record of a session entering the world, standing in its first area. Any earlier
    /// record of the character is replaced.
    /// </summary>
    public TownAreaMembershipChange Begin(
        SelectedSessionIdentity session,
        TownAreaKey area,
        CharacterPositionState position,
        bool sharesTownPresence)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineCharacterLocationDomainService) + "." + nameof(Begin));
        var member = new TownAreaMember(session, area, position, sharesTownPresence);
        RequireRoomInstance(area);
        lock (sync)
        {
            byCharacter.TryGetValue(session.CharacterId, out var previous);
            var before = previous?.Standing;
            var kind = before is null
                ? TownAreaMembershipChangeKind.Joined
                : previous!.Session != session
                    ? TownAreaMembershipChangeKind.Replaced
                    : before.Area == area
                        ? TownAreaMembershipChangeKind.Joined
                        : TownAreaMembershipChangeKind.Transferred;
            return StandAs(previous, member, kind, crossingOnly: true);
        }
    }

    /// <summary>Ends exactly this session's record. False when it does not own one.</summary>
    public bool End(SelectedSessionIdentity session)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineCharacterLocationDomainService) + "." + nameof(End));
        ArgumentNullException.ThrowIfNull(session);
        lock (sync)
        {
            if (Owned(session) is not { } current)
                return false;
            if (current.Standing is { } standing)
            {
                Unstand(standing);
                parties()?.UpdateDirectoryPresence(session.CharacterId, null);
            }

            byCharacter.Remove(session.CharacterId);
            return true;
        }
    }

    /// <summary>Takes the session's character out of town, keeping where it stood.</summary>
    /// <returns>The departure, or null when the session does not own a record standing in town.</returns>
    public TownAreaMembershipChange? Depart(SelectedSessionIdentity session, TownAreaDepartureReason reason)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineCharacterLocationDomainService) + "." + nameof(Depart));
        ArgumentNullException.ThrowIfNull(session);
        lock (sync)
        {
            if (Owned(session) is not { Standing: { } standing } current)
                return null;
            var previous = Unstand(standing);
            byCharacter[session.CharacterId] = current with { Area = null };
            parties()?.UpdateDirectoryPresence(session.CharacterId, null);
            return new TownAreaMembershipChange(
                TownAreaMembershipChangeKind.Left, session.CharacterId, previous, null, reason);
        }
    }

    /// <summary>Stands the session's character in an area, from town or from away.</summary>
    /// <returns>The arrival, or null when the session does not own a record.</returns>
    public TownAreaMembershipChange? Arrive(
        SelectedSessionIdentity session,
        TownAreaKey area,
        CharacterPositionState position,
        bool sharesTownPresence)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineCharacterLocationDomainService) + "." + nameof(Arrive));
        var member = new TownAreaMember(session, area, position, sharesTownPresence);
        RequireRoomInstance(area);
        lock (sync)
        {
            if (Owned(session) is not { } current)
                return null;
            var kind = current.Standing is { } before && before.Area != area
                ? TownAreaMembershipChangeKind.Transferred
                : TownAreaMembershipChangeKind.Joined;
            return StandAs(current, member, kind, crossingOnly: true);
        }
    }

    /// <summary>Moves a character standing in town into another area, or re-places it in its own.</summary>
    /// <returns>
    /// The transfer, or null when the session does not own a record standing in town, the
    /// destination is a couple room without its instance, or nothing would change.
    /// </returns>
    public TownAreaMembershipChange? Transfer(
        SelectedSessionIdentity session,
        TownAreaKey destination,
        CharacterPositionState position,
        bool sharesTownPresence)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineCharacterLocationDomainService) + "." + nameof(Transfer));
        ArgumentNullException.ThrowIfNull(position);
        if (destination.TownId == 101 && destination.Instance == 0) return null;
        if (position.TownId != destination.TownId || position.AreaId != destination.AreaId)
            throw new ArgumentException("The destination and position do not match.", nameof(position));
        var member = new TownAreaMember(session, destination, position, sharesTownPresence);
        lock (sync)
        {
            if (Owned(session) is not { Standing: { } before } current || before == member)
                return null;
            return StandAs(current, member, TownAreaMembershipChangeKind.Transferred, crossingOnly: false);
        }
    }

    /// <summary>Moves a character within the area it stands in.</summary>
    /// <returns>
    /// The movement, or null when the session does not own a record standing in that area or the
    /// character is already there.
    /// </returns>
    public TownAreaMovement? Step(SelectedSessionIdentity session, CharacterPositionState position)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineCharacterLocationDomainService) + "." + nameof(Step));
        ArgumentNullException.ThrowIfNull(position);
        lock (sync)
        {
            if (Owned(session) is not { Standing: { } before } current
                || before.Area.TownId != position.TownId
                || before.Area.AreaId != position.AreaId
                || before.Position == position)
            {
                return null;
            }

            byCharacter[session.CharacterId] = current with { Position = position };
            return new TownAreaMovement(session.CharacterId, position, Snapshot(AreaRequired(before.Area)));
        }
    }

    /// <summary>Where this session's character is, in town or away, or null when it owns no record.</summary>
    public CharacterPositionState? PositionOf(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (sync)
            return Owned(session)?.Position;
    }

    /// <summary>Where a character being played is, in town or away, or null when it is not being played.</summary>
    public CharacterPositionState? PositionOf(CharacterId characterId)
    {
        lock (sync)
            return byCharacter.TryGetValue(characterId, out var location) ? location.Position : null;
    }

    /// <summary>Where this session's character stands in town, or null when it does not.</summary>
    public TownAreaMember? StandingOf(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (sync)
            return Owned(session)?.Standing;
    }

    public TownAreaMember? Find(CharacterId characterId)
    {
        lock (sync)
            return byCharacter.TryGetValue(characterId, out var location) ? location.Standing : null;
    }

    public TownAreaSnapshot? Find(TownAreaKey key)
    {
        lock (sync)
            return areas.TryGetValue(key, out var area) ? Snapshot(area) : null;
    }

    public IReadOnlyList<TownAreaSnapshot> Query(TownAreaFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        lock (sync)
        {
            return [.. areas
                .Where(pair => (!filter.ChannelId.HasValue || pair.Key.ChannelId == filter.ChannelId)
                    && (!filter.TownId.HasValue || pair.Key.TownId == filter.TownId)
                    && (!filter.AreaId.HasValue || pair.Key.AreaId == filter.AreaId))
                .OrderBy(pair => pair.Key.ChannelId)
                .ThenBy(pair => pair.Key.TownId)
                .ThenBy(pair => pair.Key.AreaId)
                .Select(pair => Snapshot(pair.Value))];
        }
    }

    private static void RequireRoomInstance(TownAreaKey area)
    {
        if (area.TownId == 101 && area.Instance == 0)
            throw new InvalidOperationException("A couple room requires a private instance.");
    }

    private Location? Owned(SelectedSessionIdentity session) =>
        byCharacter.TryGetValue(session.CharacterId, out var location) && location.Session == session
            ? location
            : null;

    /// <summary>
    /// Replaces the character's record with one standing as <paramref name="member"/>. A crossing
    /// names <see cref="TownAreaDepartureReason.AreaTransfer"/>; a re-placement in the same area
    /// does too unless <paramref name="crossingOnly"/>.
    /// </summary>
    private TownAreaMembershipChange StandAs(
        Location? previous, TownAreaMember member, TownAreaMembershipChangeKind kind, bool crossingOnly)
    {
        var before = previous?.Standing;
        var next = new Location(member.Session, member.Position, member.Area, member.SharesTownPresence);
        TownAreaMembershipChange change;
        if (before == member)
        {
            var unchanged = Snapshot(AreaRequired(member.Area));
            return new(TownAreaMembershipChangeKind.Unchanged, member.CharacterId, unchanged, unchanged);
        }

        if (before is not null && before.Area == member.Area)
        {
            var area = AreaRequired(member.Area);
            byCharacter[member.CharacterId] = next;
            area.Replace();
            var current = Snapshot(area);
            change = new(kind, member.CharacterId, current, current,
                crossingOnly ? null : TownAreaDepartureReason.AreaTransfer);
        }
        else
        {
            var left = before is null ? null : Unstand(before);
            byCharacter[member.CharacterId] = next;
            change = new(kind, member.CharacterId, left, StandIn(member),
                before is null ? null : TownAreaDepartureReason.AreaTransfer);
        }

        UpdatePartyDirectory(member);
        return change;
    }

    private TownAreaSnapshot StandIn(TownAreaMember member)
    {
        if (!areas.TryGetValue(member.Area, out var area))
        {
            area = new TownArea(member.Area);
            areas.Add(member.Area, area);
        }

        area.Add(member.CharacterId);
        return Snapshot(area);
    }

    /// <summary>Takes the member out of its area and answers the area as it is left.</summary>
    private TownAreaSnapshot Unstand(TownAreaMember member)
    {
        var area = AreaRequired(member.Area);
        area.Remove(member.CharacterId);
        var snapshot = Snapshot(area);
        if (area.Count == 0)
            areas.Remove(member.Area);
        return snapshot;
    }

    private TownArea AreaRequired(TownAreaKey key) =>
        areas.TryGetValue(key, out var area)
            ? area
            : throw new InvalidDataException("The character index points to a missing town area.");

    private TownAreaSnapshot Snapshot(TownArea area) => new(
        area.Key,
        area.MembershipRevision,
        [.. area.Members.OrderBy(id => id.Value).Select(id => byCharacter[id].Standing!)]);

    private void UpdatePartyDirectory(TownAreaMember member) =>
        parties()?.UpdateDirectoryPresence(
            member.CharacterId,
            member.SharesTownPresence
                ? new PartyDirectoryScope(
                    member.Area.ChannelId,
                    member.Area.TownId,
                    member.Area.AreaId, member.Area.Instance)
                : null);

    /// <summary>One played character's position; it stands in <see cref="Area"/>, or in none while away.</summary>
    private sealed record Location(
        SelectedSessionIdentity Session,
        CharacterPositionState Position,
        TownAreaKey? Area,
        bool SharesTownPresence)
    {
        public TownAreaMember? Standing =>
            Area is { } area ? new TownAreaMember(Session, area, Position, SharesTownPresence) : null;
    }

    /// <summary>Which characters stand in one area; their positions stay on their records.</summary>
    private sealed class TownArea(TownAreaKey key)
    {
        private readonly HashSet<CharacterId> members = [];

        public TownAreaKey Key { get; } = key;
        public long MembershipRevision { get; private set; }
        public int Count => members.Count;
        public IEnumerable<CharacterId> Members => members;

        public void Add(CharacterId characterId)
        {
            members.Add(characterId);
            MembershipRevision++;
        }

        public void Replace() => MembershipRevision++;

        public void Remove(CharacterId characterId)
        {
            if (!members.Remove(characterId))
                throw new InvalidDataException("The character index points to a missing area member.");
            MembershipRevision++;
        }
    }
}
