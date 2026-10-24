using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Settlement;

/// <summary>Uses a task-clear coupon within the caller's transaction.</summary>
public interface IQuestClearCouponSettlementDomainService
{
    ValueTask<QuestClearCouponResult> UseAsync(SelectedSessionIdentity session, BusinessSlot sourceSlot,
        DateTimeOffset now, IEffectFootprintRecorder footprint, CancellationToken cancellationToken);
}
