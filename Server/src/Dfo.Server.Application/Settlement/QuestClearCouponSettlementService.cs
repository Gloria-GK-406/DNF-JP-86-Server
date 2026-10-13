using Dfo.Server.Abstractions.Settlement;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Settlement;

internal sealed class QuestClearCouponSettlementService(IOpeningInventoryDomainService inventory,
    ICharacterDomainService characters, IConsumableStateDomainService states,
    IQuestClearCouponRuleService rules, ISpecialConsumableEffectPlanRuleService plans) : IQuestClearCouponSettlementDomainService
{
    public async ValueTask<QuestClearCouponResult> UseAsync(SelectedSessionIdentity session,
        BusinessSlot sourceSlot, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        var source = await inventory.SourceAsync(session, sourceSlot, now, ct).ConfigureAwait(false);
        if (source.ItemId != 10_006_007)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Not a quest-clear coupon.");
        var character = characters.Get(session.CharacterId);
        var identity = await character.IdentityAsync(ct).ConfigureAwait(false);
        var progress = await character.ProgressAsync(ct).ConfigureAwait(false);
        if (identity is null || progress is null || identity.CharacterId != session.CharacterId)
            throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "Character identity or progress unavailable.");
        var state = await states.QuestsAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        var cleared = state.Cleared.Where(x => x.Mark != 0).Select(x => new ClearedQuest(x.QuestId, x.Mark)).ToArray();
        var selected = await rules.SelectAsync(source.ItemId, progress.Level, identity.Job, identity.GrowType, cleared, now, ct).ConfigureAwait(false);
        if (selected is null || selected.Count == 0)
            throw new OpeningRejectedException(OpeningRejection.InvalidChoice, "No eligible quests remain for this coupon.");
        var plan = await plans.CouponAsync(sourceSlot, source, state, selected, now, ct).ConfigureAwait(false);
        var result = await inventory.ApplyAsync(session, plan.Effects, now, ct).ConfigureAwait(false);
        return new(result.Inventory, plan.Completed);
    }
}
