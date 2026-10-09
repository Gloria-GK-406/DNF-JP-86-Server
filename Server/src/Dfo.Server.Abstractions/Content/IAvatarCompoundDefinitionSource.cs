using System.Collections.Immutable;

namespace Dfo.Server.Abstractions.Content;

public sealed record AvatarCompoundWeightedItem(int ItemId, int Weight);
public sealed record AvatarCompoundPart(string EquipmentType, int RareRate, int UpperRate,
    ImmutableArray<AvatarCompoundWeightedItem> Rare, ImmutableArray<AvatarCompoundWeightedItem> Advanced);
public sealed record AvatarCompoundMaterial(byte Category, int ItemId, int Count);
public sealed record AvatarCompoundDefinition(int NormalGrade, int AdvancedGrade, int GoldenBonus,
    ImmutableArray<AvatarCompoundMaterial> Materials, ImmutableArray<AvatarCompoundPart> Parts);
public sealed record AvatarCompoundItemDefinition(int ItemId, string EquipmentType, int Grade,
    ImmutableArray<int> Abilities, bool Permanent, bool Sealed)
{
    public ItemJobRestriction Jobs { get; init; } = ItemJobRestriction.Any;
    public int MinimumLevel { get; init; }
}
public interface IAvatarCompoundDefinitionSource
{
    ValueTask<AvatarCompoundDefinition?> ForJobAsync(byte job, CancellationToken ct);
    ValueTask<AvatarCompoundItemDefinition?> ItemAsync(int itemId, CancellationToken ct);
}
