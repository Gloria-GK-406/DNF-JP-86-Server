using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentEnchantRuleService(IEquipmentEnchantContentSource content) : IEquipmentEnchantRuleService
{
    public async ValueTask<int?> CardAsync(ItemSnapshot bead, int equipmentItemId, CancellationToken ct)
    {
        var definition = bead.EnchantUpgradeCount == 0
            ? await content.ResolveAsync(bead.ItemId, equipmentItemId, ct).ConfigureAwait(false)
            : null;
        definition ??= await content.ResolveExpertBeadAsync(bead.ItemId, bead.EnchantUpgradeCount, equipmentItemId, ct)
            .ConfigureAwait(false);
        return definition is not null && definition.BeadItemId == bead.ItemId && definition.CardItemId > 0
            ? definition.CardItemId : null;
    }
}
