using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Content;

// Stat ranges describe template content, never the two quality bytes stored on an item.
public sealed record MagicSealCandidate(byte Type, double MinimumStat, double MaximumStat, int Weight);
public sealed record MagicSealValueBand(int Minimum, int Maximum, int Weight);

public sealed record MagicSealQuantity(int Count, int Weight);

public sealed record EquipmentMagicSealDefinition(
    int ItemId, int Rarity, int MinimumLevel, int UnsealGoldCost, int ChangeGoldCost,
    IReadOnlyList<MagicSealQuantity> Quantities,
    IReadOnlyList<IReadOnlyList<MagicSealCandidate>> InitialGroups,
    IReadOnlyList<IReadOnlyList<MagicSealCandidate>> ChangeGroups,
    IReadOnlyList<MagicSealValueBand> InitialValues,
    IReadOnlyList<MagicSealValueBand> ChangeValues);

public sealed record MagicSealScrollDefinition(int ItemId, int MaximumEquipmentLevel);

public interface IEquipmentMagicSealContentSource
{
    ValueTask<EquipmentMagicSealDefinition?> GetEquipmentAsync(int itemId,
        CancellationToken cancellationToken);

    ValueTask<MagicSealScrollDefinition?> GetScrollAsync(int itemId,
        CancellationToken cancellationToken);
}
