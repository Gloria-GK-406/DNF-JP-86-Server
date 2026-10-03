using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentEnchantRuleService(IEquipmentEnchantContentSource content) : IEquipmentEnchantRuleService
{
    public async ValueTask<int?> CardAsync(ItemSnapshot bead, int equipmentItemId, CancellationToken ct)
    {
        // Independent upgraded-bead effects are outside the currently supported enchantment scope.
        if (bead.EnchantUpgradeCount != 0) return null;
        var definition = await content.ResolveAsync(bead.ItemId, equipmentItemId, ct).ConfigureAwait(false);
        return definition is not null && definition.BeadItemId == bead.ItemId && definition.CardItemId > 0
            ? definition.CardItemId : null;
    }
}
