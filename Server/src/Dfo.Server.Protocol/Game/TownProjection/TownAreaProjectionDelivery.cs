using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
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
    private readonly Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService housing;
    private readonly Dfo.Server.Abstractions.Marriage.IMarriageHousingSessionDomainService housingSessions;
    private readonly IEventService notifications;
    private readonly IPartyTownProjectionDelivery partyTownProjection;
    private readonly IGameCommandOutbox outbox;
    private readonly IPartyService parties;
    private readonly IPartyProjectionStateDomainService projectionState;
    private readonly IWeaknessDomainService weaknessState;
    private const bool projectWeakness = true;

    public TownAreaProjectionDelivery(
        ITownAreaDomainService townAreas,
        ISelectedSessionDomainService selectedSessions,
        ISelectedCharacterCurrentViewDomainService currentViews,
        IEventService notifications,
        IPartyTownProjectionDelivery partyTownProjection,
        IGameCommandOutbox outbox, IPartyService parties, IPartyProjectionStateDomainService projectionState, IWeaknessDomainService weaknessState,
        ITownMovementDomainService towns,
        Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService housing,
        Dfo.Server.Abstractions.Marriage.IMarriageHousingSessionDomainService housingSessions)
    {
        this.townAreas = townAreas;
        this.selectedSessions = selectedSessions;
        this.currentViews = currentViews;
        this.towns = towns;
        this.notifications = notifications;
        this.partyTownProjection = partyTownProjection;
        this.outbox = outbox; this.parties = parties; this.projectionState = projectionState; this.weaknessState = weaknessState;
        this.housing = housing;
        this.housingSessions = housingSessions;
    }

    public ValueTask RefuseTownChangeAsync(NetworkConnectionId connectionId, SelectedSessionIdentity session,
        SelectCharacterResult result, Dfo.Server.Data.Characters.CharacterPositionState standing,
        string reason, CancellationToken token) => TownMovementProjection.TownRefuseTownChangeAsync(outbox, connectionId, session, result, standing, reason, token);
    public ValueTask<bool> TransferAsync(NetworkConnectionId connectionId, SelectedSessionIdentity session,
        SelectCharacterResult result, int channelId, Dfo.Server.Data.Characters.CharacterPositionState moved,
        TownAreaMember previous, bool sharesTownPresence, CancellationToken token) =>
        TownMovementProjection.TownTransferAsync(selectedSessions, townAreas, new LegacyCommandProjections(notifications), partyTownProjection, currentViews, housing, outbox, housingSessions, connectionId, session, result, channelId, moved, previous, sharesTownPresence, token);
    public ValueTask<bool> ProjectTransferAsync(NetworkConnectionId connectionId, SelectedSessionIdentity session,
        SelectCharacterResult result, TownAreaMember previous, CancellationToken token) =>
        TownMovementProjection.TownProjectTransferAsync(selectedSessions, townAreas, new LegacyCommandProjections(notifications), partyTownProjection, currentViews, housing, outbox, housingSessions, connectionId, session, result, previous, token);

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
        await outbox.RegisterExplicitAsync(session.ConnectionId, new([message], targets), cancellationToken).ConfigureAwait(false);
        return true;
    }

    public ValueTask<bool> MoveAsync(NetworkConnectionId connectionId, SelectedSessionIdentity session,
        Dfo.Server.Data.Characters.CharacterPositionState position, ushort trailer, CancellationToken token) =>
        TownMovementProjection.TownMoveAsync(selectedSessions, townAreas, new LegacyCommandProjections(notifications), outbox, connectionId, session, position, trailer, token);

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
    public ValueTask<IReadOnlyList<TownAreaMembershipChange>> LeaveManyAsync(
        IReadOnlyList<SelectedSessionIdentity> sessions, TownAreaDepartureReason reason, CancellationToken token) =>
        TownDepartureProjection.TownLeaveManyAsync(selectedSessions, townAreas, new LegacyCommandProjections(notifications), projectionState, currentViews, parties, sessions, reason, token);

    /// <summary>
    /// Admits one or more characters and projects the resulting area revision. Callers describe
    /// only where the characters are; the destination area's content policy and every visibility
    /// notification remain owned here.
    /// </summary>
    public ValueTask<int> EnterAsync(IReadOnlyList<TownAreaEntry> entries, CancellationToken cancellationToken) =>
        TownArrivalProjection.EnterAsync(selectedSessions, towns, housing, townAreas, currentViews, outbox,
            weaknessState, projectWeakness, new LegacyCommandProjections(notifications),
            partyTownProjection, entries, cancellationToken);

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
        await outbox.RegisterExplicitAsync(
            session.ConnectionId,
            new GameExplicitMessageDelivery(
                TownPresenceMessages.ForNewcomer(publication.State), []),
            cancellationToken).ConfigureAwait(false);
        if (!selectedSessions.IsInWorld(session))
            return;
        await partyTownProjection.RefreshObservedPartiesAsync(
            [session], cancellationToken).ConfigureAwait(false);
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
                registered.Registered, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask PublishRosterAsync(
        TownAreaSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await PublishCurrentRosterCoreAsync(snapshot.Key, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask PublishArrivalCoreAsync(SelectedSessionIdentity newcomer, TownPresenceSynchronizationState state,
        IReadOnlyList<SelectedSessionIdentity> peerSessions, CancellationToken token) =>
        TownArrivalProjection.TownPublishArrivalCoreAsync(townAreas, new LegacyCommandProjections(notifications), partyTownProjection, newcomer, state, peerSessions, token);
    private ValueTask PublishCurrentRosterCoreAsync(TownAreaKey key, CancellationToken token) =>
        TownArrivalProjection.TownPublishCurrentRosterCoreAsync(selectedSessions, townAreas, new LegacyCommandProjections(notifications), currentViews, key, token);
    private ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, TownParticipantEntryState Participant)>>
        VisibleParticipantsAsync(TownAreaSnapshot area, SelectedSessionIdentity viewer, CancellationToken token) =>
        TownArrivalProjection.TownVisibleParticipantsAsync(selectedSessions, currentViews, area, viewer, token);
    private SelectedSessionIdentity? ExactSession(TownAreaMember member) =>
        selectedSessions.TryGetInWorld(member.CharacterId, out var session) && session.Owns(member) ? session : null;
    private static PartyDirectoryScope Scope(TownAreaKey key) => new(key.ChannelId, key.TownId, key.AreaId, key.Instance);
}
