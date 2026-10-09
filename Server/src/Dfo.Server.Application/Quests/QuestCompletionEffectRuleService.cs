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
    IAchievementRuleService achievements, ITitleBookRuleService titleBook)
    : IQuestCompletionEffectRuleService
{
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
        IEnumerable<ItemSnapshot> main, int carriedGold, CancellationToken ct)
    {
        var counts = main.Where(x => x.ItemId > 0 && !accounts.TryGetByItemId(x.ItemId, out _))
            .GroupBy(x => x.ItemId).ToDictionary(x => x.Key, x => x.Sum(y => (long)y.Count));
        counts[0] = carriedGold;
        var result = ImmutableArray.CreateBuilder<CarriedQuest>();
        foreach (var row in state.Carried.OrderBy(x => x.Slot))
        {
            var definition = await quests.FindAsync(row.QuestId, ct).ConfigureAwait(false);
            var trigger = row.TriggerValue;
            if (definition?.SeekingItems is { Count: > 0 } requirements)
            {
                var missing = requirements.Sum(x => Math.Max(0, x.RequiredCount - counts.GetValueOrDefault(x.ItemId)));
                trigger = unchecked((int)new QuestTrigger(unchecked((uint)trigger)).ReplaceChannel(0, missing).PackedValue);
            }
            result.Add(new(row.Slot, row.QuestId, trigger));
        }
        return result.ToImmutable();
    }

    public async ValueTask<ImmutableArray<DomainEffect>> RewardsAsync(QuestCompletionRewardPlan reward,
        QuestCompletionFacts facts, CancellationToken ct)
    {
        var effects = ImmutableArray.CreateBuilder<DomainEffect>();
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
            var after = checked((int)Math.Max(0, Math.Min(ceiling, (long)facts.Gold.Carried + reward.Gold)));
            if (after < facts.Gold.Carried) throw new InventoryEffectException(InventoryEffectRejection.InvalidTarget);
            effects.Add(new SetQuestRewardGoldEffect(facts.Gold.Carried, after));
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
        var remaining = facts.Quests with { Carried = [.. facts.Quests.Carried.Where(x => x.Slot != target.Slot)] };
        var projected = await EffectiveAsync(remaining, after.Main.Values, after.Gold, ct).ConfigureAwait(false);
        var cleared = facts.Quests.Cleared.Where(x => x.Mark != 0).Select(x => x.QuestId).Append((int)questId).ToHashSet();
        var final = ImmutableArray.CreateBuilder<CarriedQuest>();
        foreach (var row in projected)
        {
            var definition = await quests.FindAsync(row.QuestId, ct).ConfigureAwait(false);
            var next = row;
            if (definition?.ClearedQuestRequirements is { Count: > 0 } required && required.Contains(questId))
                next = row with { TriggerValue = unchecked((int)new QuestTrigger(unchecked((uint)row.TriggerValue))
                    .ReplaceChannel(0, required.Count(x => !cleared.Contains(x))).PackedValue) };
            var old = remaining.Carried.Single(x => x.Slot == row.Slot);
            if (old.TriggerValue != next.TriggerValue) effects.Add(new SetCarriedQuestEffect(old, old with { TriggerValue = next.TriggerValue }));
            final.Add(next);
        }
        var changed = effects.Any(x => x is SetCarriedQuestEffect { Value: not null });
        return new(questId, effects.ToImmutable(), xp.Award,
            changed ? new QuestProgressSnapshot(final.ToImmutable()) : null, reward.GrowType);
    }
}
