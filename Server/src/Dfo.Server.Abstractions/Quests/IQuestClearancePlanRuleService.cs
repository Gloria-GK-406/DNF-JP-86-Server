using Dfo.Server.Abstractions.Effects;

namespace Dfo.Server.Abstractions.Quests;

/// <summary>Plans condition-free clears without executing costs or granting ordinary rewards.</summary>
public interface IQuestClearancePlanRuleService
{
    ValueTask<QuestCouponPlan> BuildAsync(QuestStateFacts state, IReadOnlyList<int> selected, CancellationToken ct);
}
