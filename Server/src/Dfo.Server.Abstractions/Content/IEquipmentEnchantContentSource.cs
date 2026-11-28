namespace Dfo.Server.Abstractions.Content;

/// <summary>Validated A21 bead-to-card mapping for one equipment template.</summary>
public sealed record EquipmentEnchantDefinition(int BeadItemId, int CardItemId);

public interface IEquipmentEnchantContentSource
{
    ValueTask<IReadOnlyList<string>> GuardianGemEffectsAsync(int itemId, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<string>>([]);

    ValueTask<EquipmentEnchantDefinition?> ResolveAsync(
        int beadItemId, int equipmentItemId, CancellationToken cancellationToken);

    ValueTask<bool> ValidateExpertCardAsync(int cardItemId, byte upgradeCount, int? equipmentItemId,
        CancellationToken cancellationToken) => ValueTask.FromResult(false);

    ValueTask<EquipmentEnchantDefinition?> ResolveExpertBeadAsync(int beadItemId, byte upgradeCount,
        int? equipmentItemId, CancellationToken cancellationToken) => ValueTask.FromResult<EquipmentEnchantDefinition?>(null);
}
