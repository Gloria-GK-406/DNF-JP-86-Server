using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentResealRuleService(IEquipmentResealContentSource content,
    IEquipmentDefinitionSource equipment) : IEquipmentResealRuleService
{
    // A21's built-in ordinary wax. Same-name boosters and one-shot wax are different operations.
    public bool SupportsMaterial(int itemId) => itemId == 14;
    public async ValueTask<EquipmentResealQuote> QuoteAsync(ItemSnapshot item, CancellationToken ct)
    {
        if (item.SealFlag != 0) return new(EquipmentQualityRejection.AlreadySealed);
        if (item.ResealCount >= 7) return new(EquipmentQualityRejection.ResealLimitReached);
        var definition = await equipment.GetEquipmentAsync(item.ItemId, ct).ConfigureAwait(false);
        var table = content.Load();
        if (definition is null || definition.AttachType != "sealing" || definition.Grade is < 1 or > 99
            || !table.AllowedRarities.Contains(definition.Rarity)
            || definition.EquipmentType is not ("weapon" or "coat" or "shoulder" or "pants" or "shoes"
                or "waist" or "amulet" or "wrist" or "ring" or "support" or "magic stone")
            || item.TradeRestriction != 0 || definition.ImpossibleContents.Overlaps(["seal", "reseal", "sealing"]))
            return new(EquipmentQualityRejection.UnsupportedContent);
        var band = Math.Min((definition.Grade - 1) / 10, table.GradeBands.Count - 1);
        var cost = checked(table.GradeBands[band] * table.RarityWeights[definition.Rarity]
            + item.ResealCount * table.RepeatWeights[definition.Rarity]);
        return new(EquipmentQualityRejection.None, cost, checked((byte)(item.ResealCount + 1)));
    }
}
