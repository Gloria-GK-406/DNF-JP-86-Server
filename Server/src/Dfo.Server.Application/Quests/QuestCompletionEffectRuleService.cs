using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Quests;

internal sealed class QuestCompletionEffectRuleService(IQuestContentRuleService quests,
    IQuestRewardRuleService rewards, IExperienceRuleService experience, ICarriedGoldRuleService gold,
    IItemPlacementRuleService placements, IAccountMaterialRuleService accounts,
    ICharacterStatRuleService stats, ICharacterSkillRuleService skills, ICreatureDefinitionSource creatures,
    IAchievementRuleService achievements, ITitleBookRuleService titleBook, IQuestStateRuleService? stateRules = null)
    : IQuestCompletionEffectRuleService
{
    private readonly IQuestStateRuleService stateRules = stateRules ?? new QuestStateRuleService(quests);

    public ValueTask<QuestDefinition?> DefinitionAsync(int id, CancellationToken ct) => quests.FindAsync(id, ct);
    public QuestRewardResolution Rewards(QuestDefinition definition, QuestCompletionFacts facts, ushort selection, ushort count)
    {
        if (!TitleRewardsHaveUnlockRules(definition))
            return QuestRewardResolution.Refused(QuestRewardRejection.UnmappedTitleReward);
        // PVF's reward type=title is not in ItemRewards. It is granted once by the book
        // after the completed quest satisfies the corresponding title unlock definition.
        return rewards.Resolve(definition, quests.Parameters(), new(facts.Identity.Job, facts.Identity.GrowType, facts.Progress.Level), selection, count);
    }
    private bool TitleRewardsHaveUnlockRules(QuestDefinition quest)
    {
        foreach (var reward in quest.Rewards.Where(x => x.Kind == QuestRewardKind.Title))
        {
            if (reward.Values.Count == 0 || reward.Values.Count % 2 != 0) return false;
            for (var i = 0; i < reward.Values.Count; i += 2)
            {
                var item = reward.Values[i];
                if (item <= 0 || reward.Values[i + 1] != 1) return false;
                var unlocks = achievements.Catalog().Where(x => achievements.IsSupported(x)
                    && x.Condition.Type is "clear quest" or "clearquest"
                    && x.Condition.Values.Contains(quest.QuestId)).Select(x => x.Id).ToHashSet();
                if (!titleBook.Catalog().SelectMany(x => x.Slots).Any(x => x.SlotType >= 0
                    && unlocks.Contains(x.QuestId) && x.ItemIds.FirstOrDefault(id => id > 0) == item)) return false;
            }
        }
        return true;
    }

    public async ValueTask<ImmutableArray<CarriedQuest>> EffectiveAsync(QuestStateFacts state,
        IEnumerable<ItemSnapshot> main, int carriedGold, CancellationToken ct, IEnumerable<ItemSnapshot>? sharedMaterials = null)
    {
        var evaluated = await stateRules.EvaluateAsync(
            state.Carried.Select(x => new CarriedQuest(x.Slot, x.QuestId, x.TriggerValue)).ToArray(),
            state.Cleared.Select(x => new ClearedQuest(x.QuestId, x.Mark)).ToArray(),
            stateRules.Holdings(main, sharedMaterials ?? [], carriedGold), ct).ConfigureAwait(false);
        return evaluated.Select(x => x.Quest).ToImmutableArray();
    }

    public async ValueTask<ImmutableArray<DomainEffect>> RewardsAsync(QuestCompletionRewardPlan reward,
        QuestCompletionFacts facts, CancellationToken ct)
    {
        var effects = ImmutableArray.CreateBuilder<DomainEffect>();
        var goldCost = checked((reward.ConsumedItems ?? []).Where(x => x.ItemId == 0).Sum(x => x.RequiredCount));
        if (goldCost < 0 || goldCost > facts.Gold.Carried) throw new InventoryEffectException(InventoryEffectRejection.InsufficientAssets);
        if (goldCost > 0) effects.Add(new SpendGoldEffect(goldCost));
        foreach (var requirement in (reward.ConsumedItems ?? []).GroupBy(x => x.ItemId))
        {
            if (requirement.Key == 0) continue;
            var remaining = checked(requirement.Sum(x => x.RequiredCount));
            if (requirement.Key <= 0 || remaining <= 0) throw new InventoryEffectException(InventoryEffectRejection.UnsupportedItem);
            var isShared = accounts.TryGetByItemId(requirement.Key, out _);
            foreach (var item in (isShared ? facts.SharedMaterials : facts.Main).Where(x => x.ItemId == requirement.Key && x.IsPile && x.Snapshot.EquipmentLockId == 0)
                         .OrderBy(x => x.StorageSlotIndex))
            {
                var take = Math.Min(remaining, item.Count);
                if (take <= 0) continue;
                var slot = item.BusinessSlot ?? (isShared ? new BusinessSlot(BusinessInventorySpace.SharedMaterial, item.StorageSlotIndex)
                    : A21InventoryLayout.ToBusinessSlot(new GameSlot(InventoryContainerType.Main, item.StorageSlotIndex)));
                effects.Add(new ConsumeFromContainerEffect(slot, item.ItemId, take));
                remaining -= take;
                if (remaining == 0) break;
            }
            if (remaining != 0) throw new InventoryEffectException(InventoryEffectRejection.InvalidTarget);
        }
        foreach (var item in reward.Items)
        {
            {
                var placement = await placements.PlacementForAsync(item.ItemId, ct).ConfigureAwait(false)
                    ?? throw new InventoryEffectException(InventoryEffectRejection.UnsupportedItem);
                if (placement.WorldClass == ItemWorldClass.Creature
                    && await creatures.FindByEquipmentAsync(item.ItemId, ct).ConfigureAwait(false) is null)
                    throw new InventoryEffectException(InventoryEffectRejection.UnsupportedItem);
                effects.Add(new GrantBasicCharacterItemEffect(item.ItemId, item.Count, placement));
            }
        }
        if (reward.Gold > 0)
        {
            var ceiling = gold.CarryLimitFor(facts.Gold.Level, facts.Gold.StoredCarryLimit);
            var beforeReward = facts.Gold.Carried - goldCost;
            var after = checked((int)Math.Max(0, Math.Min(ceiling, (long)beforeReward + reward.Gold)));
            if (after < beforeReward) throw new InventoryEffectException(InventoryEffectRejection.InvalidTarget);
            effects.Add(new SetQuestRewardGoldEffect(beforeReward, after));
        }
        return effects.ToImmutable();
    }

    public async ValueTask<QuestCompletionPlan> CompleteAsync(ushort questId, QuestCompletionRewardPlan reward,
        QuestCompletionFacts facts, ImmutableArray<DomainEffect> rewardEffects, InventoryEffectHoldingsSnapshot after, CancellationToken ct)
    {
        var target = facts.Quests.Carried.First(x => x.QuestId == questId);
        var effects = ImmutableArray.CreateBuilder<DomainEffect>();
        effects.Add(new SetCarriedQuestEffect(target, null));
        effects.Add(new SetClearedQuestEffect(questId, facts.Quests.Cleared.FirstOrDefault(x => x.QuestId == questId)?.Mark, IQuestLog.ClearedMark));
        effects.AddRange(rewardEffects);
        var xp = experience.DecideAward(facts.Progress, new ExperienceBreakdown(Base: reward.Experience));
        effects.Add(new CharacterExperienceEffect(xp));
        byte? packedGrowType = reward.GrowType;
        if (reward.GrowType is { } grow)
        {
            var beforeCareer = CharacterCareer.FromPacked(facts.Identity.Job, facts.Identity.GrowType);
            if (beforeCareer.GrowType != 0 || beforeCareer.Awakening != 0) throw new InvalidOperationException("Character is already advanced.");
            var career = new CharacterCareer(beforeCareer.Job, grow);
            await stats.InitializeAsync(ct).ConfigureAwait(false);
            _ = stats.GetStats(career, xp.Award.Level);
            var skillFacts = facts.Skills ?? throw new InvalidOperationException("Skill state is unavailable.");
            var builds = facts.Builds ?? throw new InvalidOperationException("Skill builds are unavailable.");
            _ = skills.GetLedger(career, xp.Award.Level, skillFacts.Pages, skillFacts.PointBasis);
            effects.Add(new SetCharacterGrowTypeEffect(facts.Identity.Job, facts.Identity.GrowType, grow));
            effects.Add(new MaterializeSkillBuildsEffect(builds.Active, [.. builds.Builds]));
            foreach (var page in skillFacts.Pages.OrderBy(x => x.PageIndex))
            {
                foreach (var old in page.Skills)
                    effects.Add(new SetLearnedSkillEffect(page.PageIndex, old.Slot, checked((int)old.SkillId), old.Level, null));
                var occupied = new HashSet<int>();
                foreach (var rule in skills.GetAll(career.Job).OrderBy(x => x.SkillId))
                {
                    var free = rule.GetFreeLevel(career, xp.Award.Level);
                    if (free <= 0) continue;
                    var slot = rule.FindAvailableSlot(occupied) ?? throw new InvalidDataException("No slot for a granted skill.");
                    effects.Add(new SetLearnedSkillEffect(page.PageIndex, slot, rule.SkillId, null, checked((byte)free)));
                    occupied.Add(slot);
                }
            }
        }
        if (reward.Awakening is { } awakening)
        {
            var before = CharacterCareer.FromPacked(facts.Identity.Job, facts.Identity.GrowType);
            if (awakening is not (1 or 2) || before.Awakening != awakening - 1 || reward.GrowType is not null)
                throw new InvalidOperationException("Invalid awakening transition.");
            var career = new CharacterCareer(before.Job, before.GrowType, awakening);
            await stats.InitializeAsync(ct).ConfigureAwait(false);
            _ = stats.GetStats(career, xp.Award.Level);
            var skillFacts = facts.Skills ?? throw new InvalidOperationException("Skill state is unavailable.");
            var builds = facts.Builds ?? throw new InvalidOperationException("Skill builds are unavailable.");
            _ = skills.GetLedger(career, xp.Award.Level, skillFacts.Pages, skillFacts.PointBasis);
            packedGrowType = career.PackedGrowType;
            effects.Add(new SetCharacterAwakeningEffect(before.Job, facts.Identity.GrowType, career.PackedGrowType));
            effects.Add(new MaterializeSkillBuildsEffect(builds.Active, [.. builds.Builds]));
            foreach (var page in skillFacts.Pages.OrderBy(x => x.PageIndex))
            {
                var occupied = page.Skills.Select(x => (int)x.Slot).ToHashSet();
                foreach (var rule in skills.GetAll(career.Job).OrderBy(x => x.SkillId))
                {
                    var free = rule.GetFreeLevel(career, xp.Award.Level);
                    if (free <= rule.GetFreeLevel(before, facts.Progress.Level)) continue;
                    var learned = page.Skills.SingleOrDefault(x => x.SkillId == rule.SkillId);
                    if (learned is not null)
                    {
                        if (learned.Level < free)
                            effects.Add(new SetLearnedSkillEffect(page.PageIndex, learned.Slot, rule.SkillId, learned.Level, checked((byte)free)));
                    }
                    else
                    {
                        var slot = rule.FindAvailableSlot(occupied) ?? throw new InvalidDataException("No slot for an awakening skill.");
                        effects.Add(new SetLearnedSkillEffect(page.PageIndex, slot, rule.SkillId, null, checked((byte)free)));
                        occupied.Add(slot);
                    }
                }
            }
        }
        var remaining = facts.Quests with
        {
            Carried = [.. facts.Quests.Carried.Where(x => x.Slot != target.Slot)],
            Cleared = [.. facts.Quests.Cleared.Where(x => x.QuestId != questId), new(questId, IQuestLog.ClearedMark)],
        };
        var projected = await EffectiveAsync(remaining, after.Main.Values, after.Gold, ct, after.SharedMaterials.Values).ConfigureAwait(false);
        foreach (var row in projected)
        {
            var old = remaining.Carried.Single(x => x.Slot == row.Slot);
            if (old.TriggerValue != row.TriggerValue) effects.Add(new SetCarriedQuestEffect(old, old with { TriggerValue = row.TriggerValue }));
        }
        var changed = effects.Any(x => x is SetCarriedQuestEffect { Value: not null });
        return new(questId, effects.ToImmutable(), xp.Award,
            changed ? new QuestProgressSnapshot(projected) : null, packedGrowType) { Awakening = reward.Awakening };
    }
}
