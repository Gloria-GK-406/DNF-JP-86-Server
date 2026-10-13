using System.Collections.Frozen;

namespace Dfo.Server.Abstractions.Content;

/// <summary>Equipment template facts, independent of eligibility for any operation.</summary>
public sealed record EquipmentDefinition(
    int MinimumLevel,
    int Grade,
    int Rarity,
    string EquipmentType,
    ushort? MaxDurability)
{
    public string AttachType { get; init; } = string.Empty;
    /// <summary>Normalized raw declarations from <c>[impossible contents]</c>.</summary>
    public IReadOnlySet<string> ImpossibleContents { get; init; } = FrozenSet<string>.Empty;
}

public interface IEquipmentDefinitionSource
{
    /// <summary>Validates the required equipment index without eagerly parsing every template.</summary>
    ValueTask<int> InitializeAsync(CancellationToken cancellationToken);

    /// <summary>Returns null only when the item is absent from the equipment index.</summary>
    ValueTask<EquipmentDefinition?> GetEquipmentAsync(int itemId, CancellationToken cancellationToken);
}
