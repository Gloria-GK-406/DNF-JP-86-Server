using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Quests;

/// <summary>Catch-up policy for the verified no-reward story coupon, not an official-server algorithm.</summary>
internal sealed class QuestClearCouponRuleService(
    IStackableActionDefinitionSource items, IQuestContentRuleService quests) : IQuestClearCouponRuleService
{
    private const int StoryCoupon = 10_006_007;

    public bool IsCoupon(int itemId) => itemId == StoryCoupon;

    public async ValueTask<IReadOnlyList<int>> SelectAsync(int itemId, byte level, byte job,
        byte growType, IReadOnlyList<ClearedQuest> cleared, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Other archives use the same action for different grades and reward policies.
        var item = await items.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (!IsCoupon(itemId) || item is not { IsReadable: true, Action: "[quest clear]" })
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Unsupported quest-clear action or coupon.");
        if (level < Math.Max(1, item.MinimumLevel) || level > item.MaximumLevel
            || item.ExpiresAtUnixSeconds != 0 && now.ToUnixTimeSeconds() >= item.ExpiresAtUnixSeconds)
            throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "Coupon level or expiration limit.");
        return Select(await quests.AllAsync(cancellationToken).ConfigureAwait(false), level, job, growType, cleared);
    }

    internal static IReadOnlyList<int> Select(IReadOnlyList<QuestDefinition> definitions,
        byte level, byte job, byte growType, IReadOnlyList<ClearedQuest> cleared)
    {
        var completed = cleared.Where(q => q.Mark != 0).Select(q => q.QuestId).ToHashSet();
        var family = CharacterJobFamilies.Resolve(job);
        // A catch-up coupon completes eligible stories directly, not by accepting each quest.
        // Dialogue choices and prerequisite branches must not leave old stories behind.
        return definitions.Where(Eligible).Select(q => q.QuestId).Distinct().Order().ToArray();

        bool Eligible(QuestDefinition quest) => quest.QuestId is > 0 and < 30_000
            && !completed.Contains(quest.QuestId)
            && quest.Grade == QuestGrade.Epic
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
