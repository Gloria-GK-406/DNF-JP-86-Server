namespace Dfo.Server.Abstractions.Content;

/// <summary>One A21 amplifyitem material: Attribute 0 means the caller selects one of four attributes.</summary>
public sealed record EquipmentOutworldMaterialDefinition(byte Attribute, int Count);

public sealed record EquipmentOutworldRollWeight(byte Attribute, int Weight);

/// <summary>Validated A21 outworld/amplify declarations, not a mutable item instance.</summary>
public sealed record EquipmentOutworldTable(
    int MinimumEquipmentLevel,
    IReadOnlyDictionary<int, int> PurifyMaterials,
    IReadOnlyDictionary<int, int> ClearMaterials,
    IReadOnlyDictionary<int, EquipmentOutworldMaterialDefinition> InvestMaterials,
    IReadOnlyDictionary<int, EquipmentOutworldMaterialDefinition> TwistMaterials,
    IReadOnlyDictionary<int, IReadOnlyDictionary<byte, ushort>> InitialValues,
    IReadOnlyList<EquipmentOutworldRollWeight> RollWeights);

public interface IEquipmentOutworldContentSource
{
    ValueTask<EquipmentOutworldTable?> GetAsync(CancellationToken cancellationToken);
}
