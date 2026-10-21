using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
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
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game;

public sealed class IllusionTowerProjectionGate : UserExecutionGate<Guid>;
public sealed class IllusionTowerEntryGate : UserExecutionGate<CharacterId>;

public sealed class IllusionTowerProjectionDelivery(
    IIllusionTowerDomainService towers,
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
    SelectedSessionTransitionGate transitions,
    IllusionTowerProjectionGate projectionGate,
    IllusionTowerEntryGate entryGate,
    TimeProvider clock,
    INetworkConnectionCloser connections,
    ILogger<IllusionTowerProjectionDelivery> logger,
    Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService? housing = null)
{
    private static readonly IInventorySlotMappingRuleService SlotMappings = new InventorySlotMappingRuleService();

    public async ValueTask<bool> EnterAsync(
        NetworkConnectionId requesterConnection,
        SelectedSessionIdentity requester,
        PartySnapshot roster,
        DungeonEntryIntent intent,
        Action restoreIntent,
        CancellationToken token, int dungeonId = 11001)
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
            await using (await entryGate.AcquireAsync(requester.CharacterId, UserAccessMode.Exclusive, token).ConfigureAwait(false))
            await using (await AcquireTransitionsAsync(attachments.Select(value => value.Session), token).ConfigureAwait(false))
            {
                if (!Current(attachments) || !PartyCurrent(requester, roster, attachments))
                {
                    restoreIntent();
                    await RefuseAsync(requesterConnection, SelectDungeonRefusal.SelectionCohortUnavailable, token).ConfigureAwait(false);
                    return true;
                }

                var entered = await commands.SendAsync(new EnterIllusionTowerCommand(
                    attachments.Select(value => new TowerEntryMember(value.Session, value.Slot)).ToArray(),
                    requester, unchecked((uint)Random.Shared.Next()), dungeonId), token).ConfigureAwait(false);
                if (entered.Run is null)
                {
                    restoreIntent();
                    await RefuseAsync(requesterConnection,
                        entered.Rejection switch
                        {
                            "LevelTooLow" => SelectDungeonRefusal.LevelTooLow,
                            "InsufficientMaterial" => SelectDungeonRefusal.MissingRequiredItem,
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
                    if (sessions.FindByCharacter(member.Session.CharacterId) == member.Session)
                        entryIntents.Consume(member.Session);
                }

                var refreshed = new Dictionary<CharacterId, SelectCharacterResult>();
                foreach (var member in attachments.OrderBy(value => value.Slot))
                {
                    // The attachment was loaded before admission. Attribute refresh alone keeps
                    // its inventory snapshot, so reread after the temporary bindings commit.
                    var runtime = run.DungeonId == 11000
                        ? await currentViews.TryLoadAsync(member.Session, token).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Tower member view disappeared after admission.")
                        : member.Runtime;
                    refreshed[member.Session.CharacterId] = await currentState.RefreshAsync(runtime, token).ConfigureAwait(false);
                }

                var ordered = attachments.OrderBy(value => value.Slot).ToArray();
                var wireUsers = ordered.Select(value => value.Session.WireUserId).ToImmutableArray();
                var hostSlot = ordered.Single(value => value.Session == requester).Slot;
                var debits = entered.Debits.ToDictionary(value => value.Session);
                var batches = new List<(SelectedSessionIdentity Session, IReadOnlyList<IGameServerMessage> Frames)>();
                foreach (var receiver in ordered)
                {
                    var frames = new List<IGameServerMessage>();
                    // A21 recalculates player attributes on DELETE_ITEM. Deliver the committed
                    // debit before 008E/008F switch it into tower mode, whose table can still be null.
                    if (debits.TryGetValue(receiver.Session, out var debit))
                        foreach (var item in debit.Debits)
                            AppendDebit(frames, item, clock.GetUtcNow());
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
                    frames.Add(new IllusionTowerInfoNotification(run.DungeonId, run.TotalLevels));
                    frames.Add(new IllusionTowerMapNotification(run.Room));
                    batches.Add((receiver.Session, frames));
                }

                foreach (var batch in batches.OrderBy(value => value.Session == requester ? 1 : 0))
                    await sender.SendExplicitAsync(batch.Session.ConnectionId,
                        new GameExplicitMessageDelivery(batch.Frames, []), token).ConfigureAwait(false);
                await townProjection.LeaveManyAsync(ordered.Select(value => value.Session).ToArray(),
                    TownAreaDepartureReason.DungeonEntry, token).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (committed)
        {
            deliveryFailure = error;
        }

        if (deliveryFailure is not null)
        {
            logger.LogWarning(deliveryFailure,
                "Illusion tower entry projection failed after commit; isolating {Count} exact sessions", isolate.Count);
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
        await using (await projectionGate.AcquireAsync(
            expected.RunId, UserAccessMode.Exclusive, token).ConfigureAwait(false))
        await using (await AcquireTransitionsAsync(
            expected.Members.Select(member => member.Session), token).ConfigureAwait(false))
        {
            if (!Exact(session, expected)) return true;
            var action = await commands.SendAsync(
                new LoadIllusionTowerCommand(Context(expected, session)), token).ConfigureAwait(false);
            if (action.Run is not { Room.Phase: TowerRoomPhase.Ready } run) return true;
            failures = await SendToExactAsync(
                run, _ => [new LoadingCompleteNotification()], token).ConfigureAwait(false);
        }
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
        await using var projection = await projectionGate.AcquireAsync(
            expected.RunId, UserAccessMode.Exclusive, token).ConfigureAwait(false);
        await using var transition = await AcquireTransitionsAsync(
            expected.Members.Select(member => member.Session), token).ConfigureAwait(false);
        if (Exact(session, expected))
            _ = await commands.SendAsync(
                new StartIllusionTowerCommand(Context(expected, session)), token).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> AdvanceAsync(NetworkConnectionId connectionId,
        GameMessageMetadata metadata, CancellationToken token)
    {
        if (Stale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : towers.Find(session);
        if (session is null || expected is null) return false;
        IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures = [];
        await using (await projectionGate.AcquireAsync(
            expected.RunId, UserAccessMode.Exclusive, token).ConfigureAwait(false))
        await using (await AcquireTransitionsAsync(
            expected.Members.Select(member => member.Session), token).ConfigureAwait(false))
        {
            if (!Exact(session, expected)) return true;
            var action = await commands.SendAsync(
                new AdvanceIllusionTowerCommand(Context(expected, session)), token).ConfigureAwait(false);
            if (action.Changed && action.Run is { } run)
                failures = await SendToExactAsync(
                    run, _ => [new IllusionTowerMapNotification(run.Room)], token).ConfigureAwait(false);
        }
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
        await using (await projectionGate.AcquireAsync(
            expected.RunId, UserAccessMode.Exclusive, token).ConfigureAwait(false))
        await using (await AcquireTransitionsAsync(
            expected.Members.Select(member => member.Session), token).ConfigureAwait(false))
        {
            if (!Exact(session, expected)) return true;
            var action = await commands.SendAsync(
                new KillIllusionTowerActorCommand(Context(expected, session), actorUid), token).ConfigureAwait(false);
            if (!action.Changed || action.Run is null) return true;
            failures = await ProjectActionAsync(
                action, new MonsterDeathNotification(actorUid, action.Drops.Select(item => new MonsterDrop(item.Uid,
                    new RolledDrop(item.ItemId, 1, DropRarity.Common, DropItemKind.Stackable))).ToArray(), killerUid), token).ConfigureAwait(false);
        }
        await CloseProjectionFailuresAsync(failures, expected.RunId).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> PickupAsync(NetworkConnectionId connectionId,
        GameMessageMetadata metadata, ushort groundUid, CancellationToken token)
    {
        if (Stale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : towers.Find(session);
        if (session is null || expected is null) return false;
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        await using (await projectionGate.AcquireAsync(expected.RunId, UserAccessMode.Exclusive, token).ConfigureAwait(false))
        await using (await AcquireTransitionsAsync(expected.Members.Select(x => x.Session), token).ConfigureAwait(false))
        {
            if (!Exact(session, expected)) return true;
            var picked = await commands.SendAsync(new PickupTowerItemCommand(Context(expected, session), groundUid), token).ConfigureAwait(false);
            if (!picked.Action.Changed || picked.Action.Run is not { } run || picked.Update is not { } update)
            {
                if (picked.NoRoom) await sender.SendExplicitAsync(connectionId,
                    new GameExplicitMessageDelivery([new GetItemResponse(false)], []), token).ConfigureAwait(false);
                return true;
            }
            foreach (var member in ExactMembers(run))
            {
                try
                {
                    var frames = new List<IGameServerMessage>();
                    if (member == session)
                    {
                        frames.Add(new GetItemResponse(true));
                    }
                    frames.Add(new GroundPickupNotification(groundUid, session.WireUserId,
                        checked((ushort)SlotMappings.ToGameSlot(update.Slot).Index)));
                    if (member == session) AppendInventory(frames, [update], clock.GetUtcNow());
                    await SendCommittedBatchAsync(run, member, new GameExplicitMessageDelivery(frames, []), token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
                { failures.Add((member, error)); }
            }
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
        await using (await projectionGate.AcquireAsync(
            expected.RunId, UserAccessMode.Exclusive, token).ConfigureAwait(false))
        await using (await AcquireTransitionsAsync(
            expected.Members.Select(member => member.Session), token).ConfigureAwait(false))
        {
            if (!Exact(session, expected)) return true;
            var action = await commands.SendAsync(
                new DieInIllusionTowerCommand(Context(expected, session)), token).ConfigureAwait(false);
            if (!action.Changed || action.Run is null) return true;
            failures = await ProjectActionAsync(
                action, new CharacterLifeStateNotification(session.WireUserId), token,
                requester: session, requesterResponse: new CharacterDeathResponse()).ConfigureAwait(false);
        }
        await CloseProjectionFailuresAsync(failures, expected.RunId).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> SelectReturnAsync(NetworkConnectionId connectionId,
        GameMessageMetadata metadata, byte state, byte option, CancellationToken token)
    {
        if (Stale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : towers.Find(session);
        if (session is null || expected is null) return false;
        if (state != EplpCommandHandler.LeaveState) return true;
        Exception? failure = null;
        await using (await projectionGate.AcquireAsync(
            expected.RunId, UserAccessMode.Exclusive, token).ConfigureAwait(false))
        await using (await AcquireTransitionsAsync(
            expected.Members.Select(member => member.Session), token).ConfigureAwait(false))
        {
            if (!Exact(session, expected)) return true;
            var action = await commands.SendAsync(
                new SelectIllusionTowerReturnCommand(Context(expected, session), option), token).ConfigureAwait(false);
            if (action.Changed)
            {
                try
                {
                    await sender.SendExplicitAsync(session.ConnectionId,
                        new GameExplicitMessageDelivery([new EplpCommandResponse(state, option)], []), token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error)
                {
                    failure = error;
                }
            }
        }
        if (failure is not null)
            await CloseProjectionFailuresAsync([(session, failure)], expected.RunId).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<bool> LeaveAndReturnAsync(NetworkConnectionId connectionId,
        GameMessageMetadata metadata, IGameServerMessage? response, CancellationToken token)
    {
        if (Stale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : towers.Find(session);
        if (session is null || expected is null) return false;
        TowerActionResult? committed = null;
        await using (await projectionGate.AcquireAsync(
            expected.RunId, UserAccessMode.Exclusive, token).ConfigureAwait(false))
        await using (await AcquireTransitionsAsync(
            expected.Members.Select(member => member.Session), token).ConfigureAwait(false))
        {
            if (!Exact(session, expected)) return true;
            var action = await commands.SendAsync(
                new LeaveIllusionTowerCommand(Context(expected, session), Disconnected: false), token).ConfigureAwait(false);
            if (action.Changed)
                committed = action;
        }
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
                    "Illusion tower return sweep failed for run {RunId}; continuing with other due runs",
                    context.RunId);
                continue;
            }

            // Transport close may synchronously enter selected-session cleanup. The run and all
            // account transition gates acquired by SweepOneAsync have been released at this point.
            foreach (var (session, error) in failures)
            {
                logger.LogWarning(error,
                    "Illusion tower return projection failed for exact session {ConnectionId} in run {RunId}",
                    session.ConnectionId, context.RunId);
                await CloseQuietlyAsync(session).ConfigureAwait(false);
            }
        }
    }

    public ValueTask ProjectDisconnectedActionAsync(TowerActionResult action, CancellationToken token) =>
        ProjectCommittedDepartureAsync(action, null, null, token);

    public async ValueTask ProjectCommittedDepartureAsync(TowerActionResult action,
        SelectedSessionIdentity? returning, IGameServerMessage? response, CancellationToken token)
    {
        if (!action.Changed || action.Run is null) return;
        var captured = action.Run;
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        await using (await projectionGate.AcquireAsync(
            captured.RunId, UserAccessMode.Exclusive, token).ConfigureAwait(false))
        {
            var participants = captured.Members.Select(member => member.Session)
                .Append(returning).OfType<SelectedSessionIdentity>().Distinct().ToArray();
            await using (await AcquireTransitionsAsync(participants, token).ConfigureAwait(false))
            {
                var exact = ExactMembers(captured);
                var current = CurrentRun(captured);
                if (exact.Count > 0 && current?.Members.SingleOrDefault(member => member.IsLeader) is { } leader)
                {
                    failures.AddRange(await SendToExactAsync(
                        current, _ => [new UdpHostNotification(leader.Slot)], token).ConfigureAwait(false));
                }
                if (exact.Count > 0 && captured.Room.Phase == TowerRoomPhase.Ready)
                    failures.AddRange(await SendToExactAsync(
                        captured, _ => [new LoadingCompleteNotification()], token).ConfigureAwait(false));
                if (exact.Count > 0 && action.SettlementCreated && captured.Completion is { Settled: true })
                    failures.AddRange(await ProjectActionAsync(action, null, token).ConfigureAwait(false));
                if (returning is not null
                    && sessions.FindByCharacter(returning.CharacterId) == returning
                    && !towers.IsInTower(returning.CharacterId))
                    await ReturnMembersIsolatedAsync([returning], response, failures, token, captured.DungeonId == 11000).ConfigureAwait(false);
            }
        }
        await CloseProjectionFailuresAsync(failures, captured.RunId).ConfigureAwait(false);
    }

    public async ValueTask ProjectCurrentHostAsync(
        IllusionTowerSnapshot captured, CancellationToken token)
    {
        if (captured.Members.IsEmpty)
            return;
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        await using (await projectionGate.AcquireAsync(
            captured.RunId, UserAccessMode.Exclusive, token).ConfigureAwait(false))
        await using (await AcquireTransitionsAsync(
            captured.Members.Select(member => member.Session), token).ConfigureAwait(false))
        {
            var current = CurrentRun(captured);
            if (current?.Members.SingleOrDefault(member => member.IsLeader) is { } leader)
            {
                failures.AddRange(await SendToExactAsync(
                    current, _ => [new UdpHostNotification(leader.Slot)], token).ConfigureAwait(false));
            }
        }
        await CloseProjectionFailuresAsync(failures, captured.RunId).ConfigureAwait(false);
    }

    public async ValueTask ReprojectCompletionAsync(TowerActionContext context, CancellationToken token)
    {
        var expected = towers.Find(context.Session);
        if (expected is null || expected.RunId != context.RunId || expected.Room.Version != context.RoomVersion
            || expected.Completion is not { Settled: true, Receipt: not null }) return;
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        await using (await projectionGate.AcquireAsync(
            expected.RunId, UserAccessMode.Exclusive, token).ConfigureAwait(false))
        await using (await AcquireTransitionsAsync(
            expected.Members.Select(member => member.Session), token).ConfigureAwait(false))
        {
            var current = towers.Find(context.Session);
            if (current is null || current.RunId != context.RunId || current.Room.Version != context.RoomVersion
                || current.Completion is not { Settled: true, Receipt: not null }) return;
            failures.AddRange(await ProjectActionAsync(
                new TowerActionResult(false, current) { SettlementCreated = true }, null, token)
                .ConfigureAwait(false));
        }
        await CloseProjectionFailuresAsync(failures, expected.RunId).ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)>> SweepOneAsync(
        TowerActionContext context, DateTimeOffset now, CancellationToken token)
    {
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var expected = towers.Find(context.Session);
        if (expected is null || expected.RunId != context.RunId || expected.Room.Version != context.RoomVersion
            || expected.ReturnDueAt is not { } due || due > now) return failures;
        await using var projection = await projectionGate.AcquireAsync(
            expected.RunId, UserAccessMode.Exclusive, token).ConfigureAwait(false);
        var frozen = ExactMembers(expected);
        if (frozen.Count == 0) return failures;
        await using var transition = await AcquireTransitionsAsync(frozen, token).ConfigureAwait(false);
        var current = towers.Find(context.Session);
        if (current is null || current.RunId != expected.RunId || current.Room.Version != expected.Room.Version
            || current.ReturnDueAt is not { } currentDue || currentDue > now) return failures;
        foreach (var member in frozen)
            _ = await commands.SendAsync(
                new LeaveIllusionTowerCommand(Context(current, member), Disconnected: false), token).ConfigureAwait(false);
        IGameServerMessage? response = current.ReturnOption is { } option
            ? new EplpCommandResponse(EplpCommandHandler.LeaveState, option)
            : null;
        await ReturnMembersIsolatedAsync(frozen, response, failures, token, current.DungeonId == 11000).ConfigureAwait(false);
        return failures;
    }

    private async ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)>> ProjectActionAsync(
        TowerActionResult action, IGameServerMessage? shared,
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

    private async ValueTask SendCommittedBatchAsync(IllusionTowerSnapshot run,
        SelectedSessionIdentity member, GameExplicitMessageDelivery batch, CancellationToken token)
    {
        try
        {
            await sender.SendExplicitAsync(member.ConnectionId, batch, token).ConfigureAwait(false);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            // The first attempt may have sent part of the batch. Only replay these committed
            // projections, under the caller's run/account gates; never execute the reward again.
            if (!Exact(member, run)
                || !Equals(towers.Find(member)?.Completion?.Receipt, run.Completion?.Receipt))
                throw;
            await sender.SendExplicitAsync(member.ConnectionId, batch, token).ConfigureAwait(false);
        }
    }

    private async ValueTask AppendSettlementAsync(List<IGameServerMessage> frames,
        IllusionTowerSnapshot run, TowerMemberSettlementReceipt member, TowerSettlementReceipt receipt,
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
        var elapsed = (int)Math.Clamp((run.Completion!.CompletedAt - run.StartedAt).TotalMilliseconds, 0, int.MaxValue);
        frames.Add(new IllusionTowerRankingNotification(run.DungeonId, run.Completion.CompletedLevels, elapsed));
        frames.Add(new IllusionTowerRewardNotification(member.Experience,
            receipt.Members.Where(value => value.Slot < 4)
                .ToDictionary(value => value.Slot, value => value.Candidates)));
        frames.Add(new IllusionTowerContinueNotification(
            await towers.CanContinueAsync(Context(run, session), token).ConfigureAwait(false)));
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
            if (sessions.FindByCharacter(member.CharacterId) != member) continue;
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
            var current = sessions.FindByCharacter(member.CharacterId);
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
        IllusionTowerSnapshot? run = null) => members.All(value =>
        sessions.FindByCharacter(value.Session.CharacterId) == value.Session
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

    private bool Exact(SelectedSessionIdentity session, IllusionTowerSnapshot expected) =>
        sessions.FindByConnection(session.ConnectionId) == session
        && sessions.FindByCharacter(session.CharacterId) == session
        && towers.Find(session) is { } current
        && current.RunId == expected.RunId && current.Room.Version == expected.Room.Version;

    private IReadOnlyList<SelectedSessionIdentity> ExactMembers(IllusionTowerSnapshot run) =>
        run.Members.OrderBy(value => value.Slot).Select(value => value.Session)
            .Where(member => sessions.FindByCharacter(member.CharacterId) == member
                && towers.Find(member) is { } current
                && current.RunId == run.RunId
                && current.Room.Version == run.Room.Version).ToArray();

    private IllusionTowerSnapshot? CurrentRun(IllusionTowerSnapshot captured)
    {
        foreach (var member in captured.Members.OrderBy(value => value.Slot))
        {
            if (sessions.FindByCharacter(member.Session.CharacterId) != member.Session)
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
        IllusionTowerSnapshot run,
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

    private static TowerActionContext Context(IllusionTowerSnapshot run, SelectedSessionIdentity session) =>
        new(session, run.RunId, run.Room.Version);

    public static bool Stale(GameMessageMetadata metadata) =>
        metadata.InboundOrdinal is > 0 && !metadata.SequenceIsFresh;

    private static void AppendDebit(List<IGameServerMessage> frames, MaterialDebit debit, DateTimeOffset at)
    {
        var slot = SlotMappings.ToGameSlot(debit.Cost.Slot);
        frames.Add(new DeleteItemResponse(slot, debit.Cost.Count, Success: true));
        frames.Add(debit.Remaining is null
            ? GameInventoryDelivery.ClearedSlotNotification(slot.Space, slot.Index, at)
            : GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(slot.Space,
                [new InventoryItemState(null, slot.Index, debit.Remaining)], at)));
    }

    private ValueTask RefuseAsync(NetworkConnectionId connection, byte code, CancellationToken token, byte context = 0) =>
        sender.SendExplicitAsync(connection,
            new GameExplicitMessageDelivery([new SelectDungeonRefusal(code, context)], []), token);

    private async ValueTask<IAsyncDisposable> AcquireTransitionsAsync(
        IEnumerable<SelectedSessionIdentity> members, CancellationToken token)
    {
        var leases = new List<IAsyncDisposable>();
        try
        {
            foreach (var account in members.Select(value => value.AccountId).Distinct().OrderBy(value => value.Value))
                leases.Add(await transitions.AcquireAsync(account, UserAccessMode.Exclusive, token).ConfigureAwait(false));
            return new LeaseSet(leases);
        }
        catch
        {
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask CloseQuietlyAsync(SelectedSessionIdentity session)
    {
        if (sessions.FindByCharacter(session.CharacterId) != session)
            return;
        try { await connections.RequestCloseAsync(session.ConnectionId, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception error) { logger.LogWarning(error, "Failed to close tower session {ConnectionId}", session.ConnectionId); }
    }

    private async ValueTask CloseProjectionFailuresAsync(
        IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures, Guid runId)
    {
        foreach (var (session, error) in failures.DistinctBy(value => value.Session))
        {
            logger.LogWarning(error,
                "Illusion tower post-commit projection failed for exact session {ConnectionId} in run {RunId}",
                session.ConnectionId, runId);
            await CloseQuietlyAsync(session).ConfigureAwait(false);
        }
    }

    private sealed class LeaseSet(IReadOnlyList<IAsyncDisposable> leases) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
        }
    }
}

public sealed class IllusionTowerDepartureProjectionQueue
{
    private readonly ConcurrentQueue<TowerActionResult> pending = new();

    public void Enqueue(TowerActionResult action) => pending.Enqueue(action);

    internal bool TryDequeue(out TowerActionResult? action) => pending.TryDequeue(out action);
}

public sealed class IllusionTowerReturnSweepService(
    IllusionTowerProjectionDelivery delivery,
    IllusionTowerDepartureProjectionQueue departures,
    TimeProvider clock,
    ILogger<IllusionTowerReturnSweepService> logger) : BackgroundService
{
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
                    "Illusion tower disconnected projection failed for run {RunId}; continuing with queued projections",
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
                await DrainDeparturesAsync(stoppingToken).ConfigureAwait(false);
                await delivery.SweepAsync(clock.GetUtcNow(), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                logger.LogError(error,
                    "Unexpected illusion tower projection sweep failure; the hosted service will continue");
            }
        }
    }
}

public sealed class IllusionTowerDepartureProjectionSink(IllusionTowerDepartureProjectionQueue queue)
    : IIllusionTowerDepartureSink
{
    public ValueTask DepartedAsync(TowerActionResult action, CancellationToken token)
    {
        queue.Enqueue(action);
        return ValueTask.CompletedTask;
    }
}
