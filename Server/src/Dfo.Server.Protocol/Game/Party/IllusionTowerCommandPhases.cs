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
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Party;

using Dfo.Server.Protocol.Game;
internal abstract partial class PartyMutationProtocolCommandHandler
{
    protected async ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)>> IllusionTowerProjectActionAsync(
        TowerActionResult action, IGameServerMessage? shared,
        CancellationToken token, SelectedSessionIdentity? requester = null, IGameServerMessage? requesterResponse = null)
    {
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var run = action.Run!;
        foreach (var member in IllusionTowerExactMembers(run))
        {
            try
            {
                var frames = new List<IGameServerMessage>();
                var kill = action.KillRewards.SingleOrDefault(value => value.Session == member);
                if (kill is not null)
                {
                    await IllusionTowerAppendExperienceAsync(frames, member, kill.Award, token).ConfigureAwait(false);
                    IllusionTowerAppendInventory(frames, kill.InventoryUpdates, clock.GetUtcNow());
                    IllusionTowerAppendQuest(frames, kill.QuestProgress);
                }
                if (requesterResponse is not null && member == requester) frames.Add(requesterResponse);
                if (shared is not null) frames.Add(shared);
                if (action.SettlementCreated
                    && run.Completion is { Settled: true, Receipt: { } receipt }
                    && receipt.Members.SingleOrDefault(value => value.Session == member) is { } settlement)
                    await IllusionTowerAppendSettlementAsync(frames, run, settlement, receipt, member, token).ConfigureAwait(false);
                if (frames.Count > 0)
                    await IllusionTowerSendCommittedBatchAsync(run, member,
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

    protected async ValueTask IllusionTowerSendCommittedBatchAsync(IllusionTowerSnapshot run,
        SelectedSessionIdentity member, GameExplicitMessageDelivery batch, CancellationToken token)
    {
        try
        {
            await register(member.ConnectionId, batch, token).ConfigureAwait(false);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            // The first attempt may have sent part of the batch. Only replay these committed
            // projections; never execute the reward again.
            if (!IllusionTowerExact(member, run)
                || !Equals(illusionTowers.Find(member)?.Completion?.Receipt, run.Completion?.Receipt))
                throw;
            await register(member.ConnectionId, batch, token).ConfigureAwait(false);
        }
    }

    protected async ValueTask IllusionTowerAppendSettlementAsync(List<IGameServerMessage> frames,
        IllusionTowerSnapshot run, TowerMemberSettlementReceipt member, TowerSettlementReceipt receipt,
        SelectedSessionIdentity session, CancellationToken token)
    {
        if (member.Award is { } award)
        {
            await IllusionTowerAppendExperienceAsync(frames, session, award, token).ConfigureAwait(false);
            IllusionTowerAppendQuest(frames, member.QuestProgress);
        }
        else
            IllusionTowerAppendQuest(frames, member.QuestProgress);
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
            await illusionTowers.CanContinueAsync(IllusionTowerContext(run, session), token).ConfigureAwait(false)));
    }

    protected async ValueTask IllusionTowerAppendExperienceAsync(List<IGameServerMessage> frames,
        SelectedSessionIdentity session, ExperienceAward award, CancellationToken token)
    {
        var skillPoints = await characters.Get(session.CharacterId)
            .RemainingSkillPointsAsync(token).ConfigureAwait(false);
        if (skillPoints is not null)
            frames.Add(new ExperienceNotification(award.Level, award.NotificationTotal, skillPoints,
                AccountHonorLevel.Initial, ExperienceBonusLines.Of(award)));
    }

    protected static void IllusionTowerAppendInventory(List<IGameServerMessage> frames,
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

    protected static void IllusionTowerAppendQuest(List<IGameServerMessage> frames,
        Dfo.Server.Abstractions.Quests.QuestProgressSnapshot? progress)
    {
        if (progress is null) return;
        frames.Add(new AcceptedQuestProgressMessage(
            [.. progress.Quests.OrderBy(quest => quest.Slot)
                .Select(quest => new AcceptedQuestProgressState(
                    checked((ushort)quest.QuestId), unchecked((uint)quest.TriggerValue)))]));
    }

    protected async ValueTask<List<TownAreaEntry>> IllusionTowerCaptureReturnEntriesAsync(IReadOnlyList<SelectedSessionIdentity> returning,
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
                    await register(member.ConnectionId,
                        new GameExplicitMessageDelivery([response], []), token).ConfigureAwait(false);
                if (restoreInventory)
                {
                    var restored = await currentState.RefreshAsync(runtime, token).ConfigureAwait(false);
                    await register(member.ConnectionId, new GameExplicitMessageDelivery([
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

    protected bool IllusionTowerExact(SelectedSessionIdentity session, IllusionTowerSnapshot expected) =>
        sessions.IsInWorld(session)
        && illusionTowers.Find(session) is { } current
        && current.RunId == expected.RunId && current.Room.Version == expected.Room.Version;

    protected IReadOnlyList<SelectedSessionIdentity> IllusionTowerExactMembers(IllusionTowerSnapshot run) =>
        run.Members.OrderBy(value => value.Slot).Select(value => value.Session)
            .Where(member => sessions.IsInWorld(member)
                && illusionTowers.Find(member) is { } current
                && current.RunId == run.RunId
                && current.Room.Version == run.Room.Version).ToArray();

    protected IllusionTowerSnapshot? IllusionTowerCurrentRun(IllusionTowerSnapshot captured)
    {
        foreach (var member in captured.Members.OrderBy(value => value.Slot))
        {
            if (!sessions.IsInWorld(member.Session))
                continue;
            if (illusionTowers.Find(member.Session) is { } current
                && current.RunId == captured.RunId
                && current.Room.Version == captured.Room.Version)
            {
                return current;
            }
        }
        return null;
    }

    protected async ValueTask<IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)>> IllusionTowerSendToExactAsync(
        IllusionTowerSnapshot run,
        Func<SelectedSessionIdentity, IReadOnlyList<IGameServerMessage>> frames, CancellationToken token)
    {
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        foreach (var member in IllusionTowerExactMembers(run))
        {
            try
            {
                await register(member.ConnectionId,
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

    protected static TowerActionContext IllusionTowerContext(IllusionTowerSnapshot run, SelectedSessionIdentity session) =>
        new(session, run.RunId, run.Room.Version);

    protected async ValueTask IllusionTowerCloseQuietlyAsync(SelectedSessionIdentity session)
    {
        if (!sessions.IsInWorld(session))
            return;
        await close(session).ConfigureAwait(false);
    }

    protected async ValueTask IllusionTowerCloseProjectionFailuresAsync(
        IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures, Guid runId)
    {
        foreach (var (session, error) in failures.DistinctBy(value => value.Session))
        {
            logger.LogWarning(error,
                "Illusion tower post-commit projection failed for exact session {ConnectionId} in run {RunId}",
                session.ConnectionId, runId);
            await IllusionTowerCloseQuietlyAsync(session).ConfigureAwait(false);
        }
    }

}
