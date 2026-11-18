using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Settlement;
using Dfo.Server.Application.Quests;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Settlement;

internal sealed class QuestClearCouponSettlementService(IOpeningInventoryDomainService inventory,
    ICharacterDomainService characters, IConsumableStateDomainService states,
    IQuestClearCouponRuleService rules, ISpecialConsumableEffectPlanRuleService plans,
    AbyssQuestClearTicketRuleService abyssTickets, IQuestFactsDomainService questFacts,
    IItemPlacementRuleService placements, ICharacterBusinessInventoryDomainService bags,
    IOpeningEffectPlanRuleService openingPlans, ICharacterConditionTable conditions) : IQuestClearCouponSettlementDomainService
{
    public ValueTask<QuestClearCouponResult> UseAsync(SelectedSessionIdentity session,
        BusinessSlot sourceSlot, DateTimeOffset now, IEffectFootprintRecorder footprint, CancellationToken ct) =>
        UseAsync(session, sourceSlot, now, footprint, abyssTicket: false, ct);

    public ValueTask<QuestClearCouponResult> UseAbyssTicketAsync(SelectedSessionIdentity session,
        BusinessSlot sourceSlot, DateTimeOffset now, IEffectFootprintRecorder footprint, CancellationToken ct) =>
        UseAsync(session, sourceSlot, now, footprint, abyssTicket: true, ct);

    private async ValueTask<QuestClearCouponResult> UseAsync(SelectedSessionIdentity session,
        BusinessSlot sourceSlot, DateTimeOffset now, IEffectFootprintRecorder footprint, bool abyssTicket, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        var source = await inventory.SourceAsync(session, sourceSlot, now, ct).ConfigureAwait(false);
        if (!(abyssTicket ? source.ItemId == AbyssQuestClearTicketRuleService.ItemId : rules.IsCoupon(source.ItemId)))
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Not a quest-clear coupon.");

        var state = await states.QuestsAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        QuestCouponPlan plan;
        if (abyssTicket)
        {
            var subject = await questFacts.SubjectAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
            var (selected, content) = await abyssTickets.SelectAsync(source.ItemId, subject, now, ct).ConfigureAwait(false);
            plan = await plans.CouponAsync(sourceSlot, source, state, selected, now, ct).ConfigureAwait(false);
            var material = new RecipeAmount(content.MaterialItemId, content.MaterialCount);
            var costs = await ReadMaterialCostsAsync(session, material, now, ct).ConfigureAwait(false);
            plan = plan with { Effects = plan.Effects.InsertRange(0, costs.Items.Select(cost =>
                new ConsumeFromContainerEffect(cost.Slot, cost.ItemId, cost.Count))) };
        }
        else
        {
            var selected = await SelectGradeQuestsAsync(session, source.ItemId, state, now, ct).ConfigureAwait(false);
            if (selected is null || selected.Count == 0)
                throw new OpeningRejectedException(OpeningRejection.InvalidChoice, "No eligible quests remain for this coupon.");
            plan = await plans.CouponAsync(sourceSlot, source, state, selected, now, ct).ConfigureAwait(false);
        }
        var result = await inventory.ApplyAsync(session, plan.Effects, now, footprint, ct).ConfigureAwait(false);
        return new(result.Inventory, plan.Completed);
    }

    private async ValueTask<IReadOnlyList<int>> SelectGradeQuestsAsync(SelectedSessionIdentity session, int itemId,
        QuestStateFacts state, DateTimeOffset now, CancellationToken ct)
    {
        var character = characters.Get(session.CharacterId);
        var identity = await character.IdentityAsync(ct).ConfigureAwait(false);
        var progress = await character.ProgressAsync(ct).ConfigureAwait(false);
        if (identity is null || progress is null || identity.CharacterId != session.CharacterId)
            throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "Character identity or progress unavailable.");
        if (itemId == QuestClearCouponRuleService.SideCouponItemId
            && await conditions.FindAsync(session.CharacterId, ct).ConfigureAwait(false) is not { HardcoreMode: 0 })
            throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "Side quest coupon requires a non-hardcore character.");
        var cleared = state.Cleared.Where(x => x.Mark != 0).Select(x => new ClearedQuest(x.QuestId, x.Mark)).ToArray();
        return await rules.SelectAsync(itemId, progress.Level, identity.Job, identity.GrowType, cleared, now, ct).ConfigureAwait(false);
    }

    private async ValueTask<RecipeEffectCosts> ReadMaterialCostsAsync(SelectedSessionIdentity session,
        RecipeAmount material, DateTimeOffset now, CancellationToken ct)
    {
        var placement = await placements.PlacementForAsync(material.ItemId, ct).ConfigureAwait(false);
        if (placement?.Destination.Space is not { } space || !space.IsCarriedItemSpace())
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Abyss ticket material has no carried inventory space.");
        var bag = bags.GetStore(session.CharacterId, BusinessInventoryOwnership.StoreOf(space)).FindContainer(space);
        OpeningHeldItem[] held = bag is null ? [] : (await bag.GetAllAsync(ct).ConfigureAwait(false))
            .OrderBy(item => item.SlotIndex).Select(item => new OpeningHeldItem(new(space, item.SlotIndex), item)).ToArray();
        return openingPlans.MaterialCosts([material], 1, held, now);
    }
}
