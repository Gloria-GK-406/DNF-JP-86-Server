using Dfo.Server.Protocol.Game.BossTowerReward;
using Dfo.Server.Protocol.Game.BossTowerContinue;
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

internal static class BossTowerActionProjection
{
    private static readonly IInventorySlotMappingRuleService SlotMappings = new InventorySlotMappingRuleService();
    internal static async ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)>> BossTowerProjectActionAsync(ISelectedSessionDomainService sessions,ICharacterDomainService characters,IBossTowerDomainService bossTowers,TimeProvider clock,IGameCommandOutbox outbox,        BossTowerActionResult action,IGameServerMessage? shared,        CancellationToken token,SelectedSessionIdentity? requester = null,IGameServerMessage? requesterResponse = null)
    {
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var run = action.Run!;
        foreach (var member in BossTowerActionProjection.BossTowerExactMembers(sessions, bossTowers, run))
        {
            try
            {
                var frames = new List<IGameServerMessage>();
                var kill = action.KillRewards.SingleOrDefault(value => value.Session == member);
                if (kill is not null)
                {
                    await BossTowerActionProjection.BossTowerAppendExperienceAsync(characters, frames, member, kill.Award, token).ConfigureAwait(false);
                    BossTowerActionProjection.BossTowerAppendInventory(frames, kill.InventoryUpdates, clock.GetUtcNow());
                    BossTowerActionProjection.BossTowerAppendQuest(frames, kill.QuestProgress);
                }
                if (requesterResponse is not null && member == requester) frames.Add(requesterResponse);
                if (shared is not null) frames.Add(shared);
                if (action.SettlementCreated
                    && run.Completion is { Settled: true, Receipt: { } receipt }
                    && receipt.Members.SingleOrDefault(value => value.Session == member) is { } settlement)
                    await BossTowerActionProjection.BossTowerAppendSettlementAsync(characters, bossTowers, frames, run, settlement, receipt, member, token).ConfigureAwait(false);
                if (frames.Count > 0)
                    await BossTowerActionProjection.BossTowerSendCommittedBatchAsync(sessions, bossTowers, outbox, run, member,
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

    internal static async ValueTask BossTowerSendCommittedBatchAsync(ISelectedSessionDomainService sessions,IBossTowerDomainService bossTowers,IGameCommandOutbox outbox,BossTowerSnapshot run,        SelectedSessionIdentity member,GameExplicitMessageDelivery batch,CancellationToken token)
    {
        try
        {
            await outbox.RegisterExplicitAsync(member.ConnectionId, batch, token).ConfigureAwait(false);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            // The first attempt may have sent part of the batch. Only replay these committed
            // projections; never execute the reward again.
            if (!BossTowerActionProjection.BossTowerExact(sessions, bossTowers, member, run)
                || !Equals(bossTowers.Find(member)?.Completion?.Receipt, run.Completion?.Receipt))
                throw;
            await outbox.RegisterExplicitAsync(member.ConnectionId, batch, token).ConfigureAwait(false);
        }
    }

    internal static async ValueTask BossTowerAppendSettlementAsync(ICharacterDomainService characters, IBossTowerDomainService bossTowers, List<IGameServerMessage> frames,
        BossTowerSnapshot run, TowerMemberSettlementReceipt member, TowerSettlementReceipt receipt,
        SelectedSessionIdentity session, CancellationToken token)
    {
        if (member.Award is { } award)
        {
            await BossTowerActionProjection.BossTowerAppendExperienceAsync(characters, frames, session, award, token).ConfigureAwait(false);
            BossTowerActionProjection.BossTowerAppendQuest(frames, member.QuestProgress);
        }
        else
            BossTowerActionProjection.BossTowerAppendQuest(frames, member.QuestProgress);
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
            await bossTowers.CanRetryAsync(BossTowerActionProjection.BossTowerContext(run, session), token).ConfigureAwait(false)));
    }

    internal static async ValueTask BossTowerAppendExperienceAsync(ICharacterDomainService characters, List<IGameServerMessage> frames,
        SelectedSessionIdentity session, ExperienceAward award, CancellationToken token)
    {
        var skillPoints = await characters.Get(session.CharacterId)
            .RemainingSkillPointsAsync(token).ConfigureAwait(false);
        if (skillPoints is not null)
            frames.Add(new ExperienceNotification(award.Level, award.NotificationTotal, skillPoints,
                AccountHonorLevel.Initial, ExperienceBonusLines.Of(award)));
    }

    internal static void BossTowerAppendInventory(List<IGameServerMessage> frames,
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

    internal static void BossTowerAppendQuest(List<IGameServerMessage> frames,
        Dfo.Server.Abstractions.Quests.QuestProgressSnapshot? progress)
    {
        if (progress is null) return;
        frames.Add(new AcceptedQuestProgressMessage(
            [.. progress.Quests.OrderBy(quest => quest.Slot)
                .Select(quest => new AcceptedQuestProgressState(
                    checked((ushort)quest.QuestId), unchecked((uint)quest.TriggerValue)))]));
    }

    internal static async ValueTask<List<TownAreaEntry>> BossTowerCaptureReturnEntriesAsync(ISelectedSessionDomainService sessions, ISelectedCharacterCurrentViewDomainService currentViews, ICharacterStateProjectionDomainService currentState, IOnlineCharacterRuntimeDomainService onlineCharacters, IGameCommandOutbox outbox, IReadOnlyList<SelectedSessionIdentity> returning,
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
                    await outbox.RegisterExplicitAsync(member.ConnectionId,
                        new GameExplicitMessageDelivery([response], []), token).ConfigureAwait(false);
                if (restoreInventory)
                {
                    var restored = await currentState.RefreshAsync(runtime, token).ConfigureAwait(false);
                    await outbox.RegisterExplicitAsync(member.ConnectionId, new GameExplicitMessageDelivery([
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
        return entries;
    }

    internal static bool BossTowerExact(ISelectedSessionDomainService sessions, IBossTowerDomainService bossTowers, SelectedSessionIdentity session, BossTowerSnapshot expected) =>
        sessions.IsInWorld(session)
        && bossTowers.Find(session) is { } current
        && current.RunId == expected.RunId && current.Room.Version == expected.Room.Version;

    internal static IReadOnlyList<SelectedSessionIdentity> BossTowerExactMembers(ISelectedSessionDomainService sessions, IBossTowerDomainService bossTowers, BossTowerSnapshot run) =>
        run.Members.OrderBy(value => value.Slot).Select(value => value.Session)
            .Where(member => sessions.IsInWorld(member)
                && bossTowers.Find(member) is { } current
                && current.RunId == run.RunId
                && current.Room.Version == run.Room.Version).ToArray();

    internal static BossTowerSnapshot? BossTowerCurrentRun(ISelectedSessionDomainService sessions, IBossTowerDomainService bossTowers, BossTowerSnapshot captured)
    {
        foreach (var member in captured.Members.OrderBy(value => value.Slot))
        {
            if (!sessions.IsInWorld(member.Session))
                continue;
            if (bossTowers.Find(member.Session) is { } current
                && current.RunId == captured.RunId
                && current.Room.Version == captured.Room.Version)
            {
                return current;
            }
        }
        return null;
    }

    internal static async ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)>> BossTowerSendToExactAsync(ISelectedSessionDomainService sessions, IBossTowerDomainService bossTowers, IGameCommandOutbox outbox, 
        BossTowerSnapshot run,
        IReadOnlyList<IGameServerMessage> frames, CancellationToken token)
    {
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        foreach (var member in BossTowerActionProjection.BossTowerExactMembers(sessions, bossTowers, run))
        {
            try
            {
                await outbox.RegisterExplicitAsync(member.ConnectionId,
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

    internal static SelectedSessionIdentity[] BossTowerMembers(BossTowerSnapshot run) => run.Members.Select(value => value.Session).ToArray();

    internal static TowerActionContext BossTowerContext(BossTowerSnapshot run, SelectedSessionIdentity session) =>
        new(session, run.RunId, run.Room.Version);

    internal static async ValueTask BossTowerCloseQuietlyAsync(ISelectedSessionDomainService sessions, ICommandConnectionClosures closures, ExactSessionClosure targets, SelectedSessionIdentity session)
    {
        await targets.RequestAsync(sessions, closures, session).ConfigureAwait(false);
    }

    internal static async ValueTask BossTowerCloseProjectionFailuresAsync(ISelectedSessionDomainService sessions, ILogger logger, ICommandConnectionClosures closures, ExactSessionClosure targets,
        IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures, Guid runId)
    {
        foreach (var (session, error) in failures.DistinctBy(value => value.Session))
        {
            logger.LogWarning(error,
                "Boss tower post-commit projection failed for exact session {ConnectionId} in run {RunId}",
                session.ConnectionId, runId);
            await BossTowerActionProjection.BossTowerCloseQuietlyAsync(sessions, closures, targets, session).ConfigureAwait(false);
        }
    }
}
