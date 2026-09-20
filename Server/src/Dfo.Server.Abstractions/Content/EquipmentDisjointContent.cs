namespace Dfo.Server.Abstractions.Content;

/// <summary>The equipment facts the system-disjoint table consumes.</summary>
public sealed record EquipmentDisjointDefinition(
    int SellGold,
    int MinimumLevel,
    int Rarity,
    bool CanDisjoint);

/// <summary>The archive-owned limit carried by one portable disjoint machine template.</summary>
public sealed record PortableDisjointMachineDefinition(
    int Grade,
    int MaximumEquipmentLevel);

public sealed record EquipmentDisjointAdditionalRule(
    double GreatCountDivisor,
    double NormalCountDivisor,
    double GreatChancePercent,
    double NormalChancePercent);

public sealed record EquipmentDisjointExpandRule(
    bool Enabled,
    int ItemId,
    double LevelDivisor,
    double GreatChancePercent,
    double NormalChancePercent);

/// <summary>The parsed contents of <c>etc/disjoint.etc</c>.</summary>
public sealed record EquipmentDisjointTable(
    int CubeItemId,
    double CubeCreationBase,
    IReadOnlyList<double> CubeCreationMultipliers,
    IReadOnlyList<IReadOnlyList<int>> AdditionalResults,
    IReadOnlyList<EquipmentDisjointAdditionalRule> AdditionalRules,
    IReadOnlyList<EquipmentDisjointExpandRule> ExpandRules);

public interface IEquipmentDisjointDefinitionSource
{
    ValueTask<EquipmentDisjointDefinition?> GetEquipmentAsync(
        int itemId, CancellationToken cancellationToken);

    ValueTask<PortableDisjointMachineDefinition?> GetPortableMachineAsync(
        int itemId, CancellationToken cancellationToken);

    ValueTask<EquipmentDisjointTable?> GetSystemTableAsync(CancellationToken cancellationToken);
}
