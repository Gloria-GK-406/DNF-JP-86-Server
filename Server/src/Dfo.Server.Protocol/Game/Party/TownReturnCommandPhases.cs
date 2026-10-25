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

namespace Dfo.Server.Protocol.Game.Party;

using Dfo.Server.Protocol.Game.TownProjection;
internal abstract partial class PartyMutationProtocolCommandHandler
{
    protected async ValueTask<int> TownEnterAsync(IReadOnlyList<TownAreaEntry> entries, CancellationToken token)
    {
        var candidates = await TownPrepareCandidatesAsync(entries, token).ConfigureAwait(false);
        var admissions = TownCommitAdmissions(candidates);
        var deliveries = await TownCaptureDeliveriesAsync(admissions, token).ConfigureAwait(false);
        await TownProjectOwnersAsync(deliveries, token).ConfigureAwait(false);
        await TownProjectPreviousAreasAsync(admissions, token).ConfigureAwait(false);
        await TownProjectPeersAsync(admissions, deliveries, token).ConfigureAwait(false);
        return deliveries.Count;
    }

    protected async ValueTask<List<(TownAreaEntry Entry, bool SharesTownPresence, int Instance)>> TownPrepareCandidatesAsync(
        IReadOnlyList<TownAreaEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var candidates = new List<(TownAreaEntry Entry, bool SharesTownPresence, int Instance)>();
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (!sessions.IsInWorld(entry.Session))
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
            return [];

        return candidates;
    }

    protected List<TownAreaAdmission> TownCommitAdmissions(List<(TownAreaEntry Entry, bool SharesTownPresence, int Instance)> candidates)
    {
        var admissions = new List<TownAreaAdmission>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var entry = candidate.Entry;
            if (!sessions.IsInWorld(entry.Session))
                continue;
            var previous = townAreas.Find(entry.Session.CharacterId);
            var previousScope = previous is { SharesTownPresence: true }
                ? TownScope(previous.Area)
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

        return admissions;
    }

    protected async ValueTask<List<TownReturnDelivery>> TownCaptureDeliveriesAsync(List<TownAreaAdmission> admissions, CancellationToken cancellationToken)
    {
        var deliveries = new List<TownReturnDelivery>();
        foreach (var admission in admissions)
        {
            var current = townAreas.Find(admission.Change.CurrentArea!.Key);
            var own = current?.Members.SingleOrDefault(
                member => admission.Entry.Session.Owns(member));
            if (own is null)
                continue;
            var peers = await TownVisibleParticipantsAsync(
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

    protected async ValueTask TownProjectOwnersAsync(List<TownReturnDelivery> deliveries, CancellationToken cancellationToken)
    {
        foreach (var delivery in deliveries)
        {
            // FAIL_CLEAR_DUNGEON captures the client's current dungeon for its result window.
            // A give-up must send it before the town frames discard that context, exactly once.
            if (weakness is not null && delivery.Admission.Entry.ProjectWeaknessBeforeTown)
                await weakness!(delivery.Admission.Entry.Session, cancellationToken).ConfigureAwait(false);
            await register(
                delivery.Admission.Entry.Session.ConnectionId,
                new GameExplicitMessageDelivery(delivery.Frames, []),
                cancellationToken).ConfigureAwait(false);
            if (weakness is not null && !delivery.Admission.Entry.ProjectWeaknessBeforeTown)
                await weakness!(delivery.Admission.Entry.Session, cancellationToken).ConfigureAwait(false);
        }

    }

    protected async ValueTask TownProjectPreviousAreasAsync(List<TownAreaAdmission> admissions, CancellationToken cancellationToken)
    {
        foreach (var previousArea in admissions
            .Where(admission => admission.Change.PreviousArea is { } previous
                && previous.Key != admission.Change.CurrentArea?.Key)
            .Select(admission => admission.Change.PreviousArea!.Key)
            .Distinct())
        {
            await TownPublishCurrentRosterCoreAsync(previousArea, cancellationToken)
                .ConfigureAwait(false);
        }

    }

    protected async ValueTask TownProjectPeersAsync(List<TownAreaAdmission> admissions, List<TownReturnDelivery> deliveries, CancellationToken cancellationToken)
    {
        var returningCharacters = admissions
            .Select(value => value.Entry.Session.CharacterId)
            .ToHashSet();
        foreach (var delivery in deliveries)
        {
            var preExistingPeers = delivery.Peers
                .Where(peer => !returningCharacters.Contains(peer.Session.CharacterId))
                .ToArray();
            await TownPublishTownReturnCoreAsync(
                delivery.Admission,
                delivery.Area,
                preExistingPeers,
                cancellationToken).ConfigureAwait(false);
        }
    }

    protected async ValueTask TownPublishTownReturnCoreAsync(
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
            await TownPublishArrivalCoreAsync(
                admission.Entry.Session, state, peerSessions, cancellationToken).ConfigureAwait(false);
        }
        if (peerSessions.Length > 0)
        {
            await PartyTownRefreshObservedPartiesAsync(
                [admission.Entry.Session], peerSessions, cancellationToken).ConfigureAwait(false);
        }
        if (admission.Change.Kind != TownAreaMembershipChangeKind.Unchanged)
        {
            await PartyTownRefreshMovedPartyAsync(
                admission.Entry.Session, admission.PreviousScope, cancellationToken).ConfigureAwait(false);
        }
    }

    protected async ValueTask TownPublishArrivalCoreAsync(
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
            await PartyTownRefreshObservedPartiesAsync(
                publication.Registered, [newcomer], cancellationToken).ConfigureAwait(false);
        }
    }

    protected async ValueTask TownPublishCurrentRosterCoreAsync(
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
            var recipient = TownExactSession(member);
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
                var exact = TownExactSession(occupant);
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

    protected async ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, TownParticipantEntryState Participant)>>
        TownVisibleParticipantsAsync(TownAreaSnapshot area, SelectedSessionIdentity viewer,
            CancellationToken cancellationToken)
    {
        var result = new List<(SelectedSessionIdentity, TownParticipantEntryState)>();
        foreach (var member in area.VisibleTo(viewer.CharacterId)
                     .Where(member => member.CharacterId != viewer.CharacterId))
        {
            var candidate = await TownTryCreateParticipantAsync(member, cancellationToken)
                .ConfigureAwait(false);
            if (candidate.Session is not null && candidate.Participant is not null)
                result.Add((candidate.Session, candidate.Participant));
        }
        return result;
    }

    protected async ValueTask<(SelectedSessionIdentity? Session, TownParticipantEntryState? Participant)>
        TownTryCreateParticipantAsync(TownAreaMember member, CancellationToken cancellationToken)
    {
        var session = TownExactSession(member);
        var runtime = session is null ? null
            : await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (session is null || runtime is null)
        {
            return (null, null);
        }
        return (session, TownReturnProjection.Participant(runtime, member.Position));
    }

    protected SelectedSessionIdentity? TownExactSession(TownAreaMember member)
    {
        return sessions.TryGetInWorld(member.CharacterId, out var session) && session.Owns(member) ? session : null;
    }

    protected static PartyDirectoryScope TownScope(TownAreaKey key) =>
        new(key.ChannelId, key.TownId, key.AreaId, key.Instance);

    protected sealed record TownReturnDelivery(
        TownAreaAdmission Admission,
        TownAreaSnapshot Area,
        IReadOnlyList<(SelectedSessionIdentity Session, TownParticipantEntryState Participant)> Peers,
        IReadOnlyList<IGameServerMessage> Frames);

    protected virtual ValueTask<int> ReturnTownAsync(IReadOnlyList<TownAreaEntry> entries, CancellationToken token) => TownEnterAsync(entries, token);

    protected async ValueTask ReturnTownIsolatedAsync(IReadOnlyList<TownAreaEntry> entries,
        List<(SelectedSessionIdentity, Exception)> failures, CancellationToken token)
    {
        if (entries.Count == 0) return;
        try { _ = await ReturnTownAsync(entries, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            foreach (var entry in entries) failures.Add((entry.Session, error));
        }
    }

}
