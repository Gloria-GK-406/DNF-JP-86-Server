using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record ItemCatalogDefinition(ItemCatalogEntry Entry, OpeningRewardItemDefinition Item, ItemPlacement Placement);

public interface IItemCatalogSelectionRuleService
{
    string SelectionKey { get; }
    bool Allows(ItemCatalogDefinition definition);
}

public interface IItemCatalogDomainService
{
    ValueTask<ItemCatalogPage> SearchAsync(ItemCatalogFilter filter, string? selectionKey, CancellationToken ct);
    ValueTask<ItemCatalogDetails> ItemAsync(int itemId, string? selectionKey, CancellationToken ct);
}

public interface IItemCatalogRuleService
{
    ValueTask<IReadOnlyList<ItemCatalogEntry>> CandidatesAsync(ItemCatalogFilter filter, CancellationToken ct);
    ValueTask<ItemCatalogDefinition> DefinitionAsync(int itemId, CancellationToken ct);
    ValueTask<ItemCatalogDetails> DetailsAsync(ItemCatalogDefinition definition, CancellationToken ct);
    ValueTask<string?> ItemNameAsync(int itemId, CancellationToken ct);
}

public interface IItemEquipmentRuleService
{
    ValueTask<ItemEquipmentCapabilities> CapabilitiesAsync(ItemSnapshot core, CancellationToken ct);
    ValueTask<ItemSnapshot> ConfigureAsync(ItemSnapshot core, ItemEquipmentOptions options, CancellationToken ct);
}
