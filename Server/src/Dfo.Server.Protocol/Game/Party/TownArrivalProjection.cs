using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Dfo.Server.Protocol.Game.Party;

internal static class TownArrivalProjection
{
    /// <summary>The return requests for these entries, one per entry and in its order.</summary>
    internal static TownReturnRequest[] TownReturnRequests(IReadOnlyList<TownAreaEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries.Select(entry =>
        {
            ArgumentNullException.ThrowIfNull(entry);
            return new TownReturnRequest(entry.Session, entry.Runtime.World.Channel.ChannelId, entry.Position);
        }).ToArray();
    }

    /// <summary>Pairs committed admissions back with the entries their requests were made from.</summary>
    internal static List<TownAreaAdmission> TownAdmissions(IReadOnlyList<TownAreaEntry> entries,
        IReadOnlyList<TownReturnRequest> requests, IReadOnlyList<TownReturnAdmission> admitted)
    {
        var entryOf = new Dictionary<TownReturnRequest, TownAreaEntry>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < requests.Count; index++) entryOf.Add(requests[index], entries[index]);
        return admitted.Select(admission => new TownAreaAdmission(entryOf[admission.Request], admission.Change,
            admission.Previous is { SharesTownPresence: true } previous ? TownScope(previous.Area) : null)).ToList();
    }

    /// <summary>Registers owner, previous-area and peer projections of committed admissions in their fixed order.</summary>
    internal static async ValueTask<int> ProjectAsync(
        ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas,
        ISelectedCharacterCurrentViewDomainService currentViews, IGameCommandOutbox outbox,
        IWeaknessDomainService weaknessState, bool projectWeakness, ICommandProjections projections,
        IPartyTownProjectionDelivery partyTown,
        List<TownAreaAdmission> admissions, CancellationToken token)
    {
        var deliveries = await TownCaptureDeliveriesAsync(sessions, townAreas, currentViews, admissions, token).ConfigureAwait(false);
        await TownProjectOwnersAsync(sessions, outbox, weaknessState, projectWeakness, deliveries, token).ConfigureAwait(false);
        await TownProjectPreviousAreasAsync(sessions, townAreas, projections, currentViews, admissions, token).ConfigureAwait(false);
        await TownProjectPeersAsync(sessions, townAreas, projections, partyTown, admissions, deliveries, token).ConfigureAwait(false);
        return deliveries.Count;
    }

    internal static async ValueTask<List<TownReturnDelivery>> TownCaptureDeliveriesAsync(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas, ISelectedCharacterCurrentViewDomainService currentViews, List<TownAreaAdmission> admissions, CancellationToken cancellationToken)
    {
        var deliveries = new List<TownReturnDelivery>();
        foreach (var admission in admissions)
        {
            var current = townAreas.Find(admission.Change.CurrentArea!.Key);
            var own = current?.Members.SingleOrDefault(
                member => member.Session == admission.Entry.Session);
            if (own is null)
                continue;
            var peers = await TownArrivalProjection.TownVisibleParticipantsAsync(sessions, currentViews, 
                current!, admission.Entry.Session, cancellationToken).ConfigureAwait(false);
            deliveries.Add(new TownReturnDelivery(
                admission,
                current!,
                peers,
                TownReturnProjection.Frames(
                    admission.Entry.Session,
                    admission.Entry.Runtime,
                    own.Position,
                    peers.Select(candidate => candidate.Participant).ToArray())));
        }

        return deliveries;
    }

    internal static async ValueTask TownProjectOwnersAsync(ISelectedSessionDomainService sessions, IGameCommandOutbox outbox, IWeaknessDomainService weaknessState, bool projectWeakness, List<TownReturnDelivery> deliveries, CancellationToken cancellationToken)
    {
        foreach (var delivery in deliveries)
        {
            // FAIL_CLEAR_DUNGEON captures the client's current dungeon for its result window.
            // A give-up must send it before the town frames discard that context, exactly once.
            if (projectWeakness && delivery.Admission.Entry.ProjectWeaknessBeforeTown)
                await TownWeaknessProjection.RegisterAsync(sessions, weaknessState, outbox, delivery.Admission.Entry.Session, cancellationToken).ConfigureAwait(false);
            await outbox.RegisterExplicitAsync(
                delivery.Admission.Entry.Session.ConnectionId,
                new GameExplicitMessageDelivery(delivery.Frames, []),
                cancellationToken).ConfigureAwait(false);
            if (projectWeakness && !delivery.Admission.Entry.ProjectWeaknessBeforeTown)
                await TownWeaknessProjection.RegisterAsync(sessions, weaknessState, outbox, delivery.Admission.Entry.Session, cancellationToken).ConfigureAwait(false);
        }

    }

    internal static async ValueTask TownProjectPreviousAreasAsync(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas, ICommandProjections projections, ISelectedCharacterCurrentViewDomainService currentViews, List<TownAreaAdmission> admissions, CancellationToken cancellationToken)
    {
        foreach (var previousArea in admissions
            .Where(admission => admission.Change.PreviousArea is { } previous
                && previous.Key != admission.Change.CurrentArea?.Key)
            .Select(admission => admission.Change.PreviousArea!.Key)
            .Distinct())
        {
            await TownArrivalProjection.TownPublishCurrentRosterCoreAsync(sessions, townAreas, projections, currentViews, previousArea, cancellationToken)
                .ConfigureAwait(false);
        }

    }

    internal static async ValueTask TownProjectPeersAsync(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas, ICommandProjections projections, IPartyTownProjectionDelivery partyTown, List<TownAreaAdmission> admissions, List<TownReturnDelivery> deliveries, CancellationToken cancellationToken)
    {
        var returningCharacters = admissions
            .Select(value => value.Entry.Session.CharacterId)
            .ToHashSet();
        foreach (var delivery in deliveries)
        {
            var preExistingPeers = delivery.Peers
                .Where(peer => !returningCharacters.Contains(peer.Session.CharacterId))
                .ToArray();
            await TownArrivalProjection.TownPublishTownReturnCoreAsync(sessions, townAreas, projections, partyTown, 
                delivery.Admission,
                delivery.Area,
                preExistingPeers,
                cancellationToken).ConfigureAwait(false);
        }
    }

    internal static async ValueTask TownPublishTownReturnCoreAsync(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas, ICommandProjections projections, IPartyTownProjectionDelivery partyTown, 
        TownAreaAdmission admission,
        TownAreaSnapshot current,
        IReadOnlyList<(SelectedSessionIdentity Session, TownParticipantEntryState Participant)> peers,
        CancellationToken cancellationToken)
    {
        var peerSessions = peers.Select(candidate => candidate.Session).ToArray();
        var own = current.Members.Single(member => member.Session == admission.Entry.Session);
        if (admission.Change.Kind != TownAreaMembershipChangeKind.Unchanged
            && own.SharesTownPresence
            && peerSessions.Length > 0)
        {
            var state = new TownPresenceSynchronizationState(
                own.Position.TownId,
                own.Position.AreaId,
                TownReturnProjection.Participant(admission.Entry.Runtime, own.Position),
                peers.Select(candidate => candidate.Participant).ToArray());
            await TownArrivalProjection.TownPublishArrivalCoreAsync(townAreas, projections, partyTown, 
                admission.Entry.Session, state, peerSessions, cancellationToken).ConfigureAwait(false);
        }
        if (peerSessions.Length > 0)
        {
            await partyTown.RefreshObservedPartiesAsync( 
                [admission.Entry.Session], cancellationToken).ConfigureAwait(false);
        }
        if (admission.Change.Kind != TownAreaMembershipChangeKind.Unchanged)
        {
            await partyTown.RefreshMovedPartyAsync( 
                admission.Entry.Session, admission.PreviousScope, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static async ValueTask TownPublishArrivalCoreAsync(ITownAreaDomainService townAreas, ICommandProjections projections, IPartyTownProjectionDelivery partyTown, 
        SelectedSessionIdentity newcomer,
        TownPresenceSynchronizationState state,
        IReadOnlyList<SelectedSessionIdentity> peerSessions,
        CancellationToken cancellationToken)
    {
        var publication = await PartyExactNotificationDelivery.PublishWithRetryAsync(
            projections,
            peerSessions,
            new TownParticipantPublished(state),
            cancellationToken).ConfigureAwait(false);
        if (!publication.Registered.IsEmpty)
        {
            await partyTown.RefreshObservedPartiesAsync( 
                publication.Registered, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static async ValueTask TownPublishCurrentRosterCoreAsync(ISelectedSessionDomainService sessions, ITownAreaDomainService townAreas, ICommandProjections projections, ISelectedCharacterCurrentViewDomainService currentViews, 
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
            var recipient = TownArrivalProjection.TownExactSession(sessions, member);
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
                var exact = TownArrivalProjection.TownExactSession(sessions, occupant);
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

            await projections.ProjectAsync(
                new NotificationAudience.TheseSessions([recipient]),
                new TownAreaRosterReconciled(key.TownId, key.AreaId, users),
                cancellationToken).ConfigureAwait(false);
        }
    }

    internal static async ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, TownParticipantEntryState Participant)>>
        TownVisibleParticipantsAsync(ISelectedSessionDomainService sessions, ISelectedCharacterCurrentViewDomainService currentViews, TownAreaSnapshot area, SelectedSessionIdentity viewer,
            CancellationToken cancellationToken)
    {
        var result = new List<(SelectedSessionIdentity, TownParticipantEntryState)>();
        foreach (var member in area.VisibleTo(viewer.CharacterId)
                     .Where(member => member.CharacterId != viewer.CharacterId))
        {
            var candidate = await TownArrivalProjection.TownTryCreateParticipantAsync(sessions, currentViews, member, cancellationToken)
                .ConfigureAwait(false);
            if (candidate.Session is not null && candidate.Participant is not null)
                result.Add((candidate.Session, candidate.Participant));
        }
        return result;
    }

    internal static async ValueTask<(SelectedSessionIdentity? Session, TownParticipantEntryState? Participant)>
        TownTryCreateParticipantAsync(ISelectedSessionDomainService sessions, ISelectedCharacterCurrentViewDomainService currentViews, TownAreaMember member, CancellationToken cancellationToken)
    {
        var session = TownArrivalProjection.TownExactSession(sessions, member);
        var runtime = session is null ? null
            : await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (session is null || runtime is null)
        {
            return (null, null);
        }
        return (session, TownReturnProjection.Participant(runtime, member.Position));
    }

    internal static SelectedSessionIdentity? TownExactSession(ISelectedSessionDomainService sessions, TownAreaMember member)
    {
        return sessions.TryGetInWorld(member.CharacterId, out var session) && member.Session == session ? session : null;
    }

    internal static PartyDirectoryScope TownScope(TownAreaKey key) =>
        new(key.ChannelId, key.TownId, key.AreaId, key.Instance);
}
