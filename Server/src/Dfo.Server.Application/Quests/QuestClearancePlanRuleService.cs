using System.Collections.Immutable;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Quests;

internal sealed class QuestClearancePlanRuleService(IQuestContentRuleService quests) : IQuestClearancePlanRuleService
{
    public async ValueTask<QuestCouponPlan> BuildAsync(QuestStateFacts state, IReadOnlyList<int> selected, CancellationToken ct)
    {
        var ids = selected.ToHashSet();
        var marks = state.Cleared.ToDictionary(x => x.QuestId, x => x.Mark);
        var completed = ids.Where(id => marks.GetValueOrDefault(id) == 0).Order().ToImmutableArray();
        if (completed.Length == 0) throw new OpeningRejectedException(OpeningRejection.InvalidChoice, "No eligible quests remain.");
        var effects = ImmutableArray.CreateBuilder<DomainEffect>();
        foreach (var row in state.Carried.Where(x => ids.Contains(x.QuestId))) effects.Add(new SetCarriedQuestEffect(row, null));
        foreach (var row in state.Tracked.Where(x => ids.Contains(x.QuestId))) effects.Add(new RemoveTrackedQuestEffect(row));
        foreach (var id in completed) effects.Add(new SetClearedQuestEffect(id, marks.TryGetValue(id, out var mark) ? mark : null, IQuestLog.ClearedMark));
        var newly = completed.ToHashSet();
        var clearedAfter = state.Cleared.Where(x => x.Mark != 0).Select(x => x.QuestId).Concat(completed).ToHashSet();
        foreach (var row in state.Carried.Where(x => !ids.Contains(x.QuestId)))
        {
            var definition = await quests.FindAsync(row.QuestId, ct).ConfigureAwait(false);
            if (definition?.ClearedQuestRequirements is not { Count: > 0 } requirements || !requirements.Any(newly.Contains)) continue;
            var trigger = QuestStateRuleService.ApplyClearedRequirements(definition, row.TriggerValue, clearedAfter);
            if (trigger != row.TriggerValue) effects.Add(new SetCarriedQuestEffect(row, row with { TriggerValue = trigger }));
        }
        return new(completed, effects.ToImmutable());
    }
}
