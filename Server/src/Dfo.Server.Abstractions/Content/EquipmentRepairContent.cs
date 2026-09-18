namespace Dfo.Server.Abstractions.Content;

public sealed record EquipmentRepairDefinition(int RepairPrice, int Grade, ushort MaxDurability);

public interface IEquipmentRepairDefinitionSource
{
    ValueTask<EquipmentRepairDefinition?> GetAsync(int itemId, CancellationToken cancellationToken);
    ValueTask<(float CostRate, IReadOnlyList<float> UpgradeRates)?> RatesAsync(CancellationToken cancellationToken);
}
