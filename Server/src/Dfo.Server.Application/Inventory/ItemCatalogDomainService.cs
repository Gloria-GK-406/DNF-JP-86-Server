using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemCatalogDomainService(IItemCatalogRuleService rules,
    IEnumerable<IItemCatalogSelectionRuleService> selections) : IItemCatalogDomainService
{
    private readonly IReadOnlyDictionary<string, IItemCatalogSelectionRuleService> policies = selections.ToDictionary(p => p.SelectionKey);
    private IItemCatalogSelectionRuleService? Selection(string? key) => key is null ? null
        : policies.TryGetValue(key, out var rule) ? rule : throw new ArgumentException("物品选择用途无效。");

    public async ValueTask<ItemCatalogPage> SearchAsync(ItemCatalogFilter filter, string? selectionKey, CancellationToken ct)
    {
        var policy = Selection(selectionKey); var candidates = await rules.CandidatesAsync(filter, ct);
        var rows = new List<ItemCatalogEntry>(); var skip = (filter.Page - 1) * filter.PageSize;
        foreach (var entry in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (policy is not null || filter.StackableOnly)
            {
                ItemCatalogDefinition definition;
                try { definition = await rules.DefinitionAsync(entry.ItemId, ct); }
                catch (Exception error) when (error is ArgumentException or KeyNotFoundException) { continue; }
                if (policy is not null && !policy.Allows(definition)
                    || filter.StackableOnly && definition.Placement.WorldClass != ItemWorldClass.Interchangeable) continue;
            }
            if (skip-- > 0) continue;
            if (rows.Count == filter.PageSize) return new(rows, filter.Page, filter.PageSize, true);
            rows.Add(entry);
        }
        return new(rows, filter.Page, filter.PageSize, false);
    }

    public async ValueTask<ItemCatalogDetails> ItemAsync(int itemId, string? selectionKey, CancellationToken ct)
    {
        var policy = Selection(selectionKey); var definition = await rules.DefinitionAsync(itemId, ct);
        if (policy is not null && !policy.Allows(definition)) throw new ArgumentException("此物品不支持当前用途。");
        return await rules.DetailsAsync(definition, ct);
    }
}
