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
    ICharacterConditionTable conditions) : IQuestClearCouponSettlementDomainService
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
        IReadOnlyList<int> selected;
        if (abyssTicket)
        {
            var subject = await questFacts.SubjectAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
            selected = await abyssTickets.SelectAsync(source.ItemId, subject, now, ct).ConfigureAwait(false);
        }
        else
        {
            selected = await SelectGradeQuestsAsync(session, source.ItemId, state, now, ct).ConfigureAwait(false);
            if (selected is null || selected.Count == 0)
                throw new OpeningRejectedException(OpeningRejection.InvalidChoice, "No eligible quests remain for this coupon.");
        }
        var plan = await plans.CouponAsync(sourceSlot, source, state, selected, now, ct).ConfigureAwait(false);
        var result = await inventory.ApplyAsync(session, plan.Effects, now, footprint, ct).ConfigureAwait(false);
        return new(result.Inventory, plan.Completed,
            plan.Effects.Any(effect => effect is SetClearedQuestEffect { GrantsPermanentAttributes: true }));
    }

    private async ValueTask<IReadOnlyList<int>> SelectGradeQuestsAsync(SelectedSessionIdentity session, int itemId,
        QuestStateFacts state, DateTimeOffset now, CancellationToken ct)
    {
        var character = characters.Get(session.CharacterId);
        var identity = await character.IdentityAsync(ct).ConfigureAwait(false);
        var progress = await character.ProgressAsync(ct).ConfigureAwait(false);
        if (identity is null || progress is null || identity.CharacterId != session.CharacterId)
            throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "Character identity or progress unavailable.");
        var condition = await conditions.FindAsync(session.CharacterId, ct).ConfigureAwait(false);
        var cleared = state.Cleared.Where(x => x.Mark != 0).Select(x => new ClearedQuest(x.QuestId, x.Mark)).ToArray();
        return await rules.SelectAsync(itemId, progress.Level, identity.Job, identity.GrowType, condition?.HardcoreMode,
            cleared, now, ct).ConfigureAwait(false);
    }
}
