using Dfo.Server.Abstractions.DomainEvents;
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

/// <summary>One selected character asking the area service to enter its current town location.</summary>
public sealed record TownAreaEntry(
    SelectedSessionIdentity Session,
    SelectCharacterResult Runtime,
    Dfo.Server.Data.Characters.CharacterPositionState Position);

internal sealed record TownAreaAdmission(
    TownAreaEntry Entry,
    TownAreaMembershipChange Change,
    PartyDirectoryScope? PreviousScope);

/// <summary>A committed departure whose roster and party projections are still pending.</summary>
public sealed record TownAreaDeparture(
    SelectedSessionIdentity Session,
    TownAreaMembershipChange Change,
    TownAreaKey PreviousArea,
    bool SharedTownPresence,
    PartyDirectoryScope? PreviousScope);

/// <summary>The area boundary that commits town membership changes and projects their latest state.</summary>
/// <remarks>
/// Every delivery takes the destination area's projection gate and re-reads its current snapshot.
/// A delayed older change can therefore only redraw the newest state, never an older roster.
/// </remarks>
public sealed class TownAreaProjectionDelivery
{
    private readonly ITownAreaDomainService townAreas;
    private readonly ISelectedSessionDomainService selectedSessions;
    private readonly ISelectedCharacterCurrentViewDomainService currentViews;
    private readonly ITownMovementDomainService towns;
    private readonly IEventService notifications;
    private readonly IPartyTownProjectionDelivery partyTownProjection;
    private readonly IGameMessageSender sender;
    private readonly TownAreaProjectionGate projectionGate;
    private readonly Dfo.Server.Protocol.Game.Weakness.WeaknessProjectionDelivery? weaknessProjection;

    public TownAreaProjectionDelivery(
        ITownAreaDomainService townAreas,
        ISelectedSessionDomainService selectedSessions,
        ISelectedCharacterCurrentViewDomainService currentViews,
        IEventService notifications,
        IPartyTownProjectionDelivery partyTownProjection,
        IGameMessageSender sender,
        ITownMovementDomainService towns,
        TownAreaProjectionGate? projectionGate = null,
        Dfo.Server.Protocol.Game.Weakness.WeaknessProjectionDelivery? weaknessProjection = null)
    {
        this.townAreas = townAreas;
        this.selectedSessions = selectedSessions;
        this.currentViews = currentViews;
        this.towns = towns;
        this.notifications = notifications;
        this.partyTownProjection = partyTownProjection;
        this.sender = sender;
        this.projectionGate = projectionGate ?? new TownAreaProjectionGate();
        this.weaknessProjection = weaknessProjection;
    }

    /// <summary>Restores the accepted location after a refused channel-boundary crossing.</summary>
    public ValueTask RefuseTownChangeAsync(NetworkConnectionId connectionId, SelectedSessionIdentity session,
        SelectCharacterResult result, Dfo.Server.Data.Characters.CharacterPositionState standing,
        string reason, CancellationToken cancellationToken)
    {
        var spawn = result.World.Channel.Spawn with
        {
            TownId = standing.TownId, AreaId = standing.AreaId,
            X = standing.X, Y = standing.Y, Direction = standing.Direction,
        };
        return sender.SendExplicitAsync(connectionId, new GameExplicitMessageDelivery(
            [new UserAreaNotification(session.WireUserId, spawn, standing.AreaId),
             new GameServerNoticeMessage(reason)], []), cancellationToken);
    }

    public async ValueTask<bool> TransferAsync(
        NetworkConnectionId connectionId,
        SelectedSessionIdentity session,
        SelectCharacterResult result,
        int channelId,
        Dfo.Server.Data.Characters.CharacterPositionState moved,
        TownAreaMember previousMember,
        bool sharesTownPresence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(moved);
        ArgumentNullException.ThrowIfNull(previousMember);
        var destination = new TownAreaKey(
            channelId,
            moved.TownId,
            moved.AreaId);
        var change = townAreas.TransferExact(
            session.CharacterId,
            session.TownAreaLease(),
            destination,
            moved,
            sharesTownPresence);
        if (change is null)
            return false;

        return await ProjectTransferAsync(connectionId, session, result, previousMember, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Projects an already committed town movement without mutating membership.</summary>
    public async ValueTask<bool> ProjectTransferAsync(NetworkConnectionId connectionId, SelectedSessionIdentity session,
        SelectCharacterResult result, TownAreaMember previousMember, CancellationToken cancellationToken)
    {
        var member = townAreas.Find(session.CharacterId);
        if (member is null || !session.Owns(member)) return false;
        var keys = new[] { previousMember.Area, member.Area };
        await using var projection = await AcquireAreasAsync(keys, cancellationToken)
            .ConfigureAwait(false);

        var changedArea = previousMember.Area != member.Area;
        var currentKey = member.Area;
        var current = townAreas.Find(currentKey);
        var own = current?.Members.SingleOrDefault(member => session.Owns(member));
        if (own is null)
        {
            if (changedArea
                && previousMember.SharesTownPresence)
            {
                await PublishCurrentRosterCoreAsync(previousMember.Area, cancellationToken)
                    .ConfigureAwait(false);
            }
            return false;
        }

        var visible = await VisibleParticipantsAsync(current!, session, cancellationToken)
            .ConfigureAwait(false);
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

        if (changedArea && previousMember.SharesTownPresence)
            await PublishCurrentRosterCoreAsync(previousMember.Area, cancellationToken).ConfigureAwait(false);
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
        return true;
    }

    public async ValueTask<bool> MoveAsync(
        NetworkConnectionId connectionId,
        SelectedSessionIdentity session,
        Dfo.Server.Data.Characters.CharacterPositionState position,
        ushort trailer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(position);
        var previous = townAreas.Find(session.CharacterId);
        if (previous is null || !session.Owns(previous))
        {
            await sender.SendNotiAsync(
                connectionId,
                new UserPositionNotification(
                    session.WireUserId,
                    position.X,
                    position.Y,
                    position.Direction,
                    trailer),
                cancellationToken).ConfigureAwait(false);
            return false;
        }
        var movement = townAreas.MoveExact(
            session.CharacterId,
            session.TownAreaLease(),
            position);
        var area = movement?.Area ?? townAreas.Find(previous.Area);
        if (area is null)
        {
            await sender.SendNotiAsync(
                connectionId,
                new UserPositionNotification(
                    session.WireUserId,
                    position.X,
                    position.Y,
                    position.Direction,
                    trailer),
                cancellationToken).ConfigureAwait(false);
            return false;
        }

        await using var projection = await AcquireAreasAsync([area.Key], cancellationToken)
            .ConfigureAwait(false);
        var current = townAreas.Find(area.Key);
        var own = current?.Members.SingleOrDefault(member => session.Owns(member));
        if (own is null)
            return false;

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
            return true;

        var audience = current!.Members
            .Where(member => member.SharesTownPresence
                && member.CharacterId != session.CharacterId)
            .Select(ExactSession)
            .Where(candidate => candidate is not null)
            .Cast<SelectedSessionIdentity>()
            .ToArray();
        if (audience.Length > 0)
        {
            await notifications.ProjectAsync(
                new NotificationAudience.TheseSessions(audience),
                new TownPositionChanged(
                    session.WireUserId,
                    own.Position.X,
                    own.Position.Y,
                    own.Position.Direction,
                    trailer),
                cancellationToken).ConfigureAwait(false);
        }
        return true;
    }

    public async ValueTask<TownAreaMembershipChange?> LeaveAsync(
        SelectedSessionIdentity session,
        TownAreaDepartureReason reason,
        CancellationToken cancellationToken)
    {
        var changes = await LeaveManyAsync([session], reason, cancellationToken)
            .ConfigureAwait(false);
        return changes.SingleOrDefault();
    }

    /// <summary>Removes every exact member before publishing each area's final revision.</summary>
    public async ValueTask<IReadOnlyList<TownAreaMembershipChange>> LeaveManyAsync(
        IReadOnlyList<SelectedSessionIdentity> sessions,
        TownAreaDepartureReason reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var departures = new List<TownAreaDeparture>(sessions.Count);
        foreach (var session in sessions.Distinct())
        {
            ArgumentNullException.ThrowIfNull(session);
            var current = townAreas.Find(session.CharacterId);
            if (current is null || !session.Owns(current))
                continue;
            var previousScope = current.SharesTownPresence ? Scope(current.Area) : null;
            var change = townAreas.LeaveExact(
                session.CharacterId,
                session.TownAreaLease(),
                reason);
            if (change is not null)
            {
                departures.Add(new TownAreaDeparture(
                    session,
                    change,
                    current.Area,
                    current.SharesTownPresence,
                    previousScope));
            }
        }
        if (departures.Count == 0)
            return [];

        await using var projection = await AcquireAreasAsync(
            departures.Select(departure => departure.PreviousArea), cancellationToken)
            .ConfigureAwait(false);
        foreach (var area in departures
            .Where(departure => departure.SharedTownPresence)
            .Select(departure => departure.PreviousArea)
            .Distinct())
        {
            await PublishCurrentRosterCoreAsync(area, cancellationToken)
                .ConfigureAwait(false);
        }
        foreach (var departure in departures)
        {
            await partyTownProjection.RefreshMovedPartyAsync(
                departure.Session, departure.PreviousScope, cancellationToken).ConfigureAwait(false);
        }
        return departures.Select(departure => departure.Change).ToArray();
    }

    /// <summary>
    /// Admits one or more characters and projects the resulting area revision. Callers describe
    /// only where the characters are; the destination area's content policy and every visibility
    /// notification remain owned here.
    /// </summary>
    public async ValueTask<int> EnterAsync(
        IReadOnlyList<TownAreaEntry> entries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var candidates = new List<(TownAreaEntry Entry, bool SharesTownPresence)>();
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (selectedSessions.FindByCharacter(entry.Session.CharacterId) != entry.Session)
                continue;
            var sharesTownPresence = await towns.SharesTownPresenceAsync(
                entry.Position.TownId,
                entry.Position.AreaId,
                cancellationToken).ConfigureAwait(false);
            candidates.Add((entry, sharesTownPresence));
        }
        if (candidates.Count == 0)
            return 0;

        var keys = candidates.Select(candidate => new TownAreaKey(
                candidate.Entry.Runtime.World.Channel.ChannelId,
                candidate.Entry.Position.TownId,
                candidate.Entry.Position.AreaId))
            .Concat(candidates
                .Select(candidate => townAreas.Find(candidate.Entry.Session.CharacterId))
                .Where(member => member is not null
                    && selectedSessions.FindByCharacter(member.CharacterId) is { } exact
                    && exact.Owns(member))
                .Select(member => member!.Area));
        await using var projection = await AcquireAreasAsync(keys, cancellationToken)
            .ConfigureAwait(false);

        var admissions = new List<TownAreaAdmission>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var entry = candidate.Entry;
            if (selectedSessions.FindByCharacter(entry.Session.CharacterId) != entry.Session)
                continue;
            var previous = townAreas.Find(entry.Session.CharacterId);
            var previousScope = previous is { SharesTownPresence: true }
                ? Scope(previous.Area)
                : null;
            var member = new TownAreaMember(
                entry.Session.CharacterId,
                entry.Session.TownAreaLease(),
                new TownAreaKey(
                    entry.Runtime.World.Channel.ChannelId,
                    entry.Position.TownId,
                    entry.Position.AreaId),
                entry.Position,
                candidate.SharesTownPresence);
            admissions.Add(new TownAreaAdmission(
                entry,
                townAreas.Enter(member),
                previousScope));
        }

        var deliveries = new List<ReturnDelivery>();
        foreach (var admission in admissions)
        {
            var current = townAreas.Find(admission.Change.CurrentArea!.Key);
            var own = current?.Members.SingleOrDefault(
                member => admission.Entry.Session.Owns(member));
            if (own is null)
                continue;
            var peers = await VisibleParticipantsAsync(
                current!, admission.Entry.Session, cancellationToken).ConfigureAwait(false);
            deliveries.Add(new ReturnDelivery(
                admission,
                current!,
                peers,
                TownReturnProjection.Frames(
                    admission.Entry.Session,
                    admission.Entry.Runtime,
                    own.Position,
                    peers.Select(candidate => candidate.Participant).ToArray())));
        }

        foreach (var delivery in deliveries)
        {
            await sender.SendExplicitAsync(
                delivery.Admission.Entry.Session.ConnectionId,
                new GameExplicitMessageDelivery(delivery.Frames, []),
                cancellationToken).ConfigureAwait(false);
            if (weaknessProjection is not null)
                await weaknessProjection.SendCurrentAsync(delivery.Admission.Entry.Session, cancellationToken).ConfigureAwait(false);
        }

        foreach (var previousArea in admissions
            .Where(admission => admission.Change.PreviousArea is { } previous
                && previous.Key != admission.Change.CurrentArea?.Key)
            .Select(admission => admission.Change.PreviousArea!.Key)
            .Distinct())
        {
            await PublishCurrentRosterCoreAsync(previousArea, cancellationToken)
                .ConfigureAwait(false);
        }

        var returningCharacters = admissions
            .Select(value => value.Entry.Session.CharacterId)
            .ToHashSet();
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
        return deliveries.Count;
    }

    /// <summary>Projects an entry already committed by the selected-character lifecycle.</summary>
    public async ValueTask PublishCommittedEntryAsync(
        SelectedSessionIdentity session,
        TownParticipantPublicationPlan publication,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(publication);
        if (selectedSessions.FindByCharacter(session.CharacterId) != session)
            return;
        await sender.SendExplicitAsync(
            session.ConnectionId,
            new GameExplicitMessageDelivery(
                TownPresenceMessages.ForNewcomer(publication.State), []),
            cancellationToken).ConfigureAwait(false);
        if (selectedSessions.FindByCharacter(session.CharacterId) != session)
            return;
        await partyTownProjection.RefreshObservedPartiesAsync(
            [session], publication.Audience, cancellationToken).ConfigureAwait(false);
        if (selectedSessions.FindByCharacter(session.CharacterId) != session)
            return;
        var delivered = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            notifications,
            publication.Audience,
            new TownParticipantPublished(publication.State),
            cancellationToken).ConfigureAwait(false);
        if (!delivered.Delivered.IsEmpty)
        {
            await partyTownProjection.RefreshObservedPartiesAsync(
                delivered.Delivered, [session], cancellationToken).ConfigureAwait(false);
        }
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
        TownAreaAdmission admission,
        TownAreaSnapshot current,
        IReadOnlyList<(SelectedSessionIdentity Session, TownParticipantEntryState Participant)> peers,
        CancellationToken cancellationToken)
    {
        var peerSessions = peers.Select(candidate => candidate.Session).ToArray();
        var own = current.Members.Single(member => admission.Entry.Session.Owns(member));
        if (admission.Change.Kind != TownAreaMembershipChangeKind.Unchanged
            && own.SharesTownPresence
            && peerSessions.Length > 0)
        {
            var state = new TownPresenceSynchronizationState(
                own.Position.TownId,
                own.Position.AreaId,
                TownReturnProjection.Participant(admission.Entry.Runtime, own.Position),
                peers.Select(candidate => candidate.Participant).ToArray());
            await PublishArrivalCoreAsync(
                admission.Entry.Session, state, peerSessions, cancellationToken).ConfigureAwait(false);
        }
        if (peerSessions.Length > 0)
        {
            await partyTownProjection.RefreshObservedPartiesAsync(
                [admission.Entry.Session], peerSessions, cancellationToken).ConfigureAwait(false);
        }
        if (admission.Change.Kind != TownAreaMembershipChangeKind.Unchanged)
        {
            await partyTownProjection.RefreshMovedPartyAsync(
                admission.Entry.Session, admission.PreviousScope, cancellationToken).ConfigureAwait(false);
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

        // A roster redraw may project the same occupant to every viewer. Keep the
        // current view only for this redraw and for this exact session generation.
        var areaStates = new Dictionary<SelectedSessionIdentity, byte?>();

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
                if (exact is null)
                    continue;

                if (!areaStates.TryGetValue(exact, out var areaState))
                {
                    var runtime = await currentViews.TryLoadAsync(exact, cancellationToken)
                        .ConfigureAwait(false);
                    areaState = runtime?.World?.Channel?.Spawn is { } spawn
                        ? spawn.AreaState
                        : null;
                    areaStates.Add(exact, areaState);
                }
                if (areaState is not { } state)
                    continue;

                users.Add(new AreaUserPresence(
                    exact.WireUserId,
                    occupant.Position.X,
                    occupant.Position.Y,
                    occupant.Position.Direction,
                    state));
            }

            await notifications.ProjectAsync(
                new NotificationAudience.TheseSessions([recipient]),
                new TownAreaRosterReconciled(key.TownId, key.AreaId, users),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, TownParticipantEntryState Participant)>>
        VisibleParticipantsAsync(TownAreaSnapshot area, SelectedSessionIdentity viewer,
            CancellationToken cancellationToken)
    {
        var result = new List<(SelectedSessionIdentity, TownParticipantEntryState)>();
        foreach (var member in area.VisibleTo(viewer.CharacterId)
                     .Where(member => member.CharacterId != viewer.CharacterId))
        {
            var candidate = await TryCreateParticipantAsync(member, cancellationToken)
                .ConfigureAwait(false);
            if (candidate.Session is not null && candidate.Participant is not null)
                result.Add((candidate.Session, candidate.Participant));
        }
        return result;
    }

    private async ValueTask<(SelectedSessionIdentity? Session, TownParticipantEntryState? Participant)>
        TryCreateParticipantAsync(TownAreaMember member, CancellationToken cancellationToken)
    {
        var session = ExactSession(member);
        var runtime = session is null ? null
            : await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (session is null || runtime is null)
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
        TownAreaAdmission Admission,
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
