namespace Dfo.Server.Abstractions.Content;

public sealed record EquipmentQualityMaterialDefinition(bool Highest, IReadOnlySet<string>? AllowedEquipmentTypes);
public interface IEquipmentQualityDefinitionSource
{
    ValueTask<EquipmentQualityMaterialDefinition?> FindAsync(int itemId, CancellationToken ct);
}
