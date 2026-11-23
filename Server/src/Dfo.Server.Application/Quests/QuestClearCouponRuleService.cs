using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Quests;

/// <summary>Quest catch-up rules for the supported story and side quest coupons.</summary>
internal sealed class QuestClearCouponRuleService(
    IStackableActionDefinitionSource items, IQuestContentRuleService quests) : IQuestClearCouponRuleService
{
    private const int StoryCoupon = 10_006_007;
    internal const int SideCouponItemId = 10_157_196;

    public bool IsCoupon(int itemId) => itemId is StoryCoupon or SideCouponItemId;

    public async ValueTask<IReadOnlyList<int>> SelectAsync(int itemId, byte level, byte job,
        byte growType, int? hardcoreMode, IReadOnlyList<ClearedQuest> cleared, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (itemId == SideCouponItemId && hardcoreMode is not 0)
            throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "Side quest coupon requires a non-hardcore character.");

        // Other archives use the same action for different grades and reward policies.
        var item = await items.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (!IsCoupon(itemId) || item is not { IsReadable: true, Action: "[quest clear]" })
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Unsupported quest-clear action or coupon.");
        if (level < Math.Max(1, item.MinimumLevel) || level > item.MaximumLevel
            || item.ExpiresAtUnixSeconds != 0 && now.ToUnixTimeSeconds() >= item.ExpiresAtUnixSeconds)
            throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "Coupon level or expiration limit.");
        var grade = itemId == SideCouponItemId ? QuestGrade.Side : QuestGrade.Epic;
        return Select(await quests.AllAsync(cancellationToken).ConfigureAwait(false), level, job, growType, cleared, grade);
    }

    internal static IReadOnlyList<int> Select(IReadOnlyList<QuestDefinition> definitions,
        byte level, byte job, byte growType, IReadOnlyList<ClearedQuest> cleared, QuestGrade grade = QuestGrade.Epic)
    {
        var completed = cleared.Where(q => q.Mark != 0).Select(q => q.QuestId).ToHashSet();
        var family = CharacterJobFamilies.Resolve(job);
        var side = grade == QuestGrade.Side;
        // Grade tickets catch up all qualifying stages in one pass, including stages with
        // prerequisites inside that grade. Quest logs still handle ordinary acceptance separately.
        return definitions.Where(Eligible).Select(q => q.QuestId).Distinct().Order().ToArray();

        bool Eligible(QuestDefinition quest) => quest.QuestId is > 0 and < 30_000
            && !completed.Contains(quest.QuestId)
            && quest.Grade == grade
            && !QuestAvailabilityRuleService.MayBeExpertJobQuest(quest)
            && (!side || !quest.ObjectiveTypeCode.Equals("play only", StringComparison.OrdinalIgnoreCase))
            && quest.RequiredLevel is { Minimum: >= 0 } range && range.Minimum <= level
            // Old offer-level ceilings do not exclude historical stories.
            && range.Maximum >= range.Minimum
            && (quest.JobRestriction is null || quest.JobRestriction.IsValid
                && (quest.JobRestriction.IsExactAll || quest.JobRestriction.JobFamilies.Contains(family)))
            && (quest.TargetCharacterRestriction is null || quest.TargetCharacterRestriction.IsValid
                && quest.TargetCharacterRestriction.JobFamilies.Contains(family))
            && (quest.GrowTypeRequirement is null || quest.GrowTypeRequirement == growType);
    }

}
