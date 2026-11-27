namespace Dfo.Server.Abstractions.Content;

/// <summary>Validated A21 bead-to-card mapping for one equipment template.</summary>
public sealed record EquipmentEnchantDefinition(int BeadItemId, int CardItemId);

public interface IEquipmentEnchantContentSource
{
    ValueTask<IReadOnlyList<string>> GuardianGemEffectsAsync(int itemId, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<string>>([]);

    ValueTask<EquipmentEnchantDefinition?> ResolveAsync(
        int beadItemId, int equipmentItemId, CancellationToken cancellationToken);
}
