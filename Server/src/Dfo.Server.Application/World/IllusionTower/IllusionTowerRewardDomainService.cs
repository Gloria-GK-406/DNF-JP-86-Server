using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World.IllusionTower;

internal sealed class IllusionTowerRewardDomainService(IIllusionTowerRewardRuleService rules,
    ICharacterDomainService characters,
    IItemGrantDomainService grants, ICharacterContainerDomainService containers,
    IQuestProgressDomainService quests, IQuestEnemyDropDomainService questDrops,
    ICharacterQuestLogDomainService questLogs,
    IDomainTransactionAccessor transactions, TimeProvider clock)
    : IIllusionTowerRewardDomainService
{
    private void RequireTransaction()
    {
        if (transactions.Current is null) throw new InvalidOperationException("Tower awards require the owner's transaction.");
    }
    public async ValueTask<ImmutableArray<TowerKillReward>> KillAsync(IllusionTowerSnapshot run,
        TowerActor actor, CancellationToken token)
    {
        RequireTransaction();
        var rewards = ImmutableArray.CreateBuilder<TowerKillReward>();
        foreach (var member in run.Members)
        {
            var session = member.Session;
            var award = await characters.Get(session.CharacterId).AddExpAsync(new TowerKillExperienceContext(
                new(ExperienceEvent.TowerKill, session.AccountId, session.CharacterId, run.DungeonId), actor.Seed.ActorType),
                clock.GetUtcNow(), session, token).ConfigureAwait(false)
                ?? throw new InvalidDataException("Tower member has no progression.");
            var request = new QuestEnemyDropRequest(
                actor.Seed.ActorType is >= 5 and <= 8 ? QuestEnemyType.APC : QuestEnemyType.Monster,
                actor.Seed.TemplateId, actor.Uid, run.DungeonId, DungeonDifficulty.Normal,
                unchecked((int)run.Room.Seed), run.Room.Level, 0, run.Room.MapId);
            var pending = await questDrops.DrawAsync(request,
                new(session.AccountId, session.CharacterId), token).ConfigureAwait(false);
            var updates = await GrantQuestItemsAsync(session, pending, token).ConfigureAwait(false);
            var facts = new List<QuestProgressFact>
            {
                new QuestEnemyDefeated(run.DungeonId, DungeonDifficulty.Normal, actor.Seed.TemplateId,
                    actor.Seed.ActorType is >= 5 and <= 8 ? HuntEnemyTargetTypes.AiCharacter : HuntEnemyTargetTypes.Monster,
                    MonsterRank.Ordinary),
            };
            if (!updates.IsEmpty) facts.Add(new QuestHoldingsChanged());
            var report = await quests.ReportAsync(session.AccountId, session.CharacterId,
                facts, token).ConfigureAwait(false);
            rewards.Add(new(session, member.Slot, award, report.NotificationSnapshot)
                { InventoryUpdates = updates });
        }
        return rewards.ToImmutable();
    }

    private async ValueTask<ImmutableArray<TowerInventoryUpdate>> GrantQuestItemsAsync(
        SelectedSessionIdentity session, IReadOnlyList<RolledDrop> pending, CancellationToken token)
    {
        var changed = new Dictionary<BusinessSlot, TowerInventoryUpdate>();
        var active = (await questLogs.LogOf(session.AccountId, session.CharacterId)
                .CarriedAsync(token).ConfigureAwait(false))
            .Select(quest => quest.QuestId).ToHashSet();
        foreach (var drop in pending.Where(value => value.Delivery == RolledDropDelivery.DirectQuestItem
            && value.OwnerCharacterId == session.CharacterId && value.ItemId > 0 && value.Count > 0))
        {
            if (drop.QuestId <= 0 || !active.Contains(drop.QuestId) || drop.GrantLimit <= 0) continue;
            var source = AccountMaterialCatalog.ContainsItem(drop.ItemId)
                ? containers.FindContainer(session.CharacterId, InventoryContainerType.SharedMaterial, isDefault: true)
                    ?? throw new InvalidDataException("Shared material container is unavailable.")
                : containers.FindContainer(session.CharacterId, InventoryContainerType.Main, isDefault: true)
                    ?? throw new InvalidDataException("Character inventory is unavailable.");
            var held = (await source.GetAllAsync(token).ConfigureAwait(false))
                .Where(item => item.ItemId == drop.ItemId).Sum(item => (long)item.Count);
            var remaining = checked((int)Math.Min(drop.Count, Math.Max(0, drop.GrantLimit - held)));
            while (remaining > 0)
            {
                var granted = await grants.GrantAsync(session.AccountId, session.CharacterId, containers,
                    drop.ItemId, remaining, token, isDefault: true).ConfigureAwait(false);
                var placement = granted.Placement;
                var localSlot = placement switch
                {
                    ItemPlacementOutcome.AddedToPile value when value.Added > 0 => value.SlotIndex,
                    ItemPlacementOutcome.PutInEmptySlot value when value.Count > 0 => value.SlotIndex,
                    ItemPlacementOutcome.NoRoom => -1,
                    ItemPlacementOutcome.UnknownItem => throw new InvalidDataException(
                        $"Tower quest item {drop.ItemId} has no inventory definition."),
                    _ => throw new InvalidDataException(
                        $"Tower quest item {drop.ItemId} placement made no progress: {placement}."),
                };
                if (localSlot < 0) break;
                remaining -= placement switch
                {
                    ItemPlacementOutcome.AddedToPile value => value.Added,
                    ItemPlacementOutcome.PutInEmptySlot value => value.Count,
                    _ => 0,
                };
                var item = await granted.Container.GetAsync(localSlot, token).ConfigureAwait(false)
                    ?? throw new InvalidDataException("Tower quest item disappeared before commit.");
                var slot = new BusinessSlot(granted.Container.Space, localSlot);
                changed[slot] = new(slot, item.Snapshot);
            }
        }
        return changed.OrderBy(value => value.Key.Space).ThenBy(value => value.Key.Index)
            .Select(value => value.Value).ToImmutableArray();
    }
    public async ValueTask<TowerSettlementReceipt> SettleAsync(IllusionTowerSnapshot run, ulong seed, CancellationToken token)
    {
        RequireTransaction();
        var completion = run.Completion ?? throw new InvalidOperationException("Tower has not ended.");
        var receipts = ImmutableArray.CreateBuilder<TowerMemberSettlementReceipt>();
        foreach (var member in run.Members)
        {
            var session = member.Session;
            var character = characters.Get(session.CharacterId);
            var progress = await character.ProgressAsync(token).ConfigureAwait(false)
                ?? throw new InvalidDataException("Tower member has no progression.");
            var candidates = (await rules.CandidatesAsync(progress.Level, completion.RewardLevel,
                DropRandom.SeedFor(unchecked((int)seed), run.DungeonId, completion.RewardLevel, member.Slot, 0), token, run.DungeonId)
                .ConfigureAwait(false)).ToBuilder();
            var beforeGold = await character.GoldAsync(token).ConfigureAwait(false);
            var goldBalance = beforeGold;
            var delivered = ImmutableArray.CreateBuilder<TowerRewardItem>();
            var updates = ImmutableArray.CreateBuilder<TowerInventoryUpdate>();
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate.ItemId == 0)
                {
                    var previousGold = goldBalance;
                    goldBalance = await character.AddGoldAsync(candidate.DisplayValue, token).ConfigureAwait(false);
                    candidates[i] = candidate with { Delivered = goldBalance > previousGold };
                    continue;
                }
                if (candidate.ItemId < 0) continue;
                var grant = await grants.GrantAsync(session.AccountId, session.CharacterId, containers,
                    candidate.ItemId, 1, token, isDefault: true).ConfigureAwait(false);
                if (grant.Placement is ItemPlacementOutcome.NoRoom) continue;
                if (grant.Placement is not ItemPlacementOutcome.PutInEmptySlot { Count: 1 } placed)
                    throw new InvalidDataException($"Tower equipment grant failed: {grant.Placement}.");
                var item = await grant.Container.GetAsync(placed.SlotIndex, token).ConfigureAwait(false)
                    ?? throw new InvalidDataException("Tower equipment disappeared.");
                if (item.Snapshot.Durability != candidate.Durability)
                    item = await grant.Container.UpdateAsync(item, x => x with { Durability = candidate.Durability }, token).ConfigureAwait(false);
                await grant.Container.FlushAsync(token).ConfigureAwait(false);
                candidates[i] = candidate with { DisplayValue = item.Snapshot.InstanceValue, Delivered = true };
                delivered.Add(new(candidate.ItemId, 1));
                updates.Add(new(new(grant.Container.Space, placed.SlotIndex), item.Snapshot));
            }
            var award = await characters.Get(session.CharacterId).AddExpAsync(new TowerClearExperienceContext(
                new(ExperienceEvent.TowerClear, session.AccountId, session.CharacterId, run.DungeonId), completion.RewardLevel),
                clock.GetUtcNow(), session, token).ConfigureAwait(false)
                ?? throw new InvalidDataException("Tower member has no progression.");
            var facts = new List<QuestProgressFact>();
            if (completion.Reason == TowerEndReason.Completed) facts.Add(new QuestDungeonCleared(run.DungeonId, DungeonDifficulty.Normal));
            if (goldBalance != beforeGold || delivered.Count > 0) facts.Add(new QuestHoldingsChanged());
            var report = await quests.ReportAsync(session.AccountId, session.CharacterId, facts, token).ConfigureAwait(false);
            receipts.Add(new(session.CharacterId, award.Gained, goldBalance - beforeGold, delivered.ToImmutable())
            {
                Slot = member.Slot, Session = session, Award = award, GoldBalance = goldBalance,
                Candidates = candidates.ToImmutable(), InventoryUpdates = updates.ToImmutable(), QuestProgress = report.NotificationSnapshot
            });
        }
        return new(receipts.ToImmutable());
    }
}
