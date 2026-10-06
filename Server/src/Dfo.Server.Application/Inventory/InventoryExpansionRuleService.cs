using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class InventoryExpansionRuleService(IInventoryExpansionDefinitionSource content) : IInventoryExpansionRuleService
{
    public ValueTask<AccountCargoDefinition> AccountCargoAsync(CancellationToken ct) => content.AccountCargoAsync(ct);
    public ValueTask<InventoryExpansionGrant?> ItemAsync(int itemId, CancellationToken ct) => content.ItemAsync(itemId, ct);
    public AccountCargoTier? NextTier(AccountCargoDefinition definition, int current) => Next(definition, current);
    public bool CanUpgrade(InventoryExpansionGrant grant, int current, AccountCargoDefinition account, bool purchase) => grant.Kind switch
    {
        InventoryExpansionKind.PersonalCargo => (current == 0 || current is >= 8 and <= 200 && (current - 8) % 16 == 0)
            && grant.Parameter is >= 24 and <= 200 && (grant.Parameter - 8) % 16 == 0
            && (purchase ? grant.Parameter == Math.Max(current, 8) + 16 : grant.Parameter > Math.Max(current, 8)),
        InventoryExpansionKind.Main => current is 0 or 8 or 16 && grant.Parameter == current + 8 && grant.Parameter <= 24,
        InventoryExpansionKind.Avatar => current is >= 0 and < 105 && current % 7 == 0 && grant.Parameter == current + 7,
        InventoryExpansionKind.AccountCargo => Next(account, current) is { } next && next.Capacity == grant.Parameter
            && (!purchase || next.ProductId > 0 && next.ProductId == grant.ProductId),
        _ => false,
    };
    internal static AccountCargoTier? Next(AccountCargoDefinition definition, int current)
    {
        if (current == 0) return definition.Tiers.FirstOrDefault();
        for (var i = 0; i + 1 < definition.Tiers.Count; i++)
            if (definition.Tiers[i].Capacity == current) return definition.Tiers[i + 1];
        return null;
    }
}
