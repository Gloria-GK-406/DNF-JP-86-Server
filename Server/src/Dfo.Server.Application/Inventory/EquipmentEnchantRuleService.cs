using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentEnchantRuleService(IEquipmentEnchantContentSource content) : IEquipmentEnchantRuleService
{
    public async ValueTask<EquipmentEnchantDecision?> ResolveAsync(ItemSnapshot bead, int equipmentItemId, CancellationToken ct)
    {
        var expert = await content.ResolveExpertBeadAsync(bead.ItemId, bead.EnchantUpgradeCount, equipmentItemId, ct)
            .ConfigureAwait(false);
        if (Matches(expert, bead.ItemId)) return new(expert!.CardItemId, bead.EnchantUpgradeCount);

        if (bead.EnchantUpgradeCount != 0) return null;
        var ordinary = await content.ResolveAsync(bead.ItemId, equipmentItemId, ct).ConfigureAwait(false);
        return Matches(ordinary, bead.ItemId) ? new(ordinary!.CardItemId, null) : null;
    }

    private static bool Matches(EquipmentEnchantDefinition? definition, int beadItemId) =>
        definition is { CardItemId: > 0 } && definition.BeadItemId == beadItemId;
}
