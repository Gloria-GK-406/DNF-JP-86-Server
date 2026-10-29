using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Quests;

/// <summary>The committed inventory projection and quests newly cleared by one coupon use.</summary>
public sealed record QuestClearCouponResult(
    OpeningInventoryReceipt Inventory,
    IReadOnlyList<int> CompletedQuestIds);

/// <summary>Selects eligible quests from content and current character facts.</summary>
public interface IQuestClearCouponRuleService
{
    /// <summary>Whether the item is the one coupon grade whose quest-clear policy is supported.</summary>
    bool IsCoupon(int itemId);

    ValueTask<IReadOnlyList<int>> SelectAsync(
        int itemId,
        byte level,
        byte job,
        byte growType,
        IReadOnlyList<ClearedQuest> cleared,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
