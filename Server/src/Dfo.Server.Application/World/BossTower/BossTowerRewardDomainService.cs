using Dfo.Server.Abstractions.Effects;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World.BossTower;

internal sealed class BossTowerRewardDomainService(IBossTowerRewardRuleService rules,
    ICharacterDomainService characters,
    IItemDeliveryDomainService delivery,
    IQuestProgressDomainService quests, IQuestEnemyDropDomainService questDrops,
    IQuestItemDropDomainService questItems,
    IDomainTransactionAccessor transactions, TimeProvider clock)
    : IBossTowerRewardDomainService
{
    private void RequireTransaction()
    {
        if (transactions.Current is null) throw new InvalidOperationException("Tower awards require the owner's transaction.");
    }
    public async ValueTask<ImmutableArray<TowerKillReward>> KillAsync(BossTowerSnapshot run,
        TowerActor actor, IEffectFootprintRecorder footprint, CancellationToken token)
    {
        RequireTransaction();
        var rewards = ImmutableArray.CreateBuilder<TowerKillReward>();
        foreach (var member in run.Members)
        {
            var session = member.Session;
            footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
            var award = await characters.Get(session.CharacterId).AddExpAsync(new TowerKillExperienceContext(
                new(ExperienceEvent.TowerKill, session.AccountId, session.CharacterId, run.DungeonId), DungeonParticipationMode.BossTower, actor.Seed.ActorType),
                clock.GetUtcNow(), session, footprint, token).ConfigureAwait(false)
                ?? throw new InvalidDataException("Tower member has no progression.");
            var request = new QuestEnemyDropRequest(
                actor.Seed.ActorType is >= 5 and <= 8 ? QuestEnemyType.APC : QuestEnemyType.Monster,
                actor.Seed.TemplateId, actor.Uid, run.DungeonId, DungeonDifficulty.Normal,
                unchecked((int)run.Room.Seed), run.Room.Level, 0, run.Room.MapId);
            var pending = await questDrops.DrawAsync(request,
                new(session.AccountId, session.CharacterId), token).ConfigureAwait(false);
            footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
            var updates = await GrantQuestItemsAsync(session, pending, footprint, token).ConfigureAwait(false);
            var facts = new List<QuestProgressFact>
            {
                new QuestEnemyDefeated(run.DungeonId, DungeonDifficulty.Normal, actor.Seed.TemplateId,
                    actor.Seed.ActorType is >= 5 and <= 8 ? HuntEnemyTargetTypes.AiCharacter : HuntEnemyTargetTypes.Monster,
                    actor.Seed.ActorType <= 3 ? (MonsterRank)actor.Seed.ActorType : MonsterRank.Ordinary),
            };
            if (!updates.IsEmpty) facts.Add(new QuestHoldingsChanged());
            footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
            var report = await quests.ReportAsync(session.AccountId, session.CharacterId,
                facts, token).ConfigureAwait(false);
            rewards.Add(new(session, member.Slot, award, report.NotificationSnapshot)
                { InventoryUpdates = updates });
        }
        return rewards.ToImmutable();
    }

    private async ValueTask<ImmutableArray<TowerInventoryUpdate>> GrantQuestItemsAsync(
        SelectedSessionIdentity session, IReadOnlyList<RolledDrop> pending, IEffectFootprintRecorder footprint, CancellationToken token)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        var changed = new Dictionary<BusinessSlot, TowerInventoryUpdate>();
        foreach (var item in await questItems.GrantAsync(session.AccountId, session.CharacterId, pending, token).ConfigureAwait(false))
            changed[item.Slot] = new(item.Slot, item.Snapshot);
        return changed.OrderBy(value => value.Key.Space).ThenBy(value => value.Key.Index)
            .Select(value => value.Value).ToImmutableArray();
    }
    public async ValueTask<TowerSettlementReceipt> SettleAsync(BossTowerSnapshot run, ulong seed, IEffectFootprintRecorder footprint, CancellationToken token)
    {
        RequireTransaction();
        var completion = run.Completion ?? throw new InvalidOperationException("Tower has not ended.");
        var receipts = ImmutableArray.CreateBuilder<TowerMemberSettlementReceipt>();
        foreach (var member in run.Members)
        {
            var session = member.Session;
            footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
            var character = characters.Get(session.CharacterId);
            var progress = await character.ProgressAsync(token).ConfigureAwait(false)
                ?? throw new InvalidDataException("Tower member has no progression.");
            var candidates = (await rules.CandidatesAsync(progress.Level, completion.RewardLevel,
                DropRandom.SeedFor(unchecked((int)seed), run.DungeonId, completion.RewardLevel, member.Slot, 0), token)
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
                    footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
                    goldBalance = await character.AddGoldAsync(candidate.DisplayValue, token).ConfigureAwait(false);
                    candidates[i] = candidate with { DisplayValue = goldBalance - previousGold, Delivered = goldBalance > previousGold };
                    continue;
                }
                if (candidate.ItemId < 0) continue;
                footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
                var receipt = await delivery.DeliverAsync(session.AccountId, session.CharacterId,
                    [new(candidate.ItemId, 1) { Durability = candidate.Durability }], "塔通关奖励", token, ItemDeliveryMode.InventoryThenMail).ConfigureAwait(false);
                candidates[i] = candidate with { DisplayValue = receipt.Inventory.FirstOrDefault()?.Snapshot.InstanceValue
                    ?? receipt.MailedItems.FirstOrDefault()?.InstanceValue ?? candidate.DisplayValue, Delivered = true };
                delivered.Add(new(candidate.ItemId, 1));
                updates.AddRange(receipt.Inventory.Select(x => new TowerInventoryUpdate(x.Slot, x.Snapshot)));
            }
            var award = await characters.Get(session.CharacterId).AddExpAsync(new TowerClearExperienceContext(
                new(ExperienceEvent.TowerClear, session.AccountId, session.CharacterId, run.DungeonId), DungeonParticipationMode.BossTower, completion.RewardLevel),
                clock.GetUtcNow(), session, footprint, token).ConfigureAwait(false)
                ?? throw new InvalidDataException("Tower member has no progression.");
            var facts = new List<QuestProgressFact>();
            if (completion.Reason == TowerEndReason.Completed) facts.Add(new QuestDungeonCleared(run.DungeonId, DungeonDifficulty.Normal));
            if (goldBalance != beforeGold || delivered.Count > 0) facts.Add(new QuestHoldingsChanged());
            footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
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
