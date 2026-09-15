using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Party;

namespace Dfo.Server.Protocol.Game.TownProjection;

/// <summary>Serializes projections that can redraw the same logical town area.</summary>
public sealed class TownAreaProjectionGate : UserExecutionGate<TownAreaKey>;

/// <summary>A committed return admission whose wire projection has not yet been delivered.</summary>
public sealed record TownReturnAdmission(
    SelectedSessionIdentity Session,
    SelectCharacterResult Runtime,
    Dfo.Server.Data.Characters.CharacterPositionState Position,
    TownAreaMembershipChange Change,
    PartyDirectoryScope? PreviousScope);

/// <summary>A committed departure whose roster and party projections are still pending.</summary>
public sealed record TownAreaDeparture(
    SelectedSessionIdentity Session,
    TownAreaMembershipChange Change,
    TownAreaKey PreviousArea,
    bool SharedTownPresence,
    PartyDirectoryScope? PreviousScope);

/// <summary>The protocol boundary that turns committed town-area changes into client projections.</summary>
/// <remarks>
/// Every delivery takes the destination area's projection gate and re-reads its current snapshot.
/// A delayed older change can therefore only redraw the newest state, never an older roster.
/// </remarks>
public sealed class TownAreaProjectionDelivery
{
    private readonly ITownAreaDomainService townAreas;
    private readonly ISelectedSessionDomainService selectedSessions;
    private readonly ISelectedSessionRuntimeDirectory runtimes;
    private readonly INotificationBus notifications;
    private readonly IPartyTownProjectionDelivery partyTownProjection;
    private readonly IGameMessageSender sender;
    private readonly TownAreaProjectionGate projectionGate;

    public TownAreaProjectionDelivery(
        ITownAreaDomainService townAreas,
        ISelectedSessionDomainService selectedSessions,
        ISelectedSessionRuntimeDirectory runtimes,
        INotificationBus notifications,
        IPartyTownProjectionDelivery partyTownProjection,
        IGameMessageSender sender,
        TownAreaProjectionGate? projectionGate = null)
    {
        this.townAreas = townAreas;
        this.selectedSessions = selectedSessions;
        this.runtimes = runtimes;
        this.notifications = notifications;
        this.partyTownProjection = partyTownProjection;
        this.sender = sender;
        this.projectionGate = projectionGate ?? new TownAreaProjectionGate();
    }

    public async ValueTask ProjectTransferAsync(
        NetworkConnectionId connectionId,
        SelectedSessionIdentity session,
        SelectCharacterResult result,
        Dfo.Server.Data.Characters.CharacterPositionState moved,
        TownAreaMember previousMember,
        TownAreaMembershipChange change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(moved);
        ArgumentNullException.ThrowIfNull(previousMember);
        ArgumentNullException.ThrowIfNull(change);

        var keys = new[] { change.PreviousArea?.Key, change.CurrentArea?.Key }
            .Where(key => key.HasValue)
            .Select(key => key!.Value);
        await using var projection = await AcquireAreasAsync(keys, cancellationToken)
            .ConfigureAwait(false);

        var changedArea = change.PreviousArea?.Key != change.CurrentArea?.Key;
        var currentKey = change.CurrentArea?.Key
            ?? throw new InvalidDataException("An area transfer has no destination snapshot.");
        var current = townAreas.Find(currentKey);
        var own = current?.Members.SingleOrDefault(member => session.Owns(member));
        if (own is null)
        {
            if (changedArea
                && previousMember.SharesTownPresence
                && change.PreviousArea is { } abandonedPrevious)
            {
                await PublishCurrentRosterCoreAsync(abandonedPrevious.Key, cancellationToken)
                    .ConfigureAwait(false);
            }
            return;
        }

        var visible = VisibleParticipants(current!, session);
        var peers = visible.Select(candidate => candidate.Participant).ToArray();
        var peerSessions = visible.Select(candidate => candidate.Session).ToArray();
        var currentPosition = own.Position;
        var spawn = result.World.Channel.Spawn with
        {
            TownId = currentPosition.TownId,
            AreaId = currentPosition.AreaId,
            X = currentPosition.X,
            Y = currentPosition.Y,
            Direction = currentPosition.Direction,
            SharesTownPresence = own.SharesTownPresence,
        };
        var state = peers.Length == 0
            ? null
            : new TownPresenceSynchronizationState(
                currentPosition.TownId,
                currentPosition.AreaId,
                TownReturnProjection.Participant(result, currentPosition),
                peers);
        IReadOnlyList<IGameServerMessage> direct = state is null
            ?
            [
                new UserAreaNotification(session.WireUserId, spawn, currentPosition.AreaId),
                new AreaUsersNotification(
                    currentPosition.TownId,
                    currentPosition.AreaId,
                    [new AreaUserPresence(
                        session.WireUserId,
                        currentPosition.X,
                        currentPosition.Y,
                        currentPosition.Direction,
                        spawn.AreaState)]),
            ]
            :
            [
                new UserAreaNotification(session.WireUserId, spawn, currentPosition.AreaId),
                .. TownPresenceMessages.ForNewcomer(state),
            ];
        await sender.SendExplicitAsync(
            connectionId,
            new GameExplicitMessageDelivery(direct, []),
            cancellationToken).ConfigureAwait(false);

        if (changedArea && previousMember.SharesTownPresence && change.PreviousArea is { } previous)
            await PublishCurrentRosterCoreAsync(previous.Key, cancellationToken).ConfigureAwait(false);
        if (changedArea && peerSessions.Length > 0 && state is not null)
            await PublishArrivalCoreAsync(session, state, peerSessions, cancellationToken)
                .ConfigureAwait(false);
        if (peerSessions.Length > 0)
        {
            await partyTownProjection.RefreshObservedPartiesAsync(
                [session], peerSessions, cancellationToken).ConfigureAwait(false);
        }
        if (changedArea)
        {
            await partyTownProjection.RefreshMovedPartyAsync(
                session, Scope(previousMember.Area), cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask ProjectMovementAsync(
        NetworkConnectionId connectionId,
        SelectedSessionIdentity session,
        TownAreaSnapshot? area,
        short x,
        short y,
        byte direction,
        ushort trailer,
        CancellationToken cancellationToken)
    {
        if (area is null)
        {
            await sender.SendNotiAsync(
                connectionId,
                new UserPositionNotification(session.WireUserId, x, y, direction, trailer),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var projection = await AcquireAreasAsync([area.Key], cancellationToken)
            .ConfigureAwait(false);
        var current = townAreas.Find(area.Key);
        var own = current?.Members.SingleOrDefault(member => session.Owns(member));
        if (own is null)
            return;

        await sender.SendNotiAsync(
            connectionId,
            new UserPositionNotification(
                session.WireUserId,
                own.Position.X,
                own.Position.Y,
                own.Position.Direction,
                trailer),
            cancellationToken).ConfigureAwait(false);
        if (!own.SharesTownPresence)
            return;

        var audience = current!.Members
            .Where(member => member.SharesTownPresence
                && member.CharacterId != session.CharacterId)
            .Select(ExactSession)
            .Where(candidate => candidate is not null)
            .Cast<SelectedSessionIdentity>()
            .ToArray();
        if (audience.Length > 0)
        {
            await notifications.PublishAsync(
                new NotificationAudience.TheseSessions(audience),
                new TownPositionChanged(
                    session.WireUserId,
                    own.Position.X,
                    own.Position.Y,
                    own.Position.Direction,
                    trailer),
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<TownAreaMembershipChange?> LeaveAsync(
        SelectedSessionIdentity session,
        TownAreaDepartureReason reason,
        CancellationToken cancellationToken)
    {
        var departure = TryLeave(session, reason);
        if (departure is null)
            return null;

        await ProjectDepartureAsync(departure, cancellationToken).ConfigureAwait(false);
        return departure.Change;
    }

    /// <summary>Commits only the area fact; callers can batch several departures atomically.</summary>
    public TownAreaDeparture? TryLeave(
        SelectedSessionIdentity session,
        TownAreaDepartureReason reason)
    {
        ArgumentNullException.ThrowIfNull(session);
        var current = townAreas.Find(session.CharacterId);
        if (current is null || !session.Owns(current))
            return null;
        var previousScope = current.SharesTownPresence ? Scope(current.Area) : null;
        var change = townAreas.LeaveExact(
            session.CharacterId,
            session.TownAreaLease(),
            reason);
        return change is null
            ? null
            : new TownAreaDeparture(
                session,
                change,
                current.Area,
                current.SharesTownPresence,
                previousScope);
    }

    public async ValueTask ProjectDepartureAsync(
        TownAreaDeparture departure,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(departure);
        await using var projection = await AcquireAreasAsync(
            [departure.PreviousArea], cancellationToken)
            .ConfigureAwait(false);
        if (departure.SharedTownPresence)
        {
            await PublishCurrentRosterCoreAsync(departure.PreviousArea, cancellationToken)
                .ConfigureAwait(false);
        }
        await partyTownProjection.RefreshMovedPartyAsync(
            departure.Session, departure.PreviousScope, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Commits a town return while the caller owns the account transition gate.</summary>
    public TownReturnAdmission? TryAdmitForTownReturn(
        SelectedSessionIdentity session,
        SelectCharacterResult result,
        Dfo.Server.Data.Characters.CharacterPositionState position)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(position);
        if (selectedSessions.FindByCharacter(session.CharacterId) != session)
            return null;

        var previous = townAreas.Find(session.CharacterId);
        var previousScope = previous is { SharesTownPresence: true }
            ? Scope(previous.Area)
            : null;
        var member = new TownAreaMember(
            session.CharacterId,
            session.TownAreaLease(),
            new TownAreaKey(result.World.Channel.ChannelId, position.TownId, position.AreaId),
            position,
            result.World.Channel.Spawn.SharesTownPresence);
        return new TownReturnAdmission(
            session,
            result,
            position,
            townAreas.Enter(member),
            previousScope);
    }

    public async ValueTask<bool> SendTownReturnAsync(
        TownReturnAdmission admission,
        IReadOnlyList<IGameServerMessage> prefix,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admission);
        var key = admission.Change.CurrentArea?.Key
            ?? throw new InvalidDataException("A town return did not produce a destination area.");
        await using var projection = await AcquireAreasAsync([key], cancellationToken)
            .ConfigureAwait(false);
        var current = townAreas.Find(key);
        var own = current?.Members.SingleOrDefault(member => admission.Session.Owns(member));
        if (own is null)
            return false;

        var peers = VisibleParticipants(current!, admission.Session);
        var frames = TownReturnProjection.Frames(
            admission.Session,
            admission.Runtime,
            own.Position,
            peers.Select(candidate => candidate.Participant).ToArray());
        await sender.SendExplicitAsync(
            admission.Session.ConnectionId,
            new GameExplicitMessageDelivery([.. prefix, .. frames], []),
            cancellationToken).ConfigureAwait(false);
        await PublishTownReturnCoreAsync(admission, current!, peers, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> SendTownReturnBatchAsync(
        NetworkConnectionId requesterConnectionId,
        SelectedSessionIdentity requester,
        IReadOnlyList<TownReturnAdmission> admissions,
        IReadOnlyList<IGameServerMessage> requesterPrefix,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(admissions);
        var keys = admissions.Select(admission => admission.Change.CurrentArea?.Key
            ?? throw new InvalidDataException("A town return has no destination area."));
        await using var projection = await AcquireAreasAsync(keys, cancellationToken)
            .ConfigureAwait(false);

        var deliveries = new List<ReturnDelivery>();
        foreach (var admission in admissions)
        {
            var current = townAreas.Find(admission.Change.CurrentArea!.Key);
            var own = current?.Members.SingleOrDefault(member => admission.Session.Owns(member));
            if (own is null)
                continue;
            var peers = VisibleParticipants(current!, admission.Session);
            deliveries.Add(new ReturnDelivery(
                admission,
                current!,
                peers,
                TownReturnProjection.Frames(
                    admission.Session,
                    admission.Runtime,
                    own.Position,
                    peers.Select(candidate => candidate.Participant).ToArray())));
        }

        var direct = deliveries.SingleOrDefault(value => value.Admission.Session == requester);
        if (direct is null)
            return false;
        await sender.SendExplicitAsync(
            requesterConnectionId,
            new GameExplicitMessageDelivery(
                [.. requesterPrefix, .. direct.Frames],
                [.. deliveries
                    .Where(value => value.Admission.Session != requester)
                    .Select(value => new GameExplicitMessageTarget(
                        value.Admission.Session.ConnectionId,
                        value.Frames))]),
            cancellationToken).ConfigureAwait(false);

        var returningCharacters = admissions.Select(value => value.Session.CharacterId).ToHashSet();
        foreach (var delivery in deliveries)
        {
            var preExistingPeers = delivery.Peers
                .Where(peer => !returningCharacters.Contains(peer.Session.CharacterId))
                .ToArray();
            await PublishTownReturnCoreAsync(
                delivery.Admission,
                delivery.Area,
                preExistingPeers,
                cancellationToken).ConfigureAwait(false);
        }
        return true;
    }

    public async ValueTask PublishRosterAsync(
        TownAreaSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await using var projection = await AcquireAreasAsync([snapshot.Key], cancellationToken)
            .ConfigureAwait(false);
        await PublishCurrentRosterCoreAsync(snapshot.Key, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask PublishTownReturnCoreAsync(
        TownReturnAdmission admission,
        TownAreaSnapshot current,
        IReadOnlyList<(SelectedSessionIdentity Session, TownParticipantEntryState Participant)> peers,
        CancellationToken cancellationToken)
    {
        var peerSessions = peers.Select(candidate => candidate.Session).ToArray();
        var own = current.Members.Single(member => admission.Session.Owns(member));
        if (admission.Change.Kind != TownAreaMembershipChangeKind.Unchanged
            && own.SharesTownPresence
            && peerSessions.Length > 0)
        {
            var state = new TownPresenceSynchronizationState(
                own.Position.TownId,
                own.Position.AreaId,
                TownReturnProjection.Participant(admission.Runtime, own.Position),
                peers.Select(candidate => candidate.Participant).ToArray());
            await PublishArrivalCoreAsync(
                admission.Session, state, peerSessions, cancellationToken).ConfigureAwait(false);
        }
        if (peerSessions.Length > 0)
        {
            await partyTownProjection.RefreshObservedPartiesAsync(
                [admission.Session], peerSessions, cancellationToken).ConfigureAwait(false);
        }
        if (admission.Change.Kind != TownAreaMembershipChangeKind.Unchanged)
        {
            await partyTownProjection.RefreshMovedPartyAsync(
                admission.Session, admission.PreviousScope, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask PublishArrivalCoreAsync(
        SelectedSessionIdentity newcomer,
        TownPresenceSynchronizationState state,
        IReadOnlyList<SelectedSessionIdentity> peerSessions,
        CancellationToken cancellationToken)
    {
        var publication = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            notifications,
            peerSessions,
            new TownParticipantPublished(state),
            cancellationToken).ConfigureAwait(false);
        if (!publication.Delivered.IsEmpty)
        {
            await partyTownProjection.RefreshObservedPartiesAsync(
                publication.Delivered, [newcomer], cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask PublishCurrentRosterCoreAsync(
        TownAreaKey key,
        CancellationToken cancellationToken)
    {
        var snapshot = townAreas.Find(key);
        if (snapshot is null)
            return;

        foreach (var member in snapshot.Members.Where(candidate => candidate.SharesTownPresence))
        {
            var recipient = ExactSession(member);
            if (recipient is null)
                continue;
            var visible = snapshot.VisibleTo(member.CharacterId);
            var ownIndex = visible.ToList().FindIndex(candidate =>
                candidate.CharacterId == member.CharacterId);
            if (ownIndex < 0)
                continue;

            var ordered = new List<TownAreaMember>(visible.Count) { visible[ownIndex] };
            for (var index = 0; index < visible.Count; index++)
            {
                if (index != ownIndex)
                    ordered.Add(visible[index]);
            }

            var users = new List<AreaUserPresence>(ordered.Count);
            foreach (var occupant in ordered)
            {
                var exact = ExactSession(occupant);
                if (exact is null
                    || !runtimes.TryGet(exact, out var runtime, out _)
                    || runtime is null)
                {
                    continue;
                }
                users.Add(new AreaUserPresence(
                    exact.WireUserId,
                    occupant.Position.X,
                    occupant.Position.Y,
                    occupant.Position.Direction,
                    runtime.World.Channel.Spawn.AreaState));
            }

            await notifications.PublishAsync(
                new NotificationAudience.TheseSessions([recipient]),
                new TownAreaRosterReconciled(key.TownId, key.AreaId, users),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private IReadOnlyList<(SelectedSessionIdentity Session, TownParticipantEntryState Participant)>
        VisibleParticipants(TownAreaSnapshot area, SelectedSessionIdentity viewer) =>
        [.. area.VisibleTo(viewer.CharacterId)
            .Where(member => member.CharacterId != viewer.CharacterId)
            .Select(TryCreateParticipant)
            .Where(candidate => candidate.Session is not null && candidate.Participant is not null)
            .Select(candidate => (candidate.Session!, candidate.Participant!))];

    private (SelectedSessionIdentity? Session, TownParticipantEntryState? Participant)
        TryCreateParticipant(TownAreaMember member)
    {
        var session = ExactSession(member);
        if (session is null
            || !runtimes.TryGet(session, out var runtime, out _)
            || runtime is null)
        {
            return (null, null);
        }
        return (session, TownReturnProjection.Participant(runtime, member.Position));
    }

    private SelectedSessionIdentity? ExactSession(TownAreaMember member)
    {
        var session = selectedSessions.FindByCharacter(member.CharacterId);
        return session is not null && session.Owns(member) ? session : null;
    }

    private async ValueTask<IAsyncDisposable> AcquireAreasAsync(
        IEnumerable<TownAreaKey> keys,
        CancellationToken cancellationToken)
    {
        var leases = new List<IAsyncDisposable>();
        try
        {
            foreach (var key in keys.Distinct().OrderBy(KeyOrder))
            {
                leases.Add(await projectionGate.AcquireAsync(
                    key, UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false));
            }
            return new ProjectionLeaseSet(leases);
        }
        catch
        {
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static long KeyOrder(TownAreaKey key) =>
        ((long)key.ChannelId << 16) | ((long)key.TownId << 8) | key.AreaId;

    private static PartyDirectoryScope Scope(TownAreaKey key) =>
        new(key.ChannelId, key.TownId, key.AreaId);

    private sealed record ReturnDelivery(
        TownReturnAdmission Admission,
        TownAreaSnapshot Area,
        IReadOnlyList<(SelectedSessionIdentity Session, TownParticipantEntryState Participant)> Peers,
        IReadOnlyList<IGameServerMessage> Frames);

    private sealed class ProjectionLeaseSet(IReadOnlyList<IAsyncDisposable> leases)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
        }
    }
}
