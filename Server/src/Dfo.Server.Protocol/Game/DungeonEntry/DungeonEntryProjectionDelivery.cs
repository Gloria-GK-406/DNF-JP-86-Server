using Dfo.Server.Abstractions.Execution;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
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
    ILogger<DungeonEntryProjectionDelivery> logger,
    ICommandBus? commands = null,
    IInventorySlotProjectionDomainService? inventoryProjection = null,
    Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService? housing = null,
    IOutboundOrdering? ordering = null)
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
        var mirror = message.MirrorQuestId is not null;
        ValueTask RefuseAsync(NetworkConnectionId connection, byte code, byte slot, CancellationToken ct) =>
            this.RefuseAsync(connection, code, slot, ct, mirror);
        ValueTask RefuseCurrentRequesterAsync(NetworkConnectionId connection, SelectedSessionIdentity owner, CancellationToken ct) =>
            selectedSessions.IsInWorld(owner)
                ? RefuseAsync(connection, SelectDungeonRefusal.SelectionCohortUnavailable, 0, ct) : ValueTask.CompletedTask;
        void RestoreSelection()
        {
            if (selectedSessions.IsInWorld(requester))
                entryIntents.RestoreIfAbsent(requester, intent);
        }

        var attachments = await ResolveAttachmentsAsync(
            requester, requesterRuntime, roster, cancellationToken).ConfigureAwait(false);
        if (attachments.BlockedSlot is { } blockedSlot)
        {
            RestoreSelection();
            await RefuseAsync(requesterConnection, SelectDungeonRefusal.SelectionCohortUnavailable, blockedSlot,
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
        var command = new EnterDungeonCommand(new DungeonEntryContext(
            message.DungeonId, facts, message.RequestMode, message.PracticeMode, participants, message.MirrorQuestId))
        {
            Session = requester,
            Sessions = attachments.Members.Select(x => x.Session).ToArray(),
        };
        // Directly constructed protocol fixtures may supply their own visit service. They still
        // execute the same CMD routing; the production composition always uses the command bus.
        var attempt = commands is not null
            ? await commands.SendAsync(command, cancellationToken).ConfigureAwait(false)
            : await new EnterDungeonCommandHandler(new Dfo.Server.Application.World.DungeonEntryDecisionDomainService(), visits)
                .HandleAsync(new(Guid.NewGuid(), typeof(EnterDungeonCommand), typeof(EnterDungeonCommandHandler),
                    CommandExecutionKind.Workflow), command, cancellationToken).ConfigureAwait(false);
        var entered = attempt.Entry;
        if (entered is null)
        {
            RestoreSelection();
            if (attempt.Failure is not null)
            {
                var error = mirror ? DungeonEntryRefusalCodes.ForMirror(attempt.Failure.Value)
                    : DungeonEntryRefusalCodes.For(attempt.Failure.Value, message.RequestMode == 1);
                logger.Log(attempt.Failure is DungeonEntryFailure.ConfigurationInvalid or DungeonEntryFailure.HellConfigurationInvalid
                        ? LogLevel.Warning : LogLevel.Debug, "Dungeon {DungeonId} entry refused: {Failure}, code=0x{ErrorCode:X2}, memberSlot={BlockedSlot}, detail={Detail}",
                    message.DungeonId, attempt.Failure, error, attempt.BlockedSlot ?? 0, attempt.Detail);
                await RefuseAsync(requesterConnection, error, attempt.BlockedSlot ?? 0, cancellationToken).ConfigureAwait(false);
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

                frames.AddRange(await HousingCurrentProjection.ReadAsync(housing, receiver.Session, cancellationToken).ConfigureAwait(false));
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
                if (lackedSelectionPrelude || visits.DefersSelectionState(currentProgress.Level, refreshed[receiver.Session.CharacterId].Character.TutorialCompleted))
                {
                    frames.Add(new EnterSelectDungeonNotification(allWireUserIds));
                }

                if (layout.Mirror is { } mission)
                    frames.Add(new Dfo.Server.Protocol.Game.MirrorDungeonEntry.MirrorDungeonMissionNotification(mission.QuestId));
                frames.Add(new SupportTagNotification());
                foreach (var debit in entered.InventoryChanges.Where(x => x.CharacterId == receiver.Session.CharacterId))
                {
                    var slot = (inventoryProjection ?? throw new InvalidOperationException("Inventory projection is required for entry costs."))
                        .Project(debit.Slot);
                    frames.Add(debit.Remaining is { } remaining
                        ? new InventoryDeltaMessage(slot.Space, [new Dfo.Server.Data.Inventory.InventoryItemState(null, slot.Index, remaining)], refreshed[receiver.Session.CharacterId].World.PreparedAt)
                        : GameInventoryDelivery.ClearedSlotNotification(slot.Space, slot.Index, refreshed[receiver.Session.CharacterId].World.PreparedAt));
                }
                if (layout.Altar is { } altar)
                {
                    frames.Add(new Dfo.Server.Protocol.Game.Altar.AltarInfoMessage(layout.DungeonId, altar.Kind));
                    frames.Add(new Dfo.Server.Protocol.Game.Altar.AltarMapMessage(visit.Run));
                    frames.Add(GameInventoryDelivery.CarriedGoldNotification(
                        await characters.Get(receiver.Session.CharacterId).GoldAsync(cancellationToken).ConfigureAwait(false),
                        refreshed[receiver.Session.CharacterId].World.PreparedAt));
                }
                else if (layout.Tournament is { } tournament)
                {
                    frames.Add(new Dfo.Server.Protocol.Game.Tournament.TournamentInfoMessage(tournament));
                    frames.Add(new Dfo.Server.Protocol.Game.Tournament.TournamentMapMessage(tournament));
                }
                else
                {
                    frames.Add(new DungeonInfoNotification(layout, leaderVisit.Run.Difficulty));
                    frames.Add(new StartMapNotification(
                        roster.IsVirtual ? visit.Run with { Participant = null } : visit.Run,
                        FirstEntry: true,
                        visit.Actors,
                        visit.FirstActorSequence,
                        FloorPieceValues.Describe(visit.Run, visit.FloorDrops)));
                }
                if (layout.Despair is { Floor: >= 10 and <= 90 } tower && tower.Floor % 10 == 0)
                {
                    var view = refreshed[receiver.Session.CharacterId];
                    var equipment = view.CharacterInventory.Containers.SingleOrDefault(x => x.ContainerType ==
                        Dfo.Server.Data.Inventory.InventoryContainerType.Equipment)?.Items ?? [];
                    frames.Add(new DespairApcInfoNotification(0, view.Character, equipment));
                    frames.Add(new DespairApcInfoNotification(checked((byte)tower.Floor), view.Character, equipment));
                }
                if (HellPartyProjection.For(visit.Run) is { } hellInfo) frames.Add(hellInfo);
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
            // XO-1: their batches are delivered before the requester's; a follower whose delivery
            // fails is closed alone and the requester's batch still goes (BestEffort, VC-3).
            using var order = ordering?.Begin("XO-1 party dungeon entry", OutboundDelivery.BestEffort);
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
                    // A batch already registered is still delivered when the job ends (RQ-031(c)).
                    // If a later recipient's batch cannot be registered (its encoding or endpoint
                    // check fails), isolate every generation that may already hold START_MAP so
                    // none can continue against an aborted server instance.
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

        // Town memberships are removed right after the dungeon delivery, before any other job runs.
        await townAreaProjection.LeaveManyAsync(
            attachments.Members.Select(attachment => attachment.Session).ToArray(),
            TownAreaDepartureReason.DungeonEntry,
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Select dungeon {ConnectionId}: projected run={RunId} revision={Revision} "
                + "participants={ParticipantCount} dungeon={DungeonId} difficulty={Difficulty} intent={Intent} "
                + "maze={MazeIndex} size={Width}x{Height} startMap={StartMap} purpose={Purpose} quest={QuestId}",
            requesterConnection,
            runId.Value,
            change.Revision,
            participants.Length,
            layout.DungeonId,
            leaderVisit.Run.Difficulty,
            intent, layout.MazeIndex, layout.Width, layout.Height, layout.StartRoomMapId,
            layout.Purpose.Kind, layout.Purpose.QuestId);
    }

    private CohortObservation ObserveCurrentCohort(
        IReadOnlyList<(SelectedSessionIdentity Session, SelectCharacterResult Runtime, byte Slot)> members,
        DungeonRunId runId,
        long expectedRoomVersion,
        bool requireRunMatch)
    {
        foreach (var member in members)
        {
            if (!selectedSessions.IsInWorld(member.Session))
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
            var current = selectedSessions.TryGetInWorld(member.CharacterId, out var inWorld) ? inWorld : null;
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
            await connectionCloser.RequestCloseAsync(
                session.ConnectionId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private ValueTask RefuseCurrentRequesterAsync(
        NetworkConnectionId connectionId,
        SelectedSessionIdentity requester,
        CancellationToken cancellationToken) =>
        selectedSessions.IsInWorld(requester)
            ? RefuseAsync(connectionId, SelectDungeonRefusal.SelectionCohortUnavailable, 0, cancellationToken)
            : ValueTask.CompletedTask;

    private ValueTask RefuseAsync(
        NetworkConnectionId connectionId,
        byte errorCode,
        byte context,
        CancellationToken cancellationToken, bool mirror = false) =>
        sender.SendExplicitAsync(
            connectionId,
            new GameExplicitMessageDelivery([new SelectDungeonRefusal(errorCode, context, mirror)], []),
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
}
