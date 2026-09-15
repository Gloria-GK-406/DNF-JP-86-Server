using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Sessions
{
    /// <summary>Test migration input for fixtures authored against the retired presence record.</summary>
    public sealed record SessionPresence(
        CharacterId CharacterId,
        int ListenerPort,
        byte TownId,
        byte AreaId,
        bool StandingInTown,
        SelectedSessionIdentity? Session = null,
        bool SharesTownPresence = true,
        int ChannelId = 0);

    [Flags]
    public enum SessionPresenceScopeFlags
    {
        None = 0,
        SameChannel = 1 << 0,
        SameTown = 1 << 1,
        SameArea = 1 << 2,
        StandingInTown = 1 << 3,
        SharesTownPresence = 1 << 4,
        SameLogicalChannel = 1 << 5,
    }

    public sealed record SessionPresenceScope(
        SessionPresenceScopeFlags Flags,
        int? ListenerPort = null,
        byte? TownId = null,
        byte? AreaId = null,
        CharacterId? ExcludingCharacterId = null,
        int? ChannelId = null)
    {
        public static SessionPresenceScope Global { get; } = new(SessionPresenceScopeFlags.None);

        public static SessionPresenceScope VisibleTownArea(
            int listenerPort,
            byte townId,
            byte areaId,
            CharacterId? excludingCharacterId = null) =>
            new(
                SessionPresenceScopeFlags.SameChannel
                | SessionPresenceScopeFlags.SameTown
                | SessionPresenceScopeFlags.SameArea
                | SessionPresenceScopeFlags.StandingInTown
                | SessionPresenceScopeFlags.SharesTownPresence,
                listenerPort,
                townId,
                areaId,
                excludingCharacterId);

        public static SessionPresenceScope VisiblePartyArea(
            int channelId,
            byte townId,
            byte areaId,
            CharacterId? excludingCharacterId = null) =>
            new(
                SessionPresenceScopeFlags.SameLogicalChannel
                | SessionPresenceScopeFlags.SameTown
                | SessionPresenceScopeFlags.SameArea
                | SessionPresenceScopeFlags.StandingInTown
                | SessionPresenceScopeFlags.SharesTownPresence,
                TownId: townId,
                AreaId: areaId,
                ExcludingCharacterId: excludingCharacterId,
                ChannelId: channelId);
    }

    public interface ISessionPresenceDomainService : ITownAreaDomainService
    {
        void Admit(SessionPresence presence);
        bool Withdraw(CharacterId characterId);
        bool WithdrawExact(SelectedSessionIdentity session);
        bool RelocateExact(
            SelectedSessionIdentity session,
            byte townId,
            byte areaId,
            bool standingInTown,
            bool sharesTownPresence = true);
        new SessionPresence? Find(CharacterId characterId);
        IReadOnlyList<SessionPresence> Query(SessionPresenceScope scope);

        TownAreaMembershipChange ITownAreaDomainService.Enter(TownAreaMember member)
        {
            Admit(new SessionPresence(
                member.CharacterId,
                member.Area.ChannelId == 11 ? 10011 : member.Area.ChannelId,
                member.Area.TownId,
                member.Area.AreaId,
                true,
                null,
                member.SharesTownPresence,
                member.Area.ChannelId));
            return new TownAreaMembershipChange(
                TownAreaMembershipChangeKind.Joined,
                member.CharacterId,
                member.Lease,
                null,
                ((ITownAreaDomainService)this).Find(member.Area));
        }

        TownAreaMembershipChange? ITownAreaDomainService.LeaveExact(
            CharacterId characterId,
            TownAreaMembershipLease lease,
            TownAreaDepartureReason reason)
        {
            var current = Find(characterId);
            if (current is null
                || current.Session is { } session && session.ConnectionGeneration != lease.Value
                || !Withdraw(characterId))
            {
                return null;
            }
            return new TownAreaMembershipChange(
                TownAreaMembershipChangeKind.Left,
                characterId,
                lease,
                Snapshot(LogicalChannel(current),
                    current.TownId, current.AreaId),
                null,
                reason);
        }

        TownAreaMembershipChange? ITownAreaDomainService.TransferExact(
            CharacterId characterId,
            TownAreaMembershipLease lease,
            TownAreaKey destination,
            CharacterPositionState position,
            bool sharesTownPresence)
        {
            var current = Find(characterId);
            if (current?.Session is not { } session
                || session.ConnectionGeneration != lease.Value
                || !RelocateExact(
                    session,
                    destination.TownId,
                    destination.AreaId,
                    true,
                    sharesTownPresence))
            {
                return null;
            }
            return new TownAreaMembershipChange(
                TownAreaMembershipChangeKind.Transferred,
                characterId,
                lease,
                null,
                ((ITownAreaDomainService)this).Find(destination),
                TownAreaDepartureReason.AreaTransfer);
        }

        TownAreaMovement? ITownAreaDomainService.MoveExact(
            CharacterId characterId,
            TownAreaMembershipLease lease,
            CharacterPositionState position) => null;

        TownAreaMember? ITownAreaDomainService.Find(CharacterId characterId) =>
            Find(characterId) is { StandingInTown: true } value ? ToMember(value) : null;

        TownAreaSnapshot? ITownAreaDomainService.Find(TownAreaKey key) =>
            Snapshot(key.ChannelId, key.TownId, key.AreaId);

        IReadOnlyList<TownAreaSnapshot> ITownAreaDomainService.Query(TownAreaFilter filter) =>
            [.. Query(SessionPresenceScope.Global)
                .Where(value => value.StandingInTown)
                .Where(value => !filter.ChannelId.HasValue
                    || LogicalChannel(value) == filter.ChannelId)
                .Where(value => !filter.TownId.HasValue || value.TownId == filter.TownId)
                .Where(value => !filter.AreaId.HasValue || value.AreaId == filter.AreaId)
                .GroupBy(value => new TownAreaKey(
                    LogicalChannel(value),
                    value.TownId,
                    value.AreaId))
                .Select(group => new TownAreaSnapshot(
                    group.Key,
                    1,
                    0,
                    [.. group.Select(ToMember).OrderBy(value => value.CharacterId.Value)]))];

        private TownAreaSnapshot? Snapshot(int channelId, byte townId, byte areaId)
        {
            var members = Query(SessionPresenceScope.Global)
                .Where(value => value.StandingInTown
                    && LogicalChannel(value) == channelId
                    && value.TownId == townId
                    && value.AreaId == areaId)
                .Select(ToMember)
                .OrderBy(value => value.CharacterId.Value)
                .ToArray();
            return members.Length == 0
                ? null
                : new TownAreaSnapshot(new TownAreaKey(channelId, townId, areaId), 1, 0, members);
        }

        private static TownAreaMember ToMember(SessionPresence value)
        {
            var key = new TownAreaKey(
                LogicalChannel(value),
                value.TownId,
                value.AreaId);
            return new TownAreaMember(
                value.CharacterId,
                value.Session?.TownAreaLease()
                    ?? new TownAreaMembershipLease(Math.Max(1, value.CharacterId.Value)),
                key,
                new CharacterPositionState(value.TownId, value.AreaId, 0, 0, 0),
                value.SharesTownPresence);
        }

        private static int LogicalChannel(SessionPresence value) =>
            value.ChannelId > 0 ? value.ChannelId : value.ListenerPort switch
            {
                10011 => 11,
                10068 => 68,
                10161 => 100,
                _ => value.ListenerPort,
            };
    }
}

namespace Dfo.Server.Application.Sessions
{
    using Dfo.Server.Abstractions.Sessions;

    /// <summary>
    /// Test-only adapter that lets older fixtures feed the new area store while production has no
    /// session-presence model or compatibility registration.
    /// </summary>
    internal sealed class SessionPresenceDomainService : ISessionPresenceDomainService
    {
        private readonly TownAreaDomainService inner;
        private readonly Dictionary<CharacterId, SessionPresence> legacy = [];

        public SessionPresenceDomainService(IPartyDirectoryPresenceSink? parties = null)
        {
            inner = new TownAreaDomainService(parties);
        }

        public void Admit(SessionPresence presence)
        {
            legacy[presence.CharacterId] = presence;
            if (!presence.StandingInTown)
                return;
            inner.Enter(ToMember(presence));
        }

        public bool Withdraw(CharacterId characterId)
        {
            if (!legacy.Remove(characterId, out var current))
                return false;
            if (current.Session is { } exact)
            {
                inner.LeaveExact(
                    characterId,
                    exact.TownAreaLease(),
                    TownAreaDepartureReason.SessionEnded);
            }
            return true;
        }

        public bool WithdrawExact(SelectedSessionIdentity session)
        {
            if (!legacy.TryGetValue(session.CharacterId, out var current)
                || current.Session != session)
            {
                return false;
            }
            legacy.Remove(session.CharacterId);
            inner.LeaveExact(
                session.CharacterId,
                session.TownAreaLease(),
                TownAreaDepartureReason.SessionEnded);
            return true;
        }

        public bool RelocateExact(
            SelectedSessionIdentity session,
            byte townId,
            byte areaId,
            bool standingInTown,
            bool sharesTownPresence = true)
        {
            if (!legacy.TryGetValue(session.CharacterId, out var current)
                || current.Session != session)
            {
                return false;
            }

            var moved = current with
            {
                TownId = townId,
                AreaId = areaId,
                StandingInTown = standingInTown,
                SharesTownPresence = sharesTownPresence,
            };
            legacy[session.CharacterId] = moved;
            if (!standingInTown)
            {
                inner.LeaveExact(
                    session.CharacterId,
                    session.TownAreaLease(),
                    TownAreaDepartureReason.DungeonEntry);
                return true;
            }

            var member = ToMember(moved);
            if (inner.Find(session.CharacterId) is null)
                inner.Enter(member);
            else
            {
                inner.TransferExact(
                    session.CharacterId,
                    session.TownAreaLease(),
                    member.Area,
                    member.Position,
                    member.SharesTownPresence);
            }
            return true;
        }

        public SessionPresence? Find(CharacterId characterId) =>
            legacy.GetValueOrDefault(characterId);

        public IReadOnlyList<SessionPresence> Query(SessionPresenceScope scope) =>
            [.. legacy.Values
                .Where(value => value.CharacterId != scope.ExcludingCharacterId)
                .Where(value => !scope.Flags.HasFlag(SessionPresenceScopeFlags.SameChannel)
                    || value.ListenerPort == scope.ListenerPort)
                .Where(value => !scope.Flags.HasFlag(SessionPresenceScopeFlags.SameLogicalChannel)
                    || value.ChannelId == scope.ChannelId)
                .Where(value => !scope.Flags.HasFlag(SessionPresenceScopeFlags.SameTown)
                    || value.TownId == scope.TownId)
                .Where(value => !scope.Flags.HasFlag(SessionPresenceScopeFlags.SameArea)
                    || value.AreaId == scope.AreaId)
                .Where(value => !scope.Flags.HasFlag(SessionPresenceScopeFlags.StandingInTown)
                    || value.StandingInTown)
                .Where(value => !scope.Flags.HasFlag(SessionPresenceScopeFlags.SharesTownPresence)
                    || value.SharesTownPresence)
                .OrderBy(value => value.CharacterId.Value)];

        TownAreaMembershipChange ITownAreaDomainService.Enter(TownAreaMember member)
        {
            var old = legacy.GetValueOrDefault(member.CharacterId);
            legacy[member.CharacterId] = new SessionPresence(
                member.CharacterId,
                old?.ListenerPort ?? 10011,
                member.Area.TownId,
                member.Area.AreaId,
                true,
                old?.Session,
                member.SharesTownPresence,
                member.Area.ChannelId);
            return inner.Enter(member);
        }

        TownAreaMembershipChange? ITownAreaDomainService.LeaveExact(
            CharacterId characterId,
            TownAreaMembershipLease lease,
            TownAreaDepartureReason reason)
        {
            var change = inner.LeaveExact(characterId, lease, reason);
            if (change is not null)
                legacy.Remove(characterId);
            return change;
        }

        TownAreaMembershipChange? ITownAreaDomainService.TransferExact(
            CharacterId characterId,
            TownAreaMembershipLease lease,
            TownAreaKey destination,
            CharacterPositionState position,
            bool sharesTownPresence)
        {
            if (legacy.TryGetValue(characterId, out var old))
            {
                legacy[characterId] = old with
                {
                    TownId = destination.TownId,
                    AreaId = destination.AreaId,
                    StandingInTown = true,
                    SharesTownPresence = sharesTownPresence,
                    ChannelId = destination.ChannelId,
                };
            }
            return inner.TransferExact(
                characterId, lease, destination, position, sharesTownPresence);
        }

        TownAreaMovement? ITownAreaDomainService.MoveExact(
            CharacterId characterId,
            TownAreaMembershipLease lease,
            CharacterPositionState position) =>
            inner.MoveExact(characterId, lease, position);

        TownAreaMember? ITownAreaDomainService.Find(CharacterId characterId) =>
            inner.Find(characterId);

        TownAreaSnapshot? ITownAreaDomainService.Find(TownAreaKey key) => inner.Find(key);

        IReadOnlyList<TownAreaSnapshot> ITownAreaDomainService.Query(TownAreaFilter filter) =>
            inner.Query(filter);

        private static TownAreaMember ToMember(SessionPresence presence)
        {
            var channelId = presence.ChannelId > 0 ? presence.ChannelId : presence.ListenerPort switch
            {
                10011 => 11,
                10068 => 68,
                10161 => 100,
                _ => presence.ListenerPort,
            };
            var lease = presence.Session?.TownAreaLease()
                ?? new TownAreaMembershipLease(Math.Max(1, presence.CharacterId.Value));
            var key = new TownAreaKey(channelId, presence.TownId, presence.AreaId);
            return new TownAreaMember(
                presence.CharacterId,
                lease,
                key,
                new CharacterPositionState(presence.TownId, presence.AreaId, 0, 0, 0),
                presence.SharesTownPresence);
        }
    }
}
