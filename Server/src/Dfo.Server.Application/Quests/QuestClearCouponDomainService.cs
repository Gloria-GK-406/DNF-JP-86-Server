using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Quests;

internal sealed class QuestClearCouponDomainService(
    IOpeningInventoryDomainService inventory,
    ICharacterDomainService characters,
    ICharacterQuestLogDomainService logs,
    IQuestClearCouponRuleService rules,
    IQuestProgressDomainService progress) : IQuestClearCouponDomainService
{
    private const int CouponItemId = 10_006_007;

    public async ValueTask<QuestClearCouponResult> UseAsync(
        SelectedSessionIdentity session,
        BusinessSlot sourceSlot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        // Source validates the live selected character, ownership, usability and expiry before
        // any quest is changed. Settle checks the source again before consuming it.
        var address = sourceSlot;
        var source = await inventory.SourceAsync(session, address, now, cancellationToken)
            .ConfigureAwait(false);
        if (source.ItemId != CouponItemId)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent,
                $"Item {source.ItemId} is not a quest-clear coupon.");

        var character = characters.Get(session.CharacterId);
        var identity = await character.IdentityAsync(cancellationToken).ConfigureAwait(false);
        var characterProgress = await character.ProgressAsync(cancellationToken).ConfigureAwait(false);
        if (identity is null || characterProgress is null || identity.CharacterId != session.CharacterId)
            throw new OpeningRejectedException(OpeningRejection.ItemUnusable,
                "The selected character's current identity or level is unavailable.");

        var log = logs.LogOf(session.AccountId, session.CharacterId);
        var cleared = await log.ClearedAsync(cancellationToken).ConfigureAwait(false);
        var selected = await rules.SelectAsync(source.ItemId, characterProgress.Level, identity.Job,
            identity.GrowType, cleared, now, cancellationToken).ConfigureAwait(false);
        if (selected is null || selected.Count == 0)
            throw new OpeningRejectedException(OpeningRejection.InvalidChoice,
                "No eligible quests remain for this coupon.");

        var completed = await log.CompleteWithoutRewardsAsync(selected, cancellationToken)
            .ConfigureAwait(false);
        if (completed.Count == 0)
            throw new OpeningRejectedException(OpeningRejection.InvalidChoice,
                "The selected quests were already cleared.");

        await progress.ReportAsync(session.AccountId, session.CharacterId,
            [.. completed.Select(id => (QuestProgressFact)new QuestCleared(id))],
            cancellationToken).ConfigureAwait(false);

        var receipt = await inventory.SettleAsync(session,
            [new OpeningSlotConsumption(address, source.ItemId, 1)],
            ImmutableArray<OpeningPreparedGrant>.Empty, now, cancellationToken)
            .ConfigureAwait(false);
        return new QuestClearCouponResult(receipt, completed);
    }
}
