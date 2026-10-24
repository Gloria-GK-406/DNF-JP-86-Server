using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Party;

namespace Dfo.Server.Protocol.Game.TownProjection;

/// <summary>One selected character asking the area service to enter its current town location.</summary>
public sealed record TownAreaEntry(
    SelectedSessionIdentity Session,
    SelectCharacterResult Runtime,
    Dfo.Server.Data.Characters.CharacterPositionState Position,
    bool ProjectWeaknessBeforeTown = false);

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
/// Every delivery re-reads the destination area's current snapshot, so it only ever draws the
/// newest roster.
/// </remarks>
public sealed class TownAreaProjectionDelivery
{
    private readonly ITownAreaDomainService townAreas;
    private readonly ISelectedSessionDomainService selectedSessions;
    private readonly ISelectedCharacterCurrentViewDomainService currentViews;
    private readonly ITownMovementDomainService towns;
    private readonly Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService? housing;
    private readonly Dfo.Server.Abstractions.Marriage.IMarriageHousingSessionDomainService? housingSessions;
    private readonly IEventService notifications;
    private readonly IPartyTownProjectionDelivery partyTownProjection;
    private readonly IGameMessageSender sender;
    private readonly Dfo.Server.Protocol.Game.Weakness.WeaknessProjectionDelivery? weaknessProjection;

    public TownAreaProjectionDelivery(
        ITownAreaDomainService townAreas,
        ISelectedSessionDomainService selectedSessions,
        ISelectedCharacterCurrentViewDomainService currentViews,
        IEventService notifications,
        IPartyTownProjectionDelivery partyTownProjection,
        IGameMessageSender sender,
        ITownMovementDomainService towns,
        Dfo.Server.Protocol.Game.Weakness.WeaknessProjectionDelivery? weaknessProjection = null,
        Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService? housing = null,
        Dfo.Server.Abstractions.Marriage.IMarriageHousingSessionDomainService? housingSessions = null)
    {
        this.townAreas = townAreas;
        this.selectedSessions = selectedSessions;
        this.currentViews = currentViews;
        this.towns = towns;
        this.notifications = notifications;
        this.partyTownProjection = partyTownProjection;
        this.sender = sender;
        this.weaknessProjection = weaknessProjection;
        this.housing = housing;
        this.housingSessions = housingSessions;
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
        var instance = moved.TownId == 101 && housing is not null ? await housing.InstanceAsync(session, cancellationToken).ConfigureAwait(false) : 0;
        if (moved.TownId == 101 && instance is not > 0) return false;
        var destination = new TownAreaKey(
            channelId,
            moved.TownId,
            moved.AreaId, instance ?? 0);
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
        var changedArea = previousMember.Area != member.Area;
        if (changedArea && previousMember.Area.TownId == 101) housingSessions?.CloseExact(session);
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
        var marriageFrames = await HousingCurrentProjection.ReadAsync(housing, session, cancellationToken).ConfigureAwait(false);
        if (marriageFrames.Count > 0) await sender.SendExplicitAsync(connectionId, new(marriageFrames, []), cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Sets one actor's position on its own client and every current same-area observer.</summary>
    public async ValueTask<bool> ForcePositionAsync(
        SelectedSessionIdentity session,
        Dfo.Server.Data.Characters.CharacterPositionState position,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(position);
        var previous = townAreas.Find(session.CharacterId);
        if (!selectedSessions.IsInWorld(session) || previous is null || !session.Owns(previous)
            || previous.Area.TownId != position.TownId || previous.Area.AreaId != position.AreaId)
            return false;
        if (!selectedSessions.IsInWorld(session))
            return false;
        var movement = townAreas.MoveExact(session.CharacterId, session.TownAreaLease(), position);
        if (movement is null) return false;
        var own = movement.Area.Members.Single(member => session.Owns(member));
        var message = new ForcedTownPositionNotification(session.WireUserId,
            own.Position.X, own.Position.Y, own.Position.Direction);
        var targets = own.SharesTownPresence
            ? movement.Area.Members.Where(member => member.SharesTownPresence && member.CharacterId != session.CharacterId)
                .Select(ExactSession).Where(candidate => candidate is not null)
                .Select(candidate => new GameExplicitMessageTarget(candidate!.ConnectionId, [message])).ToArray()
            : [];
        await sender.SendExplicitAsync(session.ConnectionId, new([message], targets), cancellationToken).ConfigureAwait(false);
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
        var candidates = new List<(TownAreaEntry Entry, bool SharesTownPresence, int Instance)>();
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (!selectedSessions.IsInWorld(entry.Session))
                continue;
            var sharesTownPresence = await towns.SharesTownPresenceAsync(
                entry.Position.TownId,
                entry.Position.AreaId,
                cancellationToken).ConfigureAwait(false);
            var instance = entry.Position.TownId == 101 && housing is not null ? await housing.InstanceAsync(entry.Session, cancellationToken).ConfigureAwait(false) : 0;
            if (entry.Position.TownId == 101 && instance is not > 0) continue;
            candidates.Add((entry, entry.Position.TownId == 101 || sharesTownPresence, instance ?? 0));
        }
        if (candidates.Count == 0)
            return 0;

        var admissions = new List<TownAreaAdmission>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var entry = candidate.Entry;
            if (!selectedSessions.IsInWorld(entry.Session))
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
                    entry.Position.AreaId, candidate.Instance),
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
            // FAIL_CLEAR_DUNGEON captures the client's current dungeon for its result window.
            // A give-up must send it before the town frames discard that context, exactly once.
            if (weaknessProjection is not null && delivery.Admission.Entry.ProjectWeaknessBeforeTown)
                await weaknessProjection.SendCurrentAsync(delivery.Admission.Entry.Session, cancellationToken).ConfigureAwait(false);
            await sender.SendExplicitAsync(
                delivery.Admission.Entry.Session.ConnectionId,
                new GameExplicitMessageDelivery(delivery.Frames, []),
                cancellationToken).ConfigureAwait(false);
            if (weaknessProjection is not null && !delivery.Admission.Entry.ProjectWeaknessBeforeTown)
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
        if (!selectedSessions.IsInWorld(session))
            return;
        await sender.SendExplicitAsync(
            session.ConnectionId,
            new GameExplicitMessageDelivery(
                TownPresenceMessages.ForNewcomer(publication.State), []),
            cancellationToken).ConfigureAwait(false);
        if (!selectedSessions.IsInWorld(session))
            return;
        await partyTownProjection.RefreshObservedPartiesAsync(
            [session], publication.Audience, cancellationToken).ConfigureAwait(false);
        if (!selectedSessions.IsInWorld(session))
            return;
        var registered = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            notifications,
            publication.Audience,
            new TownParticipantPublished(publication.State),
            cancellationToken).ConfigureAwait(false);
        if (!registered.Registered.IsEmpty)
        {
            await partyTownProjection.RefreshObservedPartiesAsync(
                registered.Registered, [session], cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask PublishRosterAsync(
        TownAreaSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
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
        if (!publication.Registered.IsEmpty)
        {
            await partyTownProjection.RefreshObservedPartiesAsync(
                publication.Registered, [newcomer], cancellationToken).ConfigureAwait(false);
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
        return selectedSessions.TryGetInWorld(member.CharacterId, out var session) && session.Owns(member) ? session : null;
    }

    private static PartyDirectoryScope Scope(TownAreaKey key) =>
        new(key.ChannelId, key.TownId, key.AreaId, key.Instance);

    private sealed record ReturnDelivery(
        TownAreaAdmission Admission,
        TownAreaSnapshot Area,
        IReadOnlyList<(SelectedSessionIdentity Session, TownParticipantEntryState Participant)> Peers,
        IReadOnlyList<IGameServerMessage> Frames);
}
