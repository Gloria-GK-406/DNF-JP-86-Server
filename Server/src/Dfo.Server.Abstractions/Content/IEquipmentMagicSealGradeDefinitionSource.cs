namespace Dfo.Server.Abstractions.Content;

/// <summary>PVF reset ranges use an inclusive lower bound and exclusive upper bound.</summary>
public sealed record MagicSealGradeBand(int Minimum, int MaximumExclusive, int Weight);
public sealed record EquipmentMagicSealGradeDefinition(IReadOnlyList<MagicSealGradeBand> Bands,
    IReadOnlySet<byte> KnownOptions, IReadOnlySet<byte> UnmodifiableOptions);

public interface IEquipmentMagicSealGradeDefinitionSource
{
    ValueTask<EquipmentMagicSealGradeDefinition?> FindAsync(int equipmentId, CancellationToken ct);
}
