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
    private readonly IOnlineCharacterRuntimeDomainService onlineCharacters;
    private readonly ISelectedSessionDomainService selectedSessions;
    private readonly ISelectedCharacterCurrentViewDomainService currentViews;
    private readonly ITownMovementDomainService towns;
    private readonly Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService housing;
    private readonly Dfo.Server.Abstractions.Marriage.IMarriageHousingSessionDomainService housingSessions;
    private readonly ICommandProjections notifications;
    private readonly IPartyTownProjectionDelivery partyTownProjection;
    private readonly IGameCommandOutbox outbox;
    private readonly IWeaknessDomainService weaknessState;
    private const bool projectWeakness = true;

    public TownAreaProjectionDelivery(
        ITownAreaDomainService townAreas,
        IOnlineCharacterRuntimeDomainService onlineCharacters,
        ISelectedSessionDomainService selectedSessions,
        ISelectedCharacterCurrentViewDomainService currentViews,
        ICommandProjections notifications,
        IPartyTownProjectionDelivery partyTownProjection,
        IGameCommandOutbox outbox, IWeaknessDomainService weaknessState,
        ITownMovementDomainService towns,
        Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService housing,
        Dfo.Server.Abstractions.Marriage.IMarriageHousingSessionDomainService housingSessions)
    {
        this.townAreas = townAreas;
        this.onlineCharacters = onlineCharacters;
        this.selectedSessions = selectedSessions;
        this.currentViews = currentViews;
        this.towns = towns;
        this.notifications = notifications;
        this.partyTownProjection = partyTownProjection;
        this.outbox = outbox; this.weaknessState = weaknessState;
        this.housing = housing;
        this.housingSessions = housingSessions;
    }

    public ValueTask RefuseTownChangeAsync(NetworkConnectionId connectionId, SelectedSessionIdentity session,
        SelectCharacterResult result, Dfo.Server.Data.Characters.CharacterPositionState standing,
        string reason, CancellationToken token) => TownMovementProjection.TownRefuseTownChangeAsync(outbox, connectionId, session, result, standing, reason, token);
    public ValueTask<bool> ProjectTransferAsync(NetworkConnectionId connectionId, SelectedSessionIdentity session,
        SelectCharacterResult result, TownAreaMember previous, CancellationToken token) =>
        TownMovementProjection.TownProjectTransferAsync(selectedSessions, townAreas, notifications, partyTownProjection, currentViews, housing, outbox, housingSessions, connectionId, session, result, previous, token);

    /// <summary>
    /// Admits one or more characters and projects the resulting area revision. Callers describe
    /// only where the characters are; the destination area's content policy and every visibility
    /// notification remain owned here.
    /// </summary>
    public ValueTask<int> EnterAsync(IReadOnlyList<TownAreaEntry> entries, CancellationToken cancellationToken) =>
        TownArrivalProjection.EnterAsync(selectedSessions, towns, housing, townAreas, onlineCharacters, currentViews, outbox,
            weaknessState, projectWeakness, notifications,
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

    private ValueTask PublishCurrentRosterCoreAsync(TownAreaKey key, CancellationToken token) =>
        TownArrivalProjection.TownPublishCurrentRosterCoreAsync(selectedSessions, townAreas, notifications, currentViews, key, token);
}
