using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
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




using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game;

using Dfo.Server.Protocol.Game.BossTowerInfo;
using Dfo.Server.Protocol.Game.BossTowerMap;
using Dfo.Server.Protocol.Game.BossTowerReward;
using Dfo.Server.Protocol.Game.BossTowerContinue;

public sealed class BossTowerProjectionDelivery(
    IBossTowerDomainService towers,
    ICommandBus commands,
    ISelectedSessionDomainService sessions,
    IPartyService parties,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ICharacterStateProjectionDomainService currentState,
    ICharacterDomainService characters,
    IOnlineCharacterRuntimeDomainService onlineCharacters,
    IDungeonEntryIntentDomainService entryIntents,
    IGameMessageSender sender,
    TownAreaProjectionDelivery townProjection,
    TimeProvider clock,
    INetworkConnectionCloser connections,
    ILogger<BossTowerProjectionDelivery> logger,
    Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService? housing = null,
    IOutboundOrdering? ordering = null)
{
    private static readonly IInventorySlotMappingRuleService SlotMappings = new InventorySlotMappingRuleService();

    public async ValueTask<bool> EnterAsync(
        NetworkConnectionId requesterConnection,
        SelectedSessionIdentity requester,
        PartySnapshot roster,
        DungeonEntryIntent intent,
        Action restoreIntent,
        CancellationToken token, byte requestMode = 0, byte difficulty = 0, byte practiceMode = 0)
    {
        var attachments = await ResolveAttachmentsAsync(requester, roster, token).ConfigureAwait(false);
        if (attachments is null)
        {
            restoreIntent();
            await RefuseAsync(requesterConnection, SelectDungeonRefusal.SelectionCohortUnavailable, token).ConfigureAwait(false);
            return true;
        }

        Exception? deliveryFailure = null;
        var committed = false;
        var isolate = new List<SelectedSessionIdentity>();
        try
        {
            if (!Current(attachments) || !PartyCurrent(requester, roster, attachments))
            {
                restoreIntent();
                await RefuseAsync(requesterConnection, SelectDungeonRefusal.SelectionCohortUnavailable, token).ConfigureAwait(false);
                return true;
            }

            var entered = await commands.SendAsync(new EnterBossTowerCommand(
                attachments.Select(value => new TowerEntryMember(value.Session, value.Slot)).ToArray(),
                requester, unchecked((uint)Random.Shared.Next()), requestMode, difficulty, practiceMode), token).ConfigureAwait(false);
            if (entered.Run is null)
            {
                restoreIntent();
                await RefuseAsync(requesterConnection,
                    entered.Failure is { } failure ? DungeonEntryRefusalCodes.For(failure, false) : entered.Rejection switch
                    {
                        "LevelTooLow" => SelectDungeonRefusal.LevelTooLow,
                        "InsufficientMaterial" or "MissingRequiredItem" => SelectDungeonRefusal.MissingRequiredItem,
                        "InvalidParty" or "NoSession" or "AlreadyInDungeon" => SelectDungeonRefusal.InvalidSelectionState,
                        _ => SelectDungeonRefusal.DungeonUnavailable,
                    },
                    token, entered.BlockedSlot ?? 0).ConfigureAwait(false);
                return true;
            }

            committed = true;
            isolate.AddRange(attachments.Select(value => value.Session));
            var run = entered.Run;
            if (!Current(attachments, run))
                throw new InvalidOperationException("Tower cohort changed after committed admission.");

            foreach (var member in attachments.Where(value => value.Session != requester))
            {
                if (sessions.IsInWorld(member.Session))
                    entryIntents.Consume(member.Session);
            }

            var refreshed = new Dictionary<CharacterId, SelectCharacterResult>();
            foreach (var member in attachments.OrderBy(value => value.Slot))
            {
                // The attachment was loaded before admission. Attribute refresh alone keeps
                // its inventory snapshot, so reread after the temporary bindings commit.
                var runtime = await currentViews.TryLoadAsync(member.Session, token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Tower member view disappeared after admission.");
                refreshed[member.Session.CharacterId] = await currentState.RefreshAsync(runtime, token).ConfigureAwait(false);
            }

            var ordered = attachments.OrderBy(value => value.Slot).ToArray();
            var wireUsers = ordered.Select(value => value.Session.WireUserId).ToImmutableArray();
            var hostSlot = ordered.Single(value => value.Session == requester).Slot;
            var batches = new List<(SelectedSessionIdentity Session, IReadOnlyList<IGameServerMessage> Frames)>();
            foreach (var receiver in ordered)
            {
                var frames = new List<IGameServerMessage>();
                // A21 recalculates player attributes on DELETE_ITEM. Deliver the committed
                // debit before 008E/008F switch it into tower mode, whose table can still be null.
                foreach (var debit in entered.InventoryChanges.Where(value => value.CharacterId == receiver.Session.CharacterId))
                    AppendDebit(frames, debit, clock.GetUtcNow());
                foreach (var source in ordered
                    .OrderBy(value => value.Session == receiver.Session ? 0 : 1)
                    .ThenBy(value => value.Slot))
                {
                    var state = refreshed[source.Session.CharacterId];
                    var equipment = state.CharacterInventory.Containers
                        .SingleOrDefault(value => value.ContainerType == InventoryContainerType.Equipment)?.Items ?? [];
                    frames.Add(new ExtendedUserStateMessage(
                        state.Character, state.CharacterEntry.Public, state.CharacterEntry.Skills,
                        equipment, state.World.PreparedAt,
                        source.Session == receiver.Session ? null : source.Session.WireUserId));
                }
                frames.AddRange(await HousingCurrentProjection.ReadAsync(housing, receiver.Session, token).ConfigureAwait(false));
                frames.Add(new EnterSelectDungeonAcknowledgement());
                frames.Add(new UserStateNotification(wireUsers, 0x01));
                frames.Add(new UdpHostNotification(hostSlot));
                frames.Add(new SupportTagNotification());
                frames.Add(new BossTowerInfoNotification(run.DungeonId, run.TotalLevels));
                frames.Add(new BossTowerMapNotification(run.Room));
                batches.Add((receiver.Session, frames));
            }

            // XO-3: every member's batch is delivered before the requester's, each BestEffort.
            using (ordering?.Begin("XO-3 boss tower entry", OutboundDelivery.BestEffort))
            {
                foreach (var batch in batches.OrderBy(value => value.Session == requester ? 1 : 0))
                    await sender.SendExplicitAsync(batch.Session.ConnectionId,
                        new GameExplicitMessageDelivery(batch.Frames, []), token).ConfigureAwait(false);
            }
            await townProjection.LeaveManyAsync(ordered.Select(value => value.Session).ToArray(),
                TownAreaDepartureReason.DungeonEntry, token).ConfigureAwait(false);
        }
        catch (Exception error) when (committed)
        {
            deliveryFailure = error;
        }

        if (deliveryFailure is not null)
        {
            logger.LogWarning(deliveryFailure,
                "Boss tower entry projection failed after commit; isolating {Count} exact sessions", isolate.Count);
            foreach (var member in isolate.Distinct())
                await CloseQuietlyAsync(member).ConfigureAwait(false);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(deliveryFailure).Throw();
        }
        return true;
    }

    public async ValueTask<bool> CompleteLoadingAsync(NetworkConnectionId connectionId,
        GameMessageMetadata metadata, CancellationToken token)
    {
        if (Stale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : towers.Find(session);
        if (session is null || expected is null) return false;
        IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures = [];
        if (!Exact(session, expected)) return true;
        var action = await commands.SendAsync(
            new LoadBossTowerCommand(Context(expected, session)) { Sessions = Members(expected) }, token).ConfigureAwait(false);
        if (action.Run is not { Room.Phase: TowerRoomPhase.Ready } run) return true;
        var leader = run.Members.Single(value => value.IsLeader).Session;
        var started = await commands.SendAsync(new StartBossTowerCommand(Context(run, leader)) { Sessions = Members(run) }, token).ConfigureAwait(false);
        if (started.Run is not { Room.Phase: TowerRoomPhase.Fighting } fighting) return true;
        failures = await SendToExactAsync(
            fighting, _ => [new LoadingCompleteNotification()], token).ConfigureAwait(false);
        await CloseProjectionFailuresAsync(failures, expected.RunId).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> StartAsync(NetworkConnectionId connectionId,
        GameMessageMetadata metadata, CancellationToken token)
    {
        if (Stale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : towers.Find(session);
        if (session is null || expected is null) return false;
        if (Exact(session, expected))
            _ = await commands.SendAsync(
                new StartBossTowerCommand(Context(expected, session)) { Sessions = Members(expected) }, token).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> AdvanceAsync(NetworkConnectionId connectionId,
        GameMessageMetadata metadata, byte targetX, byte targetY, CancellationToken token)
    {
        if (Stale(metadata))
        {
            logger.LogWarning("Move-map ignored connection={ConnectionId} sequence={Sequence} ordinal={Ordinal} target=({X},{Y}) reason=StaleSequence",
                connectionId, metadata.Sequence, metadata.InboundOrdinal, targetX, targetY);
            return true;
        }
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : towers.Find(session);
        if (session is null || expected is null) return false;
        logger.LogInformation("Boss tower move requested connection={ConnectionId} character={CharacterId} sequence={Sequence} ordinal={Ordinal} run={RunId} roomVersion={RoomVersion} stage={Stage} phase={Phase} target=({X},{Y})",
            connectionId, session.CharacterId.Value, metadata.Sequence, metadata.InboundOrdinal, expected.RunId,
            expected.Room.Version, expected.Room.Level, expected.Room.Phase, targetX, targetY);
        IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures = [];
        if (!Exact(session, expected))
        {
            logger.LogWarning("Boss tower move refused character={CharacterId} run={RunId} roomVersion={RoomVersion} reason=SnapshotOrSessionChanged",
                session.CharacterId.Value, expected.RunId, expected.Room.Version);
            return true;
        }
        var action = await commands.SendAsync(
            new AdvanceBossTowerCommand(Context(expected, session), targetX, targetY) { Sessions = Members(expected) }, token).ConfigureAwait(false);
        logger.LogInformation("Boss tower move result character={CharacterId} run={RunId} changed={Changed} stage={Stage} map={MapId} phase={Phase} roomVersion={RoomVersion}",
            session.CharacterId.Value, expected.RunId, action.Changed, action.Run?.Room.Level,
            action.Run?.Room.MapId, action.Run?.Room.Phase, action.Run?.Room.Version);
        if (action.Changed && action.Run is { } run)
            failures = await SendToExactAsync(
                run, _ => [new BossTowerMapNotification(run.Room)], token).ConfigureAwait(false);
        await CloseProjectionFailuresAsync(failures, expected.RunId).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> KillAsync(NetworkConnectionId connectionId,
        GameMessageMetadata metadata, ushort actorUid, ushort killerUid, CancellationToken token)
    {
        if (Stale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : towers.Find(session);
        if (session is null || expected is null) return false;
        IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures = [];
        if (!Exact(session, expected)) return true;
        var action = await commands.SendAsync(
            new KillBossTowerActorCommand(Context(expected, session), actorUid) { Sessions = Members(expected) }, token).ConfigureAwait(false);
        if (!action.Changed || action.Run is null) return true;
        failures = await ProjectActionAsync(
            action, new MonsterDeathNotification(actorUid, action.Drops.Select(item => new MonsterDrop(item.Uid,
                item.Rolled)).ToArray(), killerUid), token).ConfigureAwait(false);
        await CloseProjectionFailuresAsync(failures, expected.RunId).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> PickupAsync(NetworkConnectionId connectionId,
        GameMessageMetadata metadata, ushort groundUid, CancellationToken token)
    {
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : towers.Find(session);
        if (session is null || expected is null) return false;
        if (Stale(metadata)) return true;
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        if (!Exact(session, expected)) return true;
        var picked = await commands.SendAsync(new PickupBossTowerItemCommand(Context(expected, session), groundUid) { Sessions = Members(expected) }, token).ConfigureAwait(false);
        if (!picked.Action.Changed || picked.Action.Run is not { } run)
        {
            if (picked.NoRoom) await sender.SendCmdAsync(connectionId, new GetItemResponse(false), token).ConfigureAwait(false);
            return true;
        }
        var item = run.GroundItems.Single(value => value.Uid == groundUid);
        foreach (var member in ExactMembers(run))
        {
            try
            {
                var frames = new List<IGameServerMessage>();
                if (member == session) frames.Add(new GetItemResponse(true));
                if (item.Rolled.IsGold)
                    frames.Add(new GroundGoldPickupNotification(groundUid, session.WireUserId, item.Rolled.Count));
                else
                {
                    var update = picked.Action.InventoryUpdates.FirstOrDefault();
                    frames.Add(new GroundPickupNotification(groundUid, session.WireUserId,
                        update is null ? (ushort)0 : checked((ushort)SlotMappings.ToGameSlot(update.Slot).Index)));
                }
                if (member == session)
                {
                    AppendInventory(frames, picked.Action.InventoryUpdates, clock.GetUtcNow());
                    AppendQuest(frames, picked.Action.QuestProgress);
                    if (picked.Action.GoldBalance is { } balance)
                        frames.Add(GameInventoryDelivery.CarriedGoldNotification(balance, clock.GetUtcNow()));
                }
                await SendCommittedBatchAsync(run, member, new GameExplicitMessageDelivery(frames, []), token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
            { failures.Add((member, error)); }
        }
        await CloseProjectionFailuresAsync(failures, expected.RunId).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> DieAsync(NetworkConnectionId connectionId,
        GameMessageMetadata metadata, CancellationToken token)
    {
        if (Stale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : towers.Find(session);
        if (session is null || expected is null) return false;
        IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures = [];
        if (!Exact(session, expected)) return true;
        var action = await commands.SendAsync(
            new DieInBossTowerCommand(Context(expected, session)) { Sessions = Members(expected) }, token).ConfigureAwait(false);
        if (!action.Changed || action.Run is null) return true;
        failures = await ProjectActionAsync(
            action, new CharacterLifeStateNotification(session.WireUserId), token,
            requester: session, requesterResponse: new CharacterDeathResponse()).ConfigureAwait(false);
        await CloseProjectionFailuresAsync(failures, expected.RunId).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> SelectReturnAsync(NetworkConnectionId connectionId,
        GameMessageMetadata metadata, byte state, byte option, CancellationToken token)
    {
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : towers.Find(session);
        if (session is null || expected is null) return false;
        if (Stale(metadata)) return true;
        var failures = new List<(SelectedSessionIdentity Session, Exception Error)>();
        if (!Exact(session, expected)) return true;
        if (state == 2)
        {
            await sender.SendCmdAsync(connectionId, new EplpCommandResponse(state, option), token).ConfigureAwait(false);
            return true;
        }
        if (state != 1 || option > 2) return true;
        var action = await commands.SendAsync(new SelectBossTowerResultCommand(Context(expected, session), option) { Sessions = Members(expected) }, token).ConfigureAwait(false);
        if (!action.Changed || action.Run is not { } run) return true;
        var frozen = ExactMembers(run);
        failures.AddRange(await SendToExactAsync(run, _ => [new EplpCommandResponse(state, option)], token).ConfigureAwait(false));
        // Native Boss module sends 000F then 0010 for buttons 0/1. Admission belongs to 0010.
        if (option == 2)
        {
            foreach (var member in frozen)
                _ = await commands.SendAsync(new LeaveBossTowerCommand(Context(run, member), false) { Sessions = Members(run) }, CancellationToken.None).ConfigureAwait(false);
            await ReturnMembersIsolatedAsync(frozen, null, failures, CancellationToken.None, false).ConfigureAwait(false);
        }
        await CloseProjectionFailuresAsync(failures, expected.RunId).ConfigureAwait(false);
        return true;
    }

    /// <returns>True if a current Boss run consumed/rejected this request; false allows the ordinary selection projection.</returns>
    public async ValueTask<bool> PrepareSelectionAsync(NetworkConnectionId connectionId,
        GameMessageMetadata metadata, int requestedDungeonId, CancellationToken token)
    {
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : towers.Find(session);
        if (session is null || expected is null) return false;
        if (Stale(metadata)) return true;
        if (!Exact(session, expected) || !expected.Members.Any(value => value.Session == session && value.IsLeader)) return true;
        var released = await commands.SendAsync(new ReleaseBossTowerSelectionCommand(Context(expected, session), requestedDungeonId) { Sessions = Members(expected) }, token).ConfigureAwait(false);
        return !released.Changed;
    }

    public bool HasRun(SelectedSessionIdentity session) => towers.Find(session) is not null;

    public async ValueTask<bool> LeaveAndReturnAsync(NetworkConnectionId connectionId,
        GameMessageMetadata metadata, IGameServerMessage? response, CancellationToken token)
    {
        if (Stale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : towers.Find(session);
        if (session is null || expected is null) return false;
        BossTowerActionResult? committed = null;
        if (!Exact(session, expected)) return true;
        var action = await commands.SendAsync(
            new LeaveBossTowerCommand(Context(expected, session), Disconnected: false) { Sessions = Members(expected) }, token).ConfigureAwait(false);
        if (action.Changed)
            committed = action;
        if (committed is not null)
            await ProjectCommittedDepartureAsync(committed, session, response, token).ConfigureAwait(false);
        return true;
    }

    public async ValueTask SweepAsync(DateTimeOffset now, CancellationToken token)
    {
        foreach (var context in towers.FindReturnsDue(now))
        {
            IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures;
            try
            {
                failures = await SweepOneAsync(context, now, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                logger.LogWarning(error,
                    "Boss tower return sweep failed for run {RunId}; continuing with other due runs",
                    context.RunId);
                continue;
            }

            // Transport close may synchronously enter selected-session cleanup, so it runs only
            // after SweepOneAsync has finished with the run.
            foreach (var (session, error) in failures)
            {
                logger.LogWarning(error,
                    "Boss tower return projection failed for exact session {ConnectionId} in run {RunId}",
                    session.ConnectionId, context.RunId);
                await CloseQuietlyAsync(session).ConfigureAwait(false);
            }
        }
    }

    public ValueTask ProjectDisconnectedActionAsync(BossTowerActionResult action, CancellationToken token) =>
        ProjectCommittedDepartureAsync(action, null, null, token);

    public async ValueTask ProjectCommittedDepartureAsync(BossTowerActionResult action,
        SelectedSessionIdentity? returning, IGameServerMessage? response, CancellationToken token)
    {
        if (!action.Changed || action.Run is null) return;
        var captured = action.Run;
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var participants = captured.Members.Select(member => member.Session)
            .Append(returning).OfType<SelectedSessionIdentity>().Distinct().ToArray();
        var exact = ExactMembers(captured);
        var current = CurrentRun(captured);
        if (exact.Count > 0 && current?.Members.SingleOrDefault(member => member.IsLeader) is { } leader)
        {
            failures.AddRange(await SendToExactAsync(
                current, _ => [new UdpHostNotification(leader.Slot)], token).ConfigureAwait(false));
        }
        current = CurrentRun(captured);
        if (current is not null && ExactMembers(current).Count == current.Members.Length
            && current.Room.Phase == TowerRoomPhase.Ready
            && current.Members.SingleOrDefault(member => member.IsLeader) is { } readyLeader)
        {
            var start = await commands.SendAsync(new StartBossTowerCommand(Context(current, readyLeader.Session)) { Sessions = Members(current) }, token).ConfigureAwait(false);
            if (start.Run is { Room.Phase: TowerRoomPhase.Fighting } fighting)
                failures.AddRange(await SendToExactAsync(fighting, _ => [new LoadingCompleteNotification()], token).ConfigureAwait(false));
        }
        if (exact.Count > 0 && action.SettlementCreated && captured.Completion is { Settled: true })
            failures.AddRange(await ProjectActionAsync(action, null, token).ConfigureAwait(false));
        if (returning is not null
            && sessions.IsInWorld(returning)
            && !towers.IsInTower(returning.CharacterId))
            await ReturnMembersIsolatedAsync([returning], response, failures, token, false).ConfigureAwait(false);
        await CloseProjectionFailuresAsync(failures, captured.RunId).ConfigureAwait(false);
    }

    public async ValueTask ProjectCurrentHostAsync(
        BossTowerSnapshot captured, CancellationToken token)
    {
        if (captured.Members.IsEmpty)
            return;
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var current = CurrentRun(captured);
        if (current?.Members.SingleOrDefault(member => member.IsLeader) is { } leader)
        {
            failures.AddRange(await SendToExactAsync(
                current, _ => [new UdpHostNotification(leader.Slot)], token).ConfigureAwait(false));
        }
        await CloseProjectionFailuresAsync(failures, captured.RunId).ConfigureAwait(false);
    }

    public async ValueTask ReprojectCompletionAsync(BossTowerActionContext context, CancellationToken token)
    {
        var expected = towers.Find(context.Session);
        if (expected is null || expected.RunId != context.RunId || expected.Room.Version != context.RoomVersion
            || expected.Completion is not { Settled: true, Receipt: not null }) return;
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        failures.AddRange(await ProjectActionAsync(
            new BossTowerActionResult(false, expected) { SettlementCreated = true }, null, token)
            .ConfigureAwait(false));
        await CloseProjectionFailuresAsync(failures, expected.RunId).ConfigureAwait(false);
    }

    private static bool IsDue(BossTowerSnapshot run, DateTimeOffset now) =>
        run.Completion is { Settled: true } && run.ReturnDueAt is { } due && due <= now
        || run.Completion is null && run.Room.Phase == TowerRoomPhase.Loading && run.LoadDueAt <= now;

    private async ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)>> SweepOneAsync(
        BossTowerActionContext context, DateTimeOffset now, CancellationToken token)
    {
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var expected = towers.Find(context.Session);
        if (expected is null || expected.RunId != context.RunId || expected.Room.Version != context.RoomVersion
            || !IsDue(expected, now)) return failures;
        var frozen = ExactMembers(expected);
        if (frozen.Count == 0) return failures;
        // Remove dead members first: a loading timeout must not momentarily leave an all-dead
        // cohort and accidentally create a defeat settlement while abandoning the entire run.
        foreach (var member in frozen.OrderBy(member => expected.Members.Single(value => value.Session == member).Dead ? 0 : 1))
            _ = await commands.SendAsync(
                new LeaveBossTowerCommand(Context(expected, member), Disconnected: false) { Sessions = Members(expected) }, token).ConfigureAwait(false);
        await ReturnMembersIsolatedAsync(frozen, null, failures, CancellationToken.None, false).ConfigureAwait(false);
        return failures;
    }

    private async ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)>> ProjectActionAsync(
        BossTowerActionResult action, IGameServerMessage? shared,
        CancellationToken token, SelectedSessionIdentity? requester = null, IGameServerMessage? requesterResponse = null)
    {
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var run = action.Run!;
        foreach (var member in ExactMembers(run))
        {
            try
            {
                var frames = new List<IGameServerMessage>();
                var kill = action.KillRewards.SingleOrDefault(value => value.Session == member);
                if (kill is not null)
                {
                    await AppendExperienceAsync(frames, member, kill.Award, token).ConfigureAwait(false);
                    AppendInventory(frames, kill.InventoryUpdates, clock.GetUtcNow());
                    AppendQuest(frames, kill.QuestProgress);
                }
                if (requesterResponse is not null && member == requester) frames.Add(requesterResponse);
                if (shared is not null) frames.Add(shared);
                if (action.SettlementCreated
                    && run.Completion is { Settled: true, Receipt: { } receipt }
                    && receipt.Members.SingleOrDefault(value => value.Session == member) is { } settlement)
                    await AppendSettlementAsync(frames, run, settlement, receipt, member, token).ConfigureAwait(false);
                if (frames.Count > 0)
                    await SendCommittedBatchAsync(run, member,
                        new GameExplicitMessageDelivery(frames, []), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                failures.Add((member, error));
            }
        }
        return failures;
    }

    private async ValueTask SendCommittedBatchAsync(BossTowerSnapshot run,
        SelectedSessionIdentity member, GameExplicitMessageDelivery batch, CancellationToken token)
    {
        try
        {
            await sender.SendExplicitAsync(member.ConnectionId, batch, token).ConfigureAwait(false);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            // The first attempt may have sent part of the batch. Only replay these committed
            // projections; never execute the reward again.
            if (!Exact(member, run)
                || !Equals(towers.Find(member)?.Completion?.Receipt, run.Completion?.Receipt))
                throw;
            await sender.SendExplicitAsync(member.ConnectionId, batch, token).ConfigureAwait(false);
        }
    }

    private async ValueTask AppendSettlementAsync(List<IGameServerMessage> frames,
        BossTowerSnapshot run, TowerMemberSettlementReceipt member, TowerSettlementReceipt receipt,
        SelectedSessionIdentity session, CancellationToken token)
    {
        if (member.Award is { } award)
        {
            await AppendExperienceAsync(frames, session, award, token).ConfigureAwait(false);
            AppendQuest(frames, member.QuestProgress);
        }
        else
            AppendQuest(frames, member.QuestProgress);
        foreach (var group in member.InventoryUpdates.GroupBy(update => SlotMappings.ToGameSlot(update.Slot).Space))
        {
            frames.Add(GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(
                group.Key,
                group.Select(update =>
                {
                    var slot = SlotMappings.ToGameSlot(update.Slot);
                    return new InventoryItemState(update.Item.ItemUid, slot.Index, update.Item.ToCore());
                }).ToArray(), run.Completion!.CompletedAt)));
        }
        if (member.Gold > 0)
            frames.Add(GameInventoryDelivery.CarriedGoldNotification(member.GoldBalance, run.Completion!.CompletedAt));
        frames.Add(new BossTowerRewardNotification(member.Experience,
            receipt.Members.Where(value => value.Slot < 8)
                .ToDictionary(value => value.Slot, value => value.Candidates)));
        frames.Add(new BossTowerContinueNotification(
            await towers.CanRetryAsync(Context(run, session), token).ConfigureAwait(false)));
    }

    private async ValueTask AppendExperienceAsync(List<IGameServerMessage> frames,
        SelectedSessionIdentity session, ExperienceAward award, CancellationToken token)
    {
        var skillPoints = await characters.Get(session.CharacterId)
            .RemainingSkillPointsAsync(token).ConfigureAwait(false);
        if (skillPoints is not null)
            frames.Add(new ExperienceNotification(award.Level, award.NotificationTotal, skillPoints,
                AccountHonorLevel.Initial, ExperienceBonusLines.Of(award)));
    }

    private static void AppendInventory(List<IGameServerMessage> frames,
        IReadOnlyList<TowerInventoryUpdate> updates, DateTimeOffset at)
    {
        foreach (var group in updates.GroupBy(update => SlotMappings.ToGameSlot(update.Slot).Space))
            frames.Add(GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(group.Key,
                group.Select(update =>
                {
                    var slot = SlotMappings.ToGameSlot(update.Slot);
                    return new InventoryItemState(update.Item.ItemUid, slot.Index, update.Item.ToCore());
                }).ToArray(), at)));
    }

    private static void AppendQuest(List<IGameServerMessage> frames,
        Dfo.Server.Abstractions.Quests.QuestProgressSnapshot? progress)
    {
        if (progress is null) return;
        frames.Add(new AcceptedQuestProgressMessage(
            [.. progress.Quests.OrderBy(quest => quest.Slot)
                .Select(quest => new AcceptedQuestProgressState(
                    checked((ushort)quest.QuestId), unchecked((uint)quest.TriggerValue)))]));
    }

    private async ValueTask ReturnMembersIsolatedAsync(IReadOnlyList<SelectedSessionIdentity> returning,
        IGameServerMessage? response, List<(SelectedSessionIdentity, Exception)> failures,
        CancellationToken token, bool restoreInventory)
    {
        var entries = new List<TownAreaEntry>();
        foreach (var member in returning.Distinct())
        {
            if (!sessions.IsInWorld(member)) continue;
            try
            {
                var runtime = await currentViews.TryLoadAsync(member, token).ConfigureAwait(false);
                var position = onlineCharacters.PositionOf(member);
                if (runtime is null || position is null) continue;
                if (response is not null)
                    await sender.SendExplicitAsync(member.ConnectionId,
                        new GameExplicitMessageDelivery([response], []), token).ConfigureAwait(false);
                if (restoreInventory)
                {
                    var restored = await currentState.RefreshAsync(runtime, token).ConfigureAwait(false);
                    await sender.SendExplicitAsync(member.ConnectionId, new GameExplicitMessageDelivery([
                        new InventoryFullSnapshotMessage(restored.CharacterInventory.Containers.Single(value => value.ContainerType == InventoryContainerType.Main),
                            restored.CharacterInventory, restored.AccountInventory, restored.World.PreparedAt)], []), token).ConfigureAwait(false);
                }
                entries.Add(new TownAreaEntry(member, runtime, position));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                failures.Add((member, error));
            }
        }
        if (entries.Count == 0) return;
        try
        {
            await townProjection.EnterAsync(entries, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            foreach (var entry in entries)
                failures.Add((entry.Session, error));
        }
    }

    private async ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, SelectCharacterResult Runtime, byte Slot)>?>
        ResolveAttachmentsAsync(SelectedSessionIdentity requester, PartySnapshot roster, CancellationToken token)
    {
        var result = new List<(SelectedSessionIdentity, SelectCharacterResult, byte)>();
        foreach (var member in roster.Members.OrderBy(value => value.Slot))
        {
            var current = sessions.TryGetInWorld(member.CharacterId, out var inWorld) ? inWorld : null;
            var runtime = current is null ? null : await currentViews.TryLoadAsync(current, token).ConfigureAwait(false);
            if (current is null || runtime is null || current.AccountId != member.Character.AccountId
                || current.WireUserId != member.WireUserId || member.Slot > 3
                || result.Any(value => value.Item1.CharacterId == current.CharacterId
                    || value.Item1.ConnectionId == current.ConnectionId || value.Item1.WireUserId == current.WireUserId))
                return null;
            result.Add((current, runtime, member.Slot));
        }
        return result.Any(value => value.Item1 == requester) ? result : null;
    }

    private bool Current(IReadOnlyList<(SelectedSessionIdentity Session, SelectCharacterResult Runtime, byte Slot)> members,
        BossTowerSnapshot? run = null) => members.All(value =>
        sessions.IsInWorld(value.Session)
        && (run is null || towers.Find(value.Session) is { } current
            && current.RunId == run.RunId && current.Room.Version == run.Room.Version));

    private bool PartyCurrent(SelectedSessionIdentity requester, PartySnapshot expected,
        IReadOnlyList<(SelectedSessionIdentity Session, SelectCharacterResult Runtime, byte Slot)> members)
    {
        var current = parties.FindParty(new PartyCharacterSnapshot(
            requester.AccountId, requester.CharacterId, requester.WireUserId, null));
        return current.Key == expected.Key
            && current.Generation == expected.Generation
            && current.Revision == expected.Revision
            && current.LeaderWireUserId == requester.WireUserId
            && current.Members.OrderBy(value => value.Slot)
                .Select(value => (value.CharacterId, value.Slot))
                .SequenceEqual(members.OrderBy(value => value.Slot)
                    .Select(value => (value.Session.CharacterId, value.Slot)));
    }

    private bool Exact(SelectedSessionIdentity session, BossTowerSnapshot expected) =>
        sessions.IsInWorld(session)
        && towers.Find(session) is { } current
        && current.RunId == expected.RunId && current.Room.Version == expected.Room.Version;

    private IReadOnlyList<SelectedSessionIdentity> ExactMembers(BossTowerSnapshot run) =>
        run.Members.OrderBy(value => value.Slot).Select(value => value.Session)
            .Where(member => sessions.IsInWorld(member)
                && towers.Find(member) is { } current
                && current.RunId == run.RunId
                && current.Room.Version == run.Room.Version).ToArray();

    private BossTowerSnapshot? CurrentRun(BossTowerSnapshot captured)
    {
        foreach (var member in captured.Members.OrderBy(value => value.Slot))
        {
            if (!sessions.IsInWorld(member.Session))
                continue;
            if (towers.Find(member.Session) is { } current
                && current.RunId == captured.RunId
                && current.Room.Version == captured.Room.Version)
            {
                return current;
            }
        }
        return null;
    }

    private async ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)>> SendToExactAsync(
        BossTowerSnapshot run,
        Func<SelectedSessionIdentity, IReadOnlyList<IGameServerMessage>> frames, CancellationToken token)
    {
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        foreach (var member in ExactMembers(run))
        {
            try
            {
                await sender.SendExplicitAsync(member.ConnectionId,
                    new GameExplicitMessageDelivery(frames(member), []), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                failures.Add((member, error));
            }
        }
        return failures;
    }

    private static SelectedSessionIdentity[] Members(BossTowerSnapshot run) => run.Members.Select(value => value.Session).ToArray();

    private static BossTowerActionContext Context(BossTowerSnapshot run, SelectedSessionIdentity session) =>
        new(session, run.RunId, run.Room.Version);

    public static bool Stale(GameMessageMetadata metadata) =>
        metadata.InboundOrdinal is > 0 && !metadata.SequenceIsFresh;

    private static void AppendDebit(List<IGameServerMessage> frames, DungeonEntryInventoryChange debit, DateTimeOffset at)
    {
        var slot = SlotMappings.ToGameSlot(debit.Slot);
        frames.Add(debit.Remaining is null
            ? GameInventoryDelivery.ClearedSlotNotification(slot.Space, slot.Index, at)
            : GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(slot.Space,
                [new InventoryItemState(null, slot.Index, debit.Remaining)], at)));
    }

    private ValueTask RefuseAsync(NetworkConnectionId connection, byte code, CancellationToken token, byte context = 0) =>
        sender.SendExplicitAsync(connection,
            new GameExplicitMessageDelivery([new SelectDungeonRefusal(code, context)], []), token);

    private async ValueTask CloseQuietlyAsync(SelectedSessionIdentity session)
    {
        if (!sessions.IsInWorld(session))
            return;
        await connections.RequestCloseAsync(session.ConnectionId, CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask CloseProjectionFailuresAsync(
        IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures, Guid runId)
    {
        foreach (var (session, error) in failures.DistinctBy(value => value.Session))
        {
            logger.LogWarning(error,
                "Boss tower post-commit projection failed for exact session {ConnectionId} in run {RunId}",
                session.ConnectionId, runId);
            await CloseQuietlyAsync(session).ConfigureAwait(false);
        }
    }
}

public sealed class BossTowerDepartureProjectionQueue
{
    private readonly ConcurrentQueue<BossTowerActionResult> pending = new();

    public void Enqueue(BossTowerActionResult action)
    {
        GameplayWriteAssertion.Writing(nameof(BossTowerDepartureProjectionQueue) + "." + nameof(Enqueue));
        pending.Enqueue(action);
    }

    internal bool TryDequeue(out BossTowerActionResult? action)
    {
        GameplayWriteAssertion.Writing(nameof(BossTowerDepartureProjectionQueue) + "." + nameof(TryDequeue));
        return pending.TryDequeue(out action);
    }
}

public sealed class BossTowerReturnSweepService(
    BossTowerProjectionDelivery delivery,
    BossTowerDepartureProjectionQueue departures,
    TimeProvider clock,
    ILogger<BossTowerReturnSweepService> logger,
    IGameplayQueue queue) : BackgroundService
{
    private const string Command = "timer:BossTowerReturnSweepService";
    private readonly CoalescingGameplaySource ticks = new(queue);

    /// <summary>One tick as a timer CMD: the departure queue first, then the returns due now.</summary>
    public Task TickAsync(CancellationToken token) => GameplayTimerWork.RunAsync(queue, Command, TickCoreAsync, token);

    public ValueTask SweepAsync(DateTimeOffset now, CancellationToken token) => delivery.SweepAsync(now, token);

    public async ValueTask DrainDeparturesAsync(CancellationToken token)
    {
        while (departures.TryDequeue(out var action))
        {
            try
            {
                await delivery.ProjectDisconnectedActionAsync(action!, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                logger.LogWarning(error,
                    "Boss tower disconnected projection failed for run {RunId}; continuing with queued projections",
                    action?.Run?.RunId);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await GameplayTimerWork.TickAsync(ticks, Command, TickCoreAsync, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (GameplayJob.EndedAJob(error))
            {
                // The executor logged the sweep CMD's failure with its stack (F-30).
                logger.LogWarning("Unexpected boss tower projection sweep failure; the hosted service will continue");
            }
            catch (Exception error)
            {
                logger.LogError(error,
                    "Unexpected boss tower projection sweep failure; the hosted service will continue");
            }
        }
    }

    private async ValueTask TickCoreAsync(CancellationToken token)
    {
        await DrainDeparturesAsync(token).ConfigureAwait(false);
        await delivery.SweepAsync(clock.GetUtcNow(), token).ConfigureAwait(false);
    }
}

public sealed class BossTowerDepartureProjectionSink(BossTowerDepartureProjectionQueue queue)
    : IBossTowerDepartureSink
{
    public ValueTask DepartedAsync(BossTowerActionResult action, CancellationToken token)
    {
        queue.Enqueue(action);
        return ValueTask.CompletedTask;
    }
}
