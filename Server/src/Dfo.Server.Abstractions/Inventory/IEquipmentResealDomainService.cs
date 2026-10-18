using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface IEquipmentResealDomainService
{
    /// <summary>Null means this material is not a resealing item; rejections must not fall back to quality reroll.</summary>
    ValueTask<EquipmentQualityResult?> TryResealAsync(SelectedSessionIdentity session, BusinessSlot target,
        int expectedItemId, BusinessSlot material, CancellationToken ct);
}

public sealed record EquipmentResealQuote(EquipmentQualityRejection Rejection, int Cost = 0, byte NextCount = 0);

public interface IEquipmentResealRuleService
{
    bool SupportsMaterial(int itemId);
    ValueTask<EquipmentResealQuote> QuoteAsync(ItemSnapshot equipment, CancellationToken ct);
}
