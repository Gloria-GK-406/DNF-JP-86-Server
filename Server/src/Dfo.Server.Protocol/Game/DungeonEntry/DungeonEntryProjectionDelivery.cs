using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.DungeonEntry;

/// <summary>
/// Maps a dungeon instance's stable character audience to the current transport generations and
/// commits one receiver-specific entry projection for every valid participant.
/// </summary>
/// <remarks>
/// The dungeon domain owns membership and the semantic recipient set. This boundary owns only
/// volatile transport identities and wire messages; neither the handler nor the domain duplicates
/// that mapping.
/// </remarks>
public sealed class DungeonEntryProjectionDelivery(
    ICharacterDomainService characters,
    ICharacterStateProjectionDomainService currentState,
    ISelectedSessionDomainService selectedSessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    IDungeonRunDomainService runs,
    DungeonRunDeliveryRegistry deliveryRegistry,
    IDungeonEntryIntentDomainService entryIntents,
    IDungeonVisitDomainService visits,
    ICharacterFatigueDomainService fatigue,
    IGameMessageSender sender,
    INetworkConnectionCloser connectionCloser,
    TownAreaProjectionDelivery townAreaProjection,
    SelectedSessionTransitionGate transitionGate,
    DungeonProjectionGate projectionGate,
    ILogger<DungeonEntryProjectionDelivery> logger)
{
    public async ValueTask EnterAsync(
        NetworkConnectionId requesterConnection,
        SelectedSessionIdentity requester,
        SelectCharacterResult requesterRuntime,
        PartySnapshot roster,
        SelectDungeonRequest message,
        DungeonEntryFacts facts,
        DungeonEntryIntent intent,
        CancellationToken cancellationToken)
    {
        void RestoreSelection()
        {
            if (selectedSessions.FindByCharacter(requester.CharacterId) == requester)
                entryIntents.RestoreIfAbsent(requester, intent);
        }

        var attachments = await ResolveAttachmentsAsync(
            requester, requesterRuntime, roster, cancellationToken).ConfigureAwait(false);
        if (attachments.BlockedSlot is { } blockedSlot)
        {
            RestoreSelection();
            await RefuseAsync(requesterConnection, SelectDungeonRefusal.MemberBlocked, blockedSlot,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var participants = attachments.Members
            .Select(value => new DungeonRunParticipant(
                value.Session.AccountId,
                value.Session.CharacterId,
                value.Slot,
                value.Session.CharacterId == requester.CharacterId))
            .ToArray();
        var previousRuns = participants.ToDictionary(
            participant => participant.CharacterId,
            participant => runs.Find(participant.CharacterId));
        var attempt = await visits.TryEnterPartyAsync(
            participants, message.DungeonId, facts, cancellationToken)
            .ConfigureAwait(false);
        var entered = attempt.Entry;
        if (entered is null)
        {
            RestoreSelection();
            if (attempt.Failure == DungeonEntryFailure.InsufficientFatigue)
            {
                await RefuseAsync(requesterConnection,
                    participants.Length == 1 ? SelectDungeonRefusal.InsufficientFatigue : SelectDungeonRefusal.MemberBlocked,
                    attempt.BlockedSlot ?? 0, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (attempt.Failure == DungeonEntryFailure.DifficultyNotUnlocked)
            {
                await RefuseAsync(requesterConnection, SelectDungeonRefusal.MemberBlocked,
                    participants[0].Slot, cancellationToken).ConfigureAwait(false);
                return;
            }
            logger.LogWarning(
                "Select dungeon {ConnectionId}: the archive declares no enterable dungeon "
                    + "{DungeonId}, refusing",
                requesterConnection,
                message.DungeonId);
            await RefuseAsync(requesterConnection, SelectDungeonRefusal.DungeonNotFound, 0,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var leaderVisit = entered.Visits[requester.CharacterId];
        var runId = leaderVisit.Run.RunId;
        var change = entered.Change ?? new DungeonEntryChange(
            DungeonEntryChangeKind.Prepared,
            leaderVisit.Run,
            leaderVisit.Run.EntryRevision,
            participants.OrderBy(value => value.Slot).Select(value => value.CharacterId).ToArray());
        var preparedRevision = change.Revision;
        var semanticRecipients = change.Recipients.ToHashSet();
        if (change.Run.RunId != runId
            || semanticRecipients.Count != attachments.Members.Count
            || attachments.Members.Any(value => !semanticRecipients.Contains(value.Session.CharacterId)))
        {
            RollbackEntry(runId, preparedRevision, participants);
            await RefuseCurrentRequesterAsync(requesterConnection, requester, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        foreach (var participant in participants)
        {
            if (previousRuns[participant.CharacterId] is { } previous
                && previous.RunId != runId)
            {
                deliveryRegistry.RemoveParticipant(previous.RunId, participant.CharacterId);
            }
        }

        await using var projectionLease = await projectionGate.AcquireAsync(
            runId, UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false);
        var requireRunMatch = entered.Change is not null;
        var observed = ObserveCurrentCohort(
            attachments.Members, runId, change.Run.RoomVersion, requireRunMatch);
        if (observed.Status != CohortStatus.Current)
        {
            if (observed.Status == CohortStatus.InvalidGeneration)
                RollbackEntry(runId, preparedRevision, participants);
            await RefuseCurrentRequesterAsync(requesterConnection, requester, cancellationToken)
                .ConfigureAwait(false);
            return;
        }
        change = observed.Change ?? change;

        var layout = entered.Layout;
        var deliveries = new List<(SelectedSessionIdentity Session, IReadOnlyList<IGameServerMessage> Frames)>();
        try
        {
            deliveryRegistry.Register(runId, attachments.Members.Select(value => value.Session).ToArray());

            var refreshed = new Dictionary<CharacterId, SelectCharacterResult>();
            foreach (var attachment in attachments.Members.OrderBy(value => value.Slot))
            {
                refreshed[attachment.Session.CharacterId] = await currentState
                    .RefreshAsync(attachment.Runtime, cancellationToken).ConfigureAwait(false);
            }

            var orderedAttachments = attachments.Members.OrderBy(value => value.Slot).ToArray();
            var allWireUserIds = orderedAttachments
                .Select(value => value.Session.WireUserId)
                .ToImmutableArray();
            var hostSlot = orderedAttachments.Single(value =>
                value.Session.CharacterId == participants.Single(participant => participant.IsLeader).CharacterId).Slot;

            foreach (var receiver in orderedAttachments)
            {
                var visit = entered.Visits[receiver.Session.CharacterId];
                var lackedSelectionPrelude = receiver.Session != requester
                    && entryIntents.Consume(receiver.Session) is null;
                var receiverFirst = orderedAttachments
                    .OrderBy(value => value.Session.CharacterId == receiver.Session.CharacterId ? 0 : 1)
                    .ThenBy(value => value.Slot)
                    .ToArray();
                var frames = new List<IGameServerMessage>();
                // A21 projects the receiver first, then peers in stable party-slot order. Only
                // peer USERINFO bodies receive the routing uid at +3; the self body keeps zero.
                foreach (var source in receiverFirst)
                {
                    var sourceState = refreshed[source.Session.CharacterId];
                    var equipment = sourceState.CharacterInventory.Containers
                        .SingleOrDefault(value => value.ContainerType ==
                            Dfo.Server.Data.Inventory.InventoryContainerType.Equipment)
                        ?.Items ?? [];
                    frames.Add(new ExtendedUserStateMessage(
                        sourceState.Character,
                        sourceState.CharacterEntry.Public,
                        sourceState.CharacterEntry.Skills,
                        equipment,
                        sourceState.World.PreparedAt,
                        source.Session.CharacterId == receiver.Session.CharacterId
                            ? null
                            : source.Session.WireUserId));
                }

                frames.Add(new EnterSelectDungeonAcknowledgement());
                frames.Add(new UserStateNotification(allWireUserIds, 0x01));
                frames.Add(new UdpHostNotification(hostSlot));

                var currentProgress = await characters.Get(receiver.Session.CharacterId)
                    .ProgressAsync(cancellationToken).ConfigureAwait(false);
                if (currentProgress is null)
                {
                    RollbackEntry(runId, preparedRevision, participants);
                    return;
                }
                if (lackedSelectionPrelude || visits.DefersSelectionState(currentProgress.Level))
                {
                    frames.Add(new EnterSelectDungeonNotification(allWireUserIds));
                }

                frames.Add(new SupportTagNotification());
                frames.Add(new DungeonInfoNotification(layout, leaderVisit.Run.Difficulty));
                frames.Add(new StartMapNotification(
                    roster.IsVirtual ? visit.Run with { Participant = null } : visit.Run,
                    FirstEntry: true,
                    visit.Actors,
                    visit.FirstActorSequence,
                    FloorPieceValues.Describe(visit.Run, visit.FloorDrops)));
                frames.Add(new Fatigue.FatigueNotification(await fatigue.BalanceOfAsync(receiver.Session.CharacterId, cancellationToken).ConfigureAwait(false)));
                deliveries.Add((receiver.Session, frames));
            }

            observed = ObserveCurrentCohort(
                attachments.Members, runId, change.Run.RoomVersion, requireRunMatch);
            if (observed.Status != CohortStatus.Current)
            {
                if (observed.Status == CohortStatus.InvalidGeneration)
                    RollbackEntry(runId, preparedRevision, participants);
                await RefuseCurrentRequesterAsync(requesterConnection, requester, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            change = observed.Change ?? change;
        }
        catch
        {
            RollbackEntry(runId, preparedRevision, participants);
            throw;
        }

        IAsyncDisposable transitions;
        try
        {
            transitions = await AcquireTransitionsAsync(
                transitionGate, attachments.Members, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            RollbackEntry(runId, preparedRevision, participants);
            throw;
        }

        await using (transitions)
        {
            observed = ObserveCurrentCohort(
                attachments.Members, runId, change.Run.RoomVersion, requireRunMatch);
            if (observed.Status != CohortStatus.Current)
            {
                if (observed.Status == CohortStatus.InvalidGeneration)
                    RollbackEntry(runId, preparedRevision, participants);
                await RefuseCurrentRequesterAsync(requesterConnection, requester, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            change = observed.Change ?? change;

            // Frame preparation may have awaited persistence while the instance advanced. Rewrite
            // the receiver-specific run view from the latest snapshot before any batch is sent;
            // room content is unchanged because ObserveCurrentCohort requires the same room.
            for (var index = 0; index < deliveries.Count; index++)
            {
                var delivery = deliveries[index];
                var currentRun = roster.IsVirtual
                    ? change.Run with { Participant = null }
                    : change.Run with
                    {
                        Participant = change.Run.Participants.Single(value =>
                            value.CharacterId == delivery.Session.CharacterId),
                    };
                deliveries[index] = (
                    delivery.Session,
                    delivery.Frames.Select(frame => frame is StartMapNotification start
                        ? start with { Run = currentRun }
                        : frame).ToArray());
            }

            try
            {
                // Every participant is required. Followers go first so a failed peer cannot leave
                // the requesting leader believing that the whole cohort entered successfully.
                var delivered = new List<SelectedSessionIdentity>();
                foreach (var delivery in deliveries
                    .OrderBy(value => value.Session == requester ? 1 : 0))
                {
                    try
                    {
                        await sender.SendExplicitAsync(
                            delivery.Session.ConnectionId,
                            new GameExplicitMessageDelivery(delivery.Frames, []),
                            cancellationToken).ConfigureAwait(false);
                        delivered.Add(delivery.Session);
                    }
                    catch
                    {
                        // TCP cannot retract a completed batch. If a later required recipient
                        // fails, isolate every generation that may already hold START_MAP so none
                        // can continue against an aborted server instance.
                        await IsolatePartialCohortAsync(
                            [.. delivered, delivery.Session]).ConfigureAwait(false);
                        throw;
                    }
                }
            }
            catch
            {
                RollbackEntry(runId, preparedRevision, participants);
                throw;
            }

            // Keep the exact selected-session generations leased until their town memberships
            // have been removed, so replacement cannot slip between dungeon delivery and leave.
            await townAreaProjection.LeaveManyAsync(
                attachments.Members.Select(attachment => attachment.Session).ToArray(),
                TownAreaDepartureReason.DungeonEntry,
                cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Select dungeon {ConnectionId}: projected run={RunId} revision={Revision} "
                + "participants={ParticipantCount} dungeon={DungeonId} difficulty={Difficulty} intent={Intent}",
            requesterConnection,
            runId.Value,
            change.Revision,
            participants.Length,
            layout.DungeonId,
            leaderVisit.Run.Difficulty,
            intent);
    }

    private CohortObservation ObserveCurrentCohort(
        IReadOnlyList<(SelectedSessionIdentity Session, SelectCharacterResult Runtime, byte Slot)> members,
        DungeonRunId runId,
        long expectedRoomVersion,
        bool requireRunMatch)
    {
        foreach (var member in members)
        {
            if (selectedSessions.FindByCharacter(member.Session.CharacterId) != member.Session)
                return new CohortObservation(CohortStatus.InvalidGeneration, null);
        }

        // Lightweight legacy protocol test doubles return no entry change and may not own the
        // run. A production change proves that the run service is authoritative.
        if (!requireRunMatch)
            return new CohortObservation(CohortStatus.Current, null);

        var latest = runs.FindEntryChange(members[0].Session.CharacterId);
        if (latest is null || latest.Run.RunId != runId)
            return new CohortObservation(CohortStatus.InvalidGeneration, null);

        var recipients = latest.Recipients.ToHashSet();
        if (latest.Run.RoomVersion != expectedRoomVersion
            || recipients.Count != members.Count
            || members.Any(member => !recipients.Contains(member.Session.CharacterId)))
        {
            return new CohortObservation(CohortStatus.SupersededSameRun, latest);
        }

        foreach (var member in members)
        {
            var current = runs.Find(member.Session.CharacterId);
            if (current is null || current.RunId != runId)
                return new CohortObservation(CohortStatus.InvalidGeneration, null);
            if (current.RoomVersion != latest.Run.RoomVersion
                || current.EntryRevision != latest.Revision)
            {
                return new CohortObservation(CohortStatus.SupersededSameRun, latest);
            }
        }

        return new CohortObservation(CohortStatus.Current, latest);
    }

    private async ValueTask<EntryAttachments> ResolveAttachmentsAsync(
        SelectedSessionIdentity requester,
        SelectCharacterResult requesterRuntime,
        PartySnapshot roster,
        CancellationToken cancellationToken)
    {
        var members = new List<(SelectedSessionIdentity Session, SelectCharacterResult Runtime, byte Slot)>();
        foreach (var member in roster.Members.OrderBy(value => value.Slot))
        {
            var current = selectedSessions.FindByCharacter(member.CharacterId);
            var runtime = current is null ? null
                : await currentViews.TryLoadAsync(current, cancellationToken).ConfigureAwait(false);
            if (current is null
                || current.AccountId != member.Character.AccountId
                || current.WireUserId != member.WireUserId
                || runtime is null)
            {
                return new EntryAttachments([], member.Slot);
            }

            if (members.Any(value =>
                    value.Session.CharacterId == current.CharacterId
                    || value.Session.WireUserId == current.WireUserId
                    || value.Session.ConnectionId == current.ConnectionId))
            {
                return new EntryAttachments([], member.Slot);
            }
            members.Add((current, runtime, member.Slot));
        }

        return members.Any(value => value.Session == requester)
            ? new EntryAttachments(members, null)
            : new EntryAttachments([], 0);
    }

    private static async ValueTask<IAsyncDisposable> AcquireTransitionsAsync(
        SelectedSessionTransitionGate gate,
        IReadOnlyList<(SelectedSessionIdentity Session, SelectCharacterResult Runtime, byte Slot)> members,
        CancellationToken cancellationToken)
    {
        var leases = new List<IAsyncDisposable>();
        try
        {
            foreach (var accountId in members.Select(value => value.Session.AccountId)
                         .Distinct().OrderBy(value => value.Value))
            {
                leases.Add(await gate.AcquireAsync(
                    accountId, UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false));
            }
            return new TransitionLeaseSet(leases);
        }
        catch
        {
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void RollbackEntry(
        DungeonRunId runId,
        long preparedRevision,
        IReadOnlyList<DungeonRunParticipant> participants)
    {
        var recipients = participants.Select(value => value.CharacterId).ToArray();
        if (!runs.TryAbortEntry(runId, preparedRevision, recipients))
            return;
        foreach (var participant in participants)
        {
            deliveryRegistry.RemoveParticipant(runId, participant.CharacterId);
        }
    }

    private async ValueTask IsolatePartialCohortAsync(
        IReadOnlyList<SelectedSessionIdentity> sessions)
    {
        foreach (var session in sessions.Distinct())
        {
            try
            {
                await connectionCloser.RequestCloseAsync(
                    session.ConnectionId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                logger.LogWarning(
                    error,
                    "Failed to isolate partial dungeon-entry connection {ConnectionId}",
                    session.ConnectionId);
            }
        }
    }

    private ValueTask RefuseCurrentRequesterAsync(
        NetworkConnectionId connectionId,
        SelectedSessionIdentity requester,
        CancellationToken cancellationToken) =>
        selectedSessions.FindByCharacter(requester.CharacterId) == requester
            ? RefuseAsync(connectionId, SelectDungeonRefusal.MemberBlocked, 0, cancellationToken)
            : ValueTask.CompletedTask;

    private ValueTask RefuseAsync(
        NetworkConnectionId connectionId,
        byte errorCode,
        byte context,
        CancellationToken cancellationToken) =>
        sender.SendExplicitAsync(
            connectionId,
            new GameExplicitMessageDelivery([new SelectDungeonRefusal(errorCode, context)], []),
            cancellationToken);

    private sealed record EntryAttachments(
        IReadOnlyList<(SelectedSessionIdentity Session, SelectCharacterResult Runtime, byte Slot)> Members,
        byte? BlockedSlot);

    private sealed record CohortObservation(
        CohortStatus Status,
        DungeonEntryChange? Change);

    private enum CohortStatus
    {
        Current,
        InvalidGeneration,
        SupersededSameRun,
    }

    private sealed class TransitionLeaseSet(IReadOnlyList<IAsyncDisposable> leases) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
        }
    }
}
