using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Inventory;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterSkillLearningDomainService(
    ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService currentViews, ITownAreaDomainService townAreas,
    ICharacterSkillTable skills,
    ICharacterSkillStateDomainService state, ICharacterSkillRuleService rules,
    IDomainTransactionAccessor transactions, IPremiumServiceDomainService premiums,
    IPremiumServiceRuleService premiumRules, ICharacterBusinessInventoryDomainService businessInventories,
    IOpeningInventoryDomainService inventory, ICharacterSkillResetRuleService resetRules, TimeProvider clock,
    ISpecialConsumableEffectPlanRuleService effectPlans) : ICharacterSkillLearningDomainService
{
    public async ValueTask<bool> SwapSlotsAsync(SelectedSessionIdentity session, int pageIndex, int first, int second, CancellationToken ct)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Skill slot changes require a transaction.");
        if (pageIndex < 0 || first is < 0 or > 203 || second is < 0 or > 203
            || !sessions.IsInWorld(session)) return false;
        var view = await currentViews.TryLoadAsync(session, ct);
        if (view is null || view.World.Channel.PvpSkillProfileApplies) return false;
        var builds = await state.BuildsAsync(session.CharacterId, ct);
        if (builds.Active.Value != pageIndex || !builds.IsUnlocked(new(pageIndex))) return false;
        var rows = await skills.ReadLevelsAsync(session.CharacterId, ct);
        if (rows.Any(r => r.PageIndex == pageIndex && r.Slot < 0)
            || !rows.Any(r => r.PageIndex == pageIndex && (r.Slot == first || r.Slot == second))) return false;
        await skills.SwapSlotsAsync(session.CharacterId, pageIndex, first, second, ct);
        return true;
    }

    public async ValueTask<int?> SwitchAsync(SelectedSessionIdentity session, int expectedCurrent, int target, CancellationToken ct)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Skill switching requires a transaction.");
        if (target < 0 || !sessions.IsInWorld(session)
            || townAreas.Find(session.CharacterId) is not { } town || !session.Owns(town))
            return null;
        var view = await currentViews.TryLoadAsync(session, ct);
        if (view is null || view.World.Channel.PvpSkillProfileApplies) return null;
        var builds = await state.BuildsAsync(session.CharacterId, ct);
        if (builds.Active.Value != expectedCurrent || !builds.IsUnlocked(new(target))) return null;
        await state.ActivateAsync(session.CharacterId, new(target), ct);
        return target;
    }

    public async ValueTask<CharacterSkillLearningResult> LearnAsync(SelectedSessionIdentity session, int pageIndex,
        IReadOnlyList<CharacterSkillLearningChange> changes, byte mode, IEffectFootprintRecorder footprint, CancellationToken cancellationToken)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        if (transactions.Current is null)
            throw new InvalidOperationException("Skill learning requires a domain transaction.");
        if (!sessions.IsInWorld(session))
            return new(CharacterSkillLearningRejection.NoSession);
        var selected = await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (selected is null)
            return new(CharacterSkillLearningRejection.NoSession);
        if (changes.Count is 0 or > byte.MaxValue || changes.Any(change =>
            change.SkillId == 0 || change.Refund > 1 || change.Increment == 0)
            || changes.Select(change => change.SkillId).Distinct().Count() != changes.Count)
            return new(CharacterSkillLearningRejection.InvalidRequest);
        if (mode != 0 || selected.World.Channel.PvpSkillProfileApplies
            || townAreas.Find(session.CharacterId) is not { } member
            || !session.Owns(member))
            return new(CharacterSkillLearningRejection.UnsupportedScenario);
        if (pageIndex < 0) return new(CharacterSkillLearningRejection.PageUnavailable);
        var builds = await state.BuildsAsync(session.CharacterId, cancellationToken).ConfigureAwait(false);
        if (!builds.IsUnlocked(new(pageIndex)) || builds.Active.Value != pageIndex)
            return new(CharacterSkillLearningRejection.PageUnavailable);
        var facts = await state.ReadAsync(session.CharacterId, cancellationToken).ConfigureAwait(false);
        if (facts is null) return new(CharacterSkillLearningRejection.NoSession);
        var current = facts.Pages.SingleOrDefault(page => page.PageIndex == pageIndex)?.Skills ?? [];
        if (current.Select(skill => skill.SkillId).Distinct().Count() != current.Count)
            return new(CharacterSkillLearningRejection.IncompleteLedger);
        if (changes.Any(change => change.Refund != 0))
            return await LearnRefundAsync(session, pageIndex, changes, facts, current, footprint, cancellationToken).ConfigureAwait(false);
        var target = current.ToDictionary(skill => checked((int)skill.SkillId));
        var occupied = current.Select(skill => (int)skill.Slot).ToHashSet();
        var changed = new List<CharacterSkillState>(changes.Count);
        foreach (var change in changes)
        {
            var rule = rules.Get(facts.Career.Job, change.SkillId);
            if (rule is null)
                return new(CharacterSkillLearningRejection.SkillUnavailable);
            var existing = target.GetValueOrDefault(change.SkillId);
            var level = (existing?.Level ?? 0) + (change.Refund == 0 ? change.Increment : -change.Increment);
            if (change.Refund != 0 && (existing is null || level < rule.GetFreeLevel(facts.Career, facts.Level)))
                return new(CharacterSkillLearningRejection.InvalidRequest);
            if (level > byte.MaxValue)
                return new(CharacterSkillLearningRejection.SkillUnavailable);
            var slot = existing?.Slot ?? rule.FindAvailableSlot(occupied);
            if (slot is null) return new(CharacterSkillLearningRejection.NoSlot);
            if (slot is < 0 or >= byte.MaxValue || existing?.ExtraValues.Count > byte.MaxValue)
                return new(CharacterSkillLearningRejection.InvalidRequest);
            var learned = new CharacterSkillState(slot.Value, change.SkillId, (byte)level,
                existing?.ExtraValues ?? []);
            occupied.Add(slot.Value);
            target[change.SkillId] = learned;
            changed.Add(learned);
        }
        var learningLevel = (int)facts.Level;
        if (await premiums.For(session.AccountId).HasBenefitAsync(PremiumBenefit.LearnSkillsEarly, cancellationToken).ConfigureAwait(false))
            learningLevel = checked(learningLevel + premiumRules.LevelAllowance(PremiumBenefit.LearnSkillsEarly));
        var finalLevels = target.ToDictionary(pair => pair.Key, pair => (int)pair.Value.Level);
        foreach (var learned in changed.Where(skill => changes.Single(c => c.SkillId == skill.SkillId).Refund == 0))
            if (!rules.Get(facts.Career.Job, (int)learned.SkillId)!
                .CanLearn(facts.Career, learningLevel, learned.Level, finalLevels))
                return new(CharacterSkillLearningRejection.SkillUnavailable);
        var pages = facts.Pages.Where(page => page.PageIndex != pageIndex)
            .Append(new CharacterSkillPageState(pageIndex, target.Values.OrderBy(skill => skill.Slot).ToArray())).ToArray();
        var ledger = rules.GetLedger(facts.Career, facts.Level, pages, facts.PointBasis);
        if (!ledger.IsComplete) return new(CharacterSkillLearningRejection.IncompleteLedger);
        if (ledger.For(new(pageIndex)) is { Sp: < 0 } or { Tp: < 0 })
            return new(CharacterSkillLearningRejection.InsufficientPoints);

        // No rejection returns after the first write: storage failures must roll the whole batch back.
        foreach (var learned in changed)
        {
            footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
            if (learned.Level == 0)
            {
                if (await skills.RemoveAsync(session.CharacterId, pageIndex, learned.Slot, cancellationToken) != 1)
                    throw new InvalidOperationException("Skill changed during refund.");
            }
            else if (current.Any(skill => skill.SkillId == learned.SkillId))
            {
                if (await skills.SetLevelAsync(session.CharacterId, pageIndex, learned.Slot,
                    learned.Level, cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("The learned skill row changed during its transaction.");
            }
            else
                await skills.InsertAsync(session.CharacterId, pageIndex, learned.Slot, (int)learned.SkillId,
                    learned.Level, cancellationToken).ConfigureAwait(false);
        }
        return new(CharacterSkillLearningRejection.None, new(session, pageIndex, changed.AsReadOnly(), ledger));
    }

    private async ValueTask<CharacterSkillLearningResult> LearnRefundAsync(SelectedSessionIdentity session, int pageIndex,
        IReadOnlyList<CharacterSkillLearningChange> changes, CharacterSkillFacts facts,
        IReadOnlyList<CharacterSkillState> current, IEffectFootprintRecorder footprint, CancellationToken cancellationToken)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        var learningLevel = (int)facts.Level;
        if (await premiums.For(session.AccountId).HasBenefitAsync(PremiumBenefit.LearnSkillsEarly, cancellationToken).ConfigureAwait(false))
            learningLevel = checked(learningLevel + premiumRules.LevelAllowance(PremiumBenefit.LearnSkillsEarly));
        var decision = effectPlans.RefundSkills(facts, pageIndex, changes, learningLevel);
        if (decision.Rejection != CharacterSkillLearningRejection.None) return new(decision.Rejection);
        var at = clock.GetUtcNow();
        var itemStore = businessInventories.GetStore(session.CharacterId, BusinessInventoryStoreKind.Item);
        (BusinessSlot Slot, ItemReference Item)? water = null;
        foreach (var space in new[] { BusinessInventorySpace.QuickSlot, BusinessInventorySpace.Consumable })
        {
            var bag = itemStore.FindContainer(space);
            if (bag is null) continue;
            var found = (await bag.GetAllAsync(cancellationToken))
                .Where(item => item.IsPile && item.ItemId == resetRules.ConsumableItemId && item.Count > 0
                    && !item.Snapshot.Lifetime.IsExpired(at))
                .OrderBy(item => item.SlotIndex).FirstOrDefault();
            if (found is not null) { water = (new(space, found.SlotIndex), found); break; }
        }
        if (water is null) return new(CharacterSkillLearningRejection.ResetItemRequired);
        var waterSlot = water.Value.Slot;
        var source = await inventory.SourceAsync(session, waterSlot, at, cancellationToken);
        var effects = await effectPlans.RefundAsync(waterSlot, source, pageIndex, current, decision.Changed, cancellationToken).ConfigureAwait(false);
        var consumed = (await inventory.ApplyAsync(session, effects, at, footprint, cancellationToken).ConfigureAwait(false)).Inventory;
        return new(CharacterSkillLearningRejection.None, new(session, pageIndex, decision.Changed, decision.Ledger!, consumed));
    }
}
