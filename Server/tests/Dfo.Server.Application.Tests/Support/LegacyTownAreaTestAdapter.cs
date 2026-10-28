using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
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

        TownAreaMember? ITownAreaDomainService.Find(CharacterId characterId) =>
            Find(characterId) is { StandingInTown: true } value ? LegacyPresence.ToMember(value) : null;

        TownAreaSnapshot? ITownAreaDomainService.Find(TownAreaKey key) =>
            Snapshot(key.ChannelId, key.TownId, key.AreaId);

        IReadOnlyList<TownAreaSnapshot> ITownAreaDomainService.Query(TownAreaFilter filter) =>
            [.. Query(SessionPresenceScope.Global)
                .Where(value => value.StandingInTown)
                .Where(value => !filter.ChannelId.HasValue
                    || LegacyPresence.LogicalChannel(value) == filter.ChannelId)
                .Where(value => !filter.TownId.HasValue || value.TownId == filter.TownId)
                .Where(value => !filter.AreaId.HasValue || value.AreaId == filter.AreaId)
                .GroupBy(value => new TownAreaKey(
                    LegacyPresence.LogicalChannel(value),
                    value.TownId,
                    value.AreaId))
                .Select(group => new TownAreaSnapshot(
                    group.Key,
                    1,
                    [.. group.Select(LegacyPresence.ToMember).OrderBy(value => value.CharacterId.Value)]))];

        private TownAreaSnapshot? Snapshot(int channelId, byte townId, byte areaId)
        {
            var members = Query(SessionPresenceScope.Global)
                .Where(value => value.StandingInTown
                    && LegacyPresence.LogicalChannel(value) == channelId
                    && value.TownId == townId
                    && value.AreaId == areaId)
                .Select(LegacyPresence.ToMember)
                .OrderBy(value => value.CharacterId.Value)
                .ToArray();
            return members.Length == 0
                ? null
                : new TownAreaSnapshot(new TownAreaKey(channelId, townId, areaId), 1, members);
        }
    }

    /// <summary>How a retired presence record reads as a town-area member.</summary>
    public static class LegacyPresence
    {
        /// <summary>
        /// The presence's own session, or for fixtures that never named one a stand-in carrying the
        /// generation the retired lease derived from the character.
        /// </summary>
        public static SelectedSessionIdentity SessionOf(SessionPresence value) =>
            value.Session ?? SelectedSessionIdentity.Create(
                new AccountId(Math.Max(1, value.CharacterId.Value)),
                value.CharacterId,
                new NetworkConnectionId("presence-" + value.CharacterId.Value),
                Math.Max(1, value.CharacterId.Value),
                value.ListenerPort);

        public static TownAreaMember ToMember(SessionPresence value) =>
            new(
                SessionOf(value),
                new TownAreaKey(LogicalChannel(value), value.TownId, value.AreaId),
                new CharacterPositionState(value.TownId, value.AreaId, 0, 0, 0),
                value.SharesTownPresence);

        public static int LogicalChannel(SessionPresence value) =>
            value.ChannelId > 0 ? value.ChannelId : value.ListenerPort switch
            {
                10011 => 11,
                10068 => 68,
                10161 => 100,
                _ => value.ListenerPort,
            };
    }
}

namespace Dfo.Server.Abstractions.World
{
    using Dfo.Server.Abstractions.Characters;
    using Dfo.Server.Abstractions.Sessions;
    using Dfo.Server.Application.World;

    /// <summary>
    /// Places characters for fixtures that arrange who stands where without driving the commands
    /// that move them. Production changes a position only through the online character runtime.
    /// </summary>
    internal static class TownAreaTestPlacement
    {
        public static OnlineCharacterLocationDomainService Locations(this ITownAreaDomainService areas) => areas switch
        {
            OnlineCharacterLocationDomainService locations => locations,
            Dfo.Server.Application.Sessions.SessionPresenceDomainService presence => presence.Locations,
            _ => throw new NotSupportedException("This fixture's town areas cannot be arranged."),
        };

        /// <summary>Stands the member's session in its area, replacing whatever record it had.</summary>
        public static TownAreaMembershipChange Enter(this ITownAreaDomainService areas, TownAreaMember member) =>
            areas.Locations().Begin(member.Session, member.Area, member.Position, member.SharesTownPresence);

        public static TownAreaMembershipChange? TransferExact(
            this ITownAreaDomainService areas,
            SelectedSessionIdentity session,
            TownAreaKey destination,
            CharacterPositionState position,
            bool sharesTownPresence) =>
            areas.Locations().Transfer(session, destination, position, sharesTownPresence);

        /// <summary>Ends the session's record when it ended, otherwise takes it away from town.</summary>
        public static TownAreaMembershipChange? LeaveExact(
            this ITownAreaDomainService areas, SelectedSessionIdentity session, TownAreaDepartureReason reason)
        {
            var locations = areas.Locations();
            if (reason != TownAreaDepartureReason.SessionEnded)
                return locations.Depart(session, reason);
            var standing = locations.StandingOf(session);
            return locations.End(session) && standing is not null
                ? new TownAreaMembershipChange(
                    TownAreaMembershipChangeKind.Left, session.CharacterId, locations.Find(standing.Area), null, reason)
                : null;
        }

        public static TownAreaMovement? MoveExact(
            this ITownAreaDomainService areas, SelectedSessionIdentity session, CharacterPositionState position) =>
            areas.Locations().Step(session, position);

        /// <summary>
        /// Crosses the session's character into another area of its channel, keeping its facing,
        /// and answers where it then is. A character away from town is stood there instead.
        /// </summary>
        public static CharacterPositionState CrossInto(
            this IOnlineCharacterRuntimeDomainService runtime,
            SelectedSessionIdentity session,
            byte townId,
            byte areaId,
            short x,
            short y)
        {
            var channel = runtime.StandingOf(session)?.Area.ChannelId
                ?? LegacyPresence.LogicalChannel(new SessionPresence(session.CharacterId, session.ListenerPort, townId, areaId, true));
            var destination = new TownAreaKey(channel, townId, areaId);
            if (runtime.StandingOf(session) is { } standing)
            {
                runtime.CrossInto(session, destination, x, y, standing.SharesTownPresence);
            }
            else
            {
                var current = runtime.PositionOf(session)
                    ?? throw new InvalidOperationException("The session reporting a movement is not the one standing there.");
                runtime.ReturnToTown(session, destination, current with { TownId = townId, AreaId = areaId, X = x, Y = y }, true);
            }
            return runtime.PositionOf(session)!;
        }

        /// <summary>
        /// Begins a session standing where <paramref name="position"/> is, on the channel its
        /// listener serves, sharing town presence.
        /// </summary>
        public static TownAreaMembershipChange Begin(
            this IOnlineCharacterRuntimeDomainService runtime,
            SelectedSessionIdentity session,
            CharacterPositionState position,
            byte hpPercent) =>
            runtime.Begin(
                session,
                new TownAreaKey(
                    LegacyPresence.LogicalChannel(new SessionPresence(
                        session.CharacterId, session.ListenerPort, position.TownId, position.AreaId, true)),
                    position.TownId,
                    position.AreaId),
                position,
                true,
                hpPercent);
    }
}

namespace Dfo.Server.Application.Sessions
{
    using Dfo.Server.Abstractions.Sessions;

    /// <summary>
    /// Test-only adapter that lets older fixtures feed the location store while production has no
    /// session-presence model or compatibility registration.
    /// </summary>
    internal sealed class SessionPresenceDomainService : ISessionPresenceDomainService
    {
        private readonly Dictionary<CharacterId, SessionPresence> legacy = [];

        public SessionPresenceDomainService(IPartyDirectoryPresenceSink? parties = null)
        {
            Locations = new OnlineCharacterLocationDomainService(parties);
        }

        /// <summary>The store the presences are placed in; an online runtime built over it sees them.</summary>
        public OnlineCharacterLocationDomainService Locations { get; }

        public void Admit(SessionPresence presence)
        {
            legacy[presence.CharacterId] = presence;
            if (!presence.StandingInTown)
                return;
            var member = LegacyPresence.ToMember(presence);
            Locations.Begin(member.Session, member.Area, member.Position, member.SharesTownPresence);
        }

        public bool Withdraw(CharacterId characterId)
        {
            if (!legacy.Remove(characterId, out var current))
                return false;
            Locations.End(LegacyPresence.SessionOf(current));
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
            Locations.End(session);
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
                Locations.Depart(session, TownAreaDepartureReason.DungeonEntry);
                return true;
            }

            var member = LegacyPresence.ToMember(moved);
            if (Locations.PositionOf(session) is null)
                Locations.Begin(session, member.Area, member.Position, member.SharesTownPresence);
            else if (Locations.StandingOf(session) is null)
                Locations.Arrive(session, member.Area, member.Position, member.SharesTownPresence);
            else
                Locations.Transfer(session, member.Area, member.Position, member.SharesTownPresence);
            return true;
        }

        /// <summary>
        /// The admitted presence as the location store now has it: the runtime writes there
        /// directly, so a record it moved or ended is read back from the store, not the admission.
        /// </summary>
        public SessionPresence? Find(CharacterId characterId)
        {
            if (!legacy.TryGetValue(characterId, out var admitted))
                return null;
            var session = LegacyPresence.SessionOf(admitted);
            if (Locations.StandingOf(session) is { } member)
                return admitted with
                {
                    TownId = member.Area.TownId,
                    AreaId = member.Area.AreaId,
                    StandingInTown = true,
                    SharesTownPresence = member.SharesTownPresence,
                };
            if (Locations.PositionOf(session) is not null)
                return admitted with { StandingInTown = false };
            // Admitted away from town, it never had a record to end.
            return admitted.StandingInTown ? null : admitted;
        }

        public IReadOnlyList<SessionPresence> Query(SessionPresenceScope scope) =>
            [.. legacy.Keys.Select(Find).OfType<SessionPresence>()
                .Where(value => value.CharacterId != scope.ExcludingCharacterId)
                .Where(value => !scope.Flags.HasFlag(SessionPresenceScopeFlags.SameChannel)
                    || value.ListenerPort == scope.ListenerPort)
                .Where(value => !scope.Flags.HasFlag(SessionPresenceScopeFlags.SameTown)
                    || value.TownId == scope.TownId)
                .Where(value => !scope.Flags.HasFlag(SessionPresenceScopeFlags.SameArea)
                    || value.AreaId == scope.AreaId)
                .Where(value => !scope.Flags.HasFlag(SessionPresenceScopeFlags.StandingInTown)
                    || value.StandingInTown)
                .Where(value => !scope.Flags.HasFlag(SessionPresenceScopeFlags.SharesTownPresence)
                    || value.SharesTownPresence)
                .OrderBy(value => value.CharacterId.Value)];

        TownAreaMember? ITownAreaDomainService.Find(CharacterId characterId) =>
            Locations.Find(characterId);

        TownAreaSnapshot? ITownAreaDomainService.Find(TownAreaKey key) => Locations.Find(key);

        IReadOnlyList<TownAreaSnapshot> ITownAreaDomainService.Query(TownAreaFilter filter) =>
            Locations.Query(filter);
    }
}
