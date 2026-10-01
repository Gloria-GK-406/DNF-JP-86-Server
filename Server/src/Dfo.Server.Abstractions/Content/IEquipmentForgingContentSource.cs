namespace Dfo.Server.Abstractions.Content;

/// <summary>One target forging level from A21 upgrade_separate.etc.</summary>
public sealed record EquipmentForgingLevel(int TargetLevel, int SuccessWeight, double MaterialWeight);

public sealed record EquipmentForgingMaterial(int ItemId, int BaseCount);

public sealed record EquipmentForgingTable(
    int MaxLevel,
    IReadOnlyList<EquipmentForgingLevel> Levels,
    IReadOnlyDictionary<int, EquipmentForgingMaterial> MaterialsByGrade,
    IReadOnlyList<double> ItemWeightsByRarity);

public interface IEquipmentForgingContentSource
{
    ValueTask<EquipmentForgingTable?> GetAsync(CancellationToken cancellationToken);
}
