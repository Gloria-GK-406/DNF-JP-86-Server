using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Application.World;

/// <summary>Process-lifetime store for lazily materialized town areas.</summary>
internal sealed class TownAreaDomainService(IPartyDirectoryPresenceSink? parties = null)
    : ITownAreaDomainService
{
    private readonly object sync = new();
    private readonly Dictionary<TownAreaKey, TownArea> areas = [];
    private readonly Dictionary<CharacterId, TownAreaMember> byCharacter = [];

    public TownAreaMembershipChange Enter(TownAreaMember member)
    {
        GameplayWriteAssertion.Writing(nameof(TownAreaDomainService) + "." + nameof(Enter));
        ArgumentNullException.ThrowIfNull(member);
        if (member.Area.TownId == 101 && member.Area.Instance == 0) throw new InvalidOperationException("A couple room requires a private instance.");

        TownAreaMembershipChange change;
        lock (sync)
        {
            if (byCharacter.TryGetValue(member.CharacterId, out var previous))
            {
                if (previous == member)
                {
                    var unchanged = SnapshotRequired(previous.Area);
                    return new TownAreaMembershipChange(
                        TownAreaMembershipChangeKind.Unchanged,
                        member.CharacterId,
                        member.Lease,
                        unchanged,
                        unchanged);
                }

                if (previous.Area == member.Area)
                {
                    var area = AreaRequired(previous.Area);
                    area.Replace(member);
                    byCharacter[member.CharacterId] = member;
                    var current = area.Snapshot();
                    change = new TownAreaMembershipChange(
                        previous.Lease == member.Lease
                            ? TownAreaMembershipChangeKind.Joined
                            : TownAreaMembershipChangeKind.Replaced,
                        member.CharacterId,
                        member.Lease,
                        current,
                        current);
                }
                else
                {
                    var previousSnapshot = RemoveCurrent(previous);
                    var currentSnapshot = AddCurrent(member);
                    change = new TownAreaMembershipChange(
                        previous.Lease == member.Lease
                            ? TownAreaMembershipChangeKind.Transferred
                            : TownAreaMembershipChangeKind.Replaced,
                        member.CharacterId,
                        member.Lease,
                        previousSnapshot,
                        currentSnapshot,
                        TownAreaDepartureReason.AreaTransfer);
                }
            }
            else
            {
                change = new TownAreaMembershipChange(
                    TownAreaMembershipChangeKind.Joined,
                    member.CharacterId,
                    member.Lease,
                    null,
                    AddCurrent(member));
            }

            UpdatePartyDirectory(member);
        }

        return change;
    }

    public TownAreaMembershipChange? LeaveExact(
        CharacterId characterId,
        TownAreaMembershipLease lease,
        TownAreaDepartureReason reason)
    {
        GameplayWriteAssertion.Writing(nameof(TownAreaDomainService) + "." + nameof(LeaveExact));
        TownAreaMembershipChange? change;
        lock (sync)
        {
            if (!byCharacter.TryGetValue(characterId, out var current)
                || current.Lease != lease)
            {
                return null;
            }

            change = new TownAreaMembershipChange(
                TownAreaMembershipChangeKind.Left,
                characterId,
                lease,
                RemoveCurrent(current),
                null,
                reason);
            parties?.UpdateDirectoryPresence(characterId, null);
        }

        return change;
    }

    public TownAreaMembershipChange? TransferExact(
        CharacterId characterId,
        TownAreaMembershipLease lease,
        TownAreaKey destination,
        CharacterPositionState position,
        bool sharesTownPresence)
    {
        GameplayWriteAssertion.Writing(nameof(TownAreaDomainService) + "." + nameof(TransferExact));
        ArgumentNullException.ThrowIfNull(position);
        if (destination.TownId == 101 && destination.Instance == 0) return null;
        if (position.TownId != destination.TownId || position.AreaId != destination.AreaId)
            throw new ArgumentException("The destination and position do not match.", nameof(position));

        TownAreaMembershipChange? change;
        TownAreaMember moved;
        lock (sync)
        {
            if (!byCharacter.TryGetValue(characterId, out var current)
                || current.Lease != lease)
            {
                return null;
            }

            moved = new TownAreaMember(
                characterId, lease, destination, position, sharesTownPresence);
            if (current.Area == destination)
            {
                var area = AreaRequired(destination);
                if (current == moved)
                    return null;
                area.Replace(moved);
                byCharacter[characterId] = moved;
                var snapshot = area.Snapshot();
                change = new TownAreaMembershipChange(
                    TownAreaMembershipChangeKind.Transferred,
                    characterId,
                    lease,
                    snapshot,
                    snapshot,
                    TownAreaDepartureReason.AreaTransfer);
            }
            else
            {
                change = new TownAreaMembershipChange(
                    TownAreaMembershipChangeKind.Transferred,
                    characterId,
                    lease,
                    RemoveCurrent(current),
                    AddCurrent(moved),
                    TownAreaDepartureReason.AreaTransfer);
            }

            UpdatePartyDirectory(moved);
        }

        return change;
    }

    public TownAreaMovement? MoveExact(
        CharacterId characterId,
        TownAreaMembershipLease lease,
        CharacterPositionState position)
    {
        GameplayWriteAssertion.Writing(nameof(TownAreaDomainService) + "." + nameof(MoveExact));
        ArgumentNullException.ThrowIfNull(position);
        lock (sync)
        {
            if (!byCharacter.TryGetValue(characterId, out var current)
                || current.Lease != lease
                || current.Area.TownId != position.TownId
                || current.Area.AreaId != position.AreaId)
            {
                return null;
            }

            if (current.Position == position)
                return null;

            var moved = new TownAreaMember(
                current.CharacterId,
                current.Lease,
                current.Area,
                position,
                current.SharesTownPresence);
            var area = AreaRequired(current.Area);
            area.Move(moved);
            byCharacter[characterId] = moved;
            return new TownAreaMovement(characterId, lease, position, area.Snapshot());
        }
    }

    public TownAreaMember? Find(CharacterId characterId)
    {
        lock (sync)
            return byCharacter.TryGetValue(characterId, out var member) ? member : null;
    }

    public TownAreaSnapshot? Find(TownAreaKey key)
    {
        lock (sync)
            return areas.TryGetValue(key, out var area) ? area.Snapshot() : null;
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
                .Select(pair => pair.Value.Snapshot())];
        }
    }

    private TownAreaSnapshot AddCurrent(TownAreaMember member)
    {
        if (!areas.TryGetValue(member.Area, out var area))
        {
            area = new TownArea(member.Area);
            areas.Add(member.Area, area);
        }

        area.Add(member);
        byCharacter[member.CharacterId] = member;
        return area.Snapshot();
    }

    private TownAreaSnapshot RemoveCurrent(TownAreaMember member)
    {
        var area = AreaRequired(member.Area);
        area.Remove(member.CharacterId);
        byCharacter.Remove(member.CharacterId);
        var snapshot = area.Snapshot();
        if (area.Count == 0)
            areas.Remove(member.Area);
        return snapshot;
    }

    private TownArea AreaRequired(TownAreaKey key) =>
        areas.TryGetValue(key, out var area)
            ? area
            : throw new InvalidDataException("The character index points to a missing town area.");

    private TownAreaSnapshot SnapshotRequired(TownAreaKey key) => AreaRequired(key).Snapshot();

    private void UpdatePartyDirectory(TownAreaMember member) =>
        parties?.UpdateDirectoryPresence(
            member.CharacterId,
            member.SharesTownPresence
                ? new PartyDirectoryScope(
                    member.Area.ChannelId,
                    member.Area.TownId,
                    member.Area.AreaId, member.Area.Instance)
                : null);

    private sealed class TownArea(TownAreaKey key)
    {
        private readonly Dictionary<CharacterId, TownAreaMember> members = [];
        private long membershipRevision;
        private long movementRevision;

        public int Count => members.Count;

        public void Add(TownAreaMember member)
        {
            members.Add(member.CharacterId, member);
            membershipRevision++;
        }

        public void Replace(TownAreaMember member)
        {
            members[member.CharacterId] = member;
            membershipRevision++;
        }

        public void Remove(CharacterId characterId)
        {
            if (!members.Remove(characterId))
                throw new InvalidDataException("The character index points to a missing area member.");
            membershipRevision++;
        }

        public void Move(TownAreaMember member)
        {
            if (!members.ContainsKey(member.CharacterId))
                throw new InvalidDataException("The character index points to a missing area member.");
            members[member.CharacterId] = member;
            movementRevision++;
        }

        public TownAreaSnapshot Snapshot() => new(
            key,
            membershipRevision,
            movementRevision,
            [.. members.Values.OrderBy(member => member.CharacterId.Value)]);
    }
}
