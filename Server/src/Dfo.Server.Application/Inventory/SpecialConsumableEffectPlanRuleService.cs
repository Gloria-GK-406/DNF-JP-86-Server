using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class SpecialConsumableEffectPlanRuleService(IStackableActionDefinitionSource actions,
    IItemUsageRuleSource usages, IDailyResetRuleService days, IItemUseRuleService uses, ICharacterSkillRuleService skills, IQuestClearancePlanRuleService clearance) : ISpecialConsumableEffectPlanRuleService
{
    private readonly IQuestClearancePlanRuleService clearance = clearance;

    public async ValueTask<ImmutableArray<DomainEffect>> FatigueAsync(FatiguePotionFacts f, CancellationToken ct)
    {
        var action = await actions.GetAsync(f.Item.ItemId, ct).ConfigureAwait(false);
        if (action is not { IsReadable: true, Action: "[add fatigue]", FatigueRecovery: > 0 and <= ushort.MaxValue })
            throw Reject("Not a supported fatigue potion.");
        if (!f.InTown || f.Level < action.MinimumLevel || f.Level > action.MaximumLevel
            || action.ExpiresAtUnixSeconds > 0 && action.ExpiresAtUnixSeconds <= f.At.ToUnixTimeSeconds())
            throw Reject("Fatigue potion is not usable here, at this level, or after expiration.");
        var effective = f.Fatigue.Raw is { } raw && days.IsCurrentPeriod(raw.UpdatedAt, f.At) ? raw : new(0, 0, f.At);
        var used = Math.Min(f.Fatigue.Total, effective.Used);
        if (used < action.FatigueRecovery) throw Reject("Not enough fatigue recovery space.");
        var usage = await usages.GetAsync(f.Item.ItemId, ct).ConfigureAwait(false);
        if (usage is not { IsReadable: true, DailyLimit: >= -1, CooldownMilliseconds: >= 0 })
            throw new InvalidDataException("Missing or invalid usage rule.");
        if (f.Usage?.UsedCount < 0) throw new InvalidDataException("Invalid item usage count.");
        var count = f.Usage?.LastUsedAt is { } last && days.IsCurrentPeriod(last, f.At) ? f.Usage.UsedCount : 0;
        if (usage.DailyLimit >= 0 && count >= usage.DailyLimit) throw Reject(nameof(ItemUsageRejection.DailyLimitReached));
        if (f.Usage?.LastUsedAt?.AddMilliseconds(usage.CooldownMilliseconds) > f.At)
            throw Reject(nameof(ItemUsageRejection.CoolingDown));
        var result = await ConsumptionAsync(f.Source, f.Item, ct).ConfigureAwait(false);
        return result.Add(new SetFatigueStateEffect(f.Fatigue.Raw,
                new(used - action.FatigueRecovery, effective.ConsumedToday, f.At)))
            .Add(new SetItemUsageStateEffect(f.Item.ItemId, f.Usage, new(checked(count + 1), f.At)));
    }

    public async ValueTask<QuestCouponPlan> CouponAsync(BusinessSlot source, ItemReference item, QuestStateFacts state,
        IReadOnlyList<int> selected, DateTimeOffset at, CancellationToken ct)
    {
        var task = await clearance.BuildAsync(state, selected, ct).ConfigureAwait(false);
        var consumption = await ConsumptionAsync(source, item, ct).ConfigureAwait(false);
        return task with { Effects = consumption.AddRange(task.Effects) };
    }

    public async ValueTask<ImmutableArray<DomainEffect>> RefundAsync(BusinessSlot source, ItemReference water, int page,
        IReadOnlyList<CharacterSkillState> before, IReadOnlyList<CharacterSkillState> changed, CancellationToken ct)
    {
        var result = (await ConsumptionAsync(source, water, ct).ConfigureAwait(false)).ToBuilder();
        foreach (var skill in changed)
        {
            var previous = before.SingleOrDefault(x => x.SkillId == skill.SkillId);
            result.Add(new SetLearnedSkillEffect(page, skill.Slot, checked((int)skill.SkillId), previous?.Level,
                skill.Level == 0 ? null : skill.Level));
        }
        return result.ToImmutable();
    }

    public RefundSkillPlan RefundSkills(CharacterSkillFacts facts, int page, IReadOnlyList<CharacterSkillLearningChange> changes, int learningLevel)
    {
        var rules = skills;
        var current = facts.Pages.SingleOrDefault(x => x.PageIndex == page)?.Skills ?? [];
        var target = current.ToDictionary(x => checked((int)x.SkillId));
        var occupied = current.Select(x => (int)x.Slot).ToHashSet();
        var changed = ImmutableArray.CreateBuilder<CharacterSkillState>();
        foreach (var change in changes)
        {
            var rule = rules.Get(facts.Career.Job, change.SkillId);
            if (rule is null) return Refuse(CharacterSkillLearningRejection.SkillUnavailable);
            var existing = target.GetValueOrDefault(change.SkillId);
            var level = (existing?.Level ?? 0) + (change.Refund == 0 ? change.Increment : -change.Increment);
            if (change.Refund != 0 && (existing is null || level < rule.GetFreeLevel(facts.Career, facts.Level)))
                return Refuse(CharacterSkillLearningRejection.InvalidRequest);
            if (level > byte.MaxValue) return Refuse(CharacterSkillLearningRejection.SkillUnavailable);
            var slot = existing?.Slot ?? rule.FindAvailableSlot(occupied);
            if (slot is null) return Refuse(CharacterSkillLearningRejection.NoSlot);
            if (slot is < 0 or >= byte.MaxValue || existing?.ExtraValues.Count > byte.MaxValue)
                return Refuse(CharacterSkillLearningRejection.InvalidRequest);
            var learned = new CharacterSkillState(slot.Value, change.SkillId, (byte)level, existing?.ExtraValues ?? []);
            occupied.Add(slot.Value); target[change.SkillId] = learned; changed.Add(learned);
        }
        var final = target.ToDictionary(x => x.Key, x => (int)x.Value.Level);
        foreach (var learned in changed.Where(x => changes.Single(c => c.SkillId == x.SkillId).Refund == 0))
            if (!rules.Get(facts.Career.Job, (int)learned.SkillId)!.CanLearn(facts.Career, learningLevel, learned.Level, final))
                return Refuse(CharacterSkillLearningRejection.SkillUnavailable);
        foreach (var learned in target.Values.Where(x => x.Level > 0))
        {
            var rule = rules.Get(facts.Career.Job, (int)learned.SkillId);
            if (rule is null) return Refuse(CharacterSkillLearningRejection.IncompleteLedger);
            if (learned.Level > rule.GetFreeLevel(facts.Career, facts.Level)
                && rule.Prerequisites.Any(pre => final.GetValueOrDefault(pre.SkillId) < pre.Level))
                return Refuse(CharacterSkillLearningRejection.SkillUnavailable);
        }
        var pages = facts.Pages.Where(x => x.PageIndex != page)
            .Append(new CharacterSkillPageState(page, target.Values.OrderBy(x => x.Slot).ToArray())).ToArray();
        var ledger = rules.GetLedger(facts.Career, facts.Level, pages, facts.PointBasis);
        if (!ledger.IsComplete) return Refuse(CharacterSkillLearningRejection.IncompleteLedger);
        if (ledger.For(new(page)) is { Sp: < 0 } or { Tp: < 0 }) return Refuse(CharacterSkillLearningRejection.InsufficientPoints);
        return new(CharacterSkillLearningRejection.None, changed.ToImmutable(), ledger);
    }
    private static RefundSkillPlan Refuse(CharacterSkillLearningRejection rejection) => new(rejection, [], null);

    public ImmutableArray<DomainEffect> Rename(PetNameFacts f)
    {
        if (f.Source.Space != BusinessInventorySpace.CreatureConsumable || f.Source.Index is < 0 or > 50
            || f.Card is not { Snapshot: { LegacyKind: 7 }, ItemId: 25, IsPile: true, Count: > 0 }
            || f.Worn is not { Snapshot: { LegacyKind: 5 } } || f.Current is null
            || f.Worn.ItemUid?.Value != f.Current.ItemUid || f.Worn.ItemId != f.Current.ItemTemplateId
            || f.Card.Snapshot.Lifetime.IsExpired(f.At) || f.Worn.Snapshot.Lifetime.IsExpired(f.At)
            || f.Name.IsDefault || f.Name.Length > 12 || f.Name.Any(x => x is 0 or < 0x20 or 0x7F)) return [];
        return [new ConsumeFromContainerEffect(f.Source, 25, 1, f.Card.Snapshot.EquipmentLockId),
            new RenamePetEffect(f.Current.ItemUid, f.Current.ItemTemplateId, f.Name)];
    }

    private async ValueTask<ImmutableArray<DomainEffect>> ConsumptionAsync(BusinessSlot source, ItemReference item, CancellationToken ct)
    {
        var rule = await uses.RuleForAsync(item.ItemId, ct).ConfigureAwait(false);
        if (rule is not { IsReadable: true }) throw Reject("Unusable source declaration.");
        var result = ImmutableArray.CreateBuilder<DomainEffect>();
        result.Add(new ConsumeFromContainerEffect(source, item.ItemId, 1));
        if (rule.CooldownMilliseconds > 0)
            result.Add(new StartItemCooldownEffect(uses.CooldownKeyFor(item.ItemId, rule), TimeSpan.FromMilliseconds(rule.CooldownMilliseconds)));
        return result.ToImmutable();
    }
    private static OpeningRejectedException Reject(string message) => new(OpeningRejection.ItemUnusable, message);
}
