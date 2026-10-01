using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Quests;

/// <summary>The committed inventory projection and quests newly cleared by one coupon use.</summary>
public sealed record QuestClearCouponResult(
    OpeningInventoryReceipt Inventory,
    IReadOnlyList<int> CompletedQuestIds);

/// <summary>Uses a task-clear coupon within the caller's transaction.</summary>
public interface IQuestClearCouponDomainService
{
    ValueTask<QuestClearCouponResult> UseAsync(
        SelectedSessionIdentity session,
        BusinessSlot sourceSlot,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

/// <summary>Selects eligible quests from content and current character facts.</summary>
public interface IQuestClearCouponRuleService
{
    ValueTask<IReadOnlyList<int>> SelectAsync(
        int itemId,
        byte level,
        byte job,
        byte growType,
        IReadOnlyList<ClearedQuest> cleared,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
