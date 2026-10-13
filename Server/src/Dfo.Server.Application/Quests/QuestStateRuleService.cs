using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Quests;

internal sealed class QuestStateRuleService(IQuestContentRuleService content) : IQuestStateRuleService
{
    public QuestHoldingsFacts Holdings(IEnumerable<ItemSnapshot> main, IEnumerable<ItemSnapshot> shared, int gold)
    {
        var quantities = main.Where(x => x.ItemId > 0 && !AccountMaterialCatalog.ContainsItem(x.ItemId))
            .GroupBy(x => x.ItemId).ToDictionary(x => x.Key, x => x.Sum(item => (long)Math.Max(0, item.Count)));
        foreach (var item in shared.Where(x => x.ItemId > 0 && AccountMaterialCatalog.ContainsItem(x.ItemId)))
            quantities[item.ItemId] = Math.Max(0, item.Count);
        quantities[0] = Math.Max(0, gold);
        return new(quantities);
    }

    internal static int ApplyClearedRequirements(QuestDefinition definition, int trigger, IReadOnlySet<int> cleared) =>
        definition.ClearedQuestRequirements.Count == 0 ? trigger
            : unchecked((int)new QuestTrigger(unchecked((uint)trigger)).ReplaceChannel(0,
                definition.ClearedQuestRequirements.Count(id => !cleared.Contains(id))).PackedValue);

    public async ValueTask<IReadOnlyList<AcceptedQuestState>> EvaluateAsync(IReadOnlyList<CarriedQuest> carried,
        IReadOnlyList<ClearedQuest> cleared, QuestHoldingsFacts holdings, CancellationToken ct)
    {
        var clearedIds = cleared.Where(x => x.Mark != 0).Select(x => x.QuestId).ToHashSet();
        var result = new List<AcceptedQuestState>(carried.Count);
        foreach (var row in carried.OrderBy(x => x.Slot))
        {
            var definition = await content.FindAsync(row.QuestId, ct).ConfigureAwait(false);
            var trigger = definition is null ? row.TriggerValue : QuestKillObjectiveRules.RepairRegionalResidual(definition.KillObjectives, row.TriggerValue);
            var items = (definition?.SeekingItems ?? []).Select(x => new QuestItemRequirementState(
                x.ItemId, x.RequiredCount, holdings.Quantities.GetValueOrDefault(x.ItemId))).ToArray();
            if (items.Length > 0)
                trigger = unchecked((int)new QuestTrigger(unchecked((uint)trigger)).ReplaceChannel(0, items.Sum(x => x.Missing)).PackedValue);
            if (definition is not null) trigger = ApplyClearedRequirements(definition, trigger, clearedIds);
            result.Add(new(row with { TriggerValue = trigger }, items));
        }
        return result;
    }
}
