using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Content;

public sealed record MagicSealCandidate(RandomOptionState Option, int Weight);

public sealed record MagicSealQuantity(int Count, int Weight);

public sealed record EquipmentMagicSealDefinition(
    int ItemId, int Rarity, int MinimumLevel, int UnsealGoldCost, int ChangeGoldCost,
    IReadOnlyList<MagicSealQuantity> Quantities,
    IReadOnlyList<IReadOnlyList<MagicSealCandidate>> InitialGroups,
    IReadOnlyList<IReadOnlyList<MagicSealCandidate>> ChangeGroups);

public sealed record MagicSealScrollDefinition(int ItemId, int MaximumEquipmentLevel);

public interface IEquipmentMagicSealContentSource
{
    ValueTask<EquipmentMagicSealDefinition?> GetEquipmentAsync(int itemId,
        CancellationToken cancellationToken);

    ValueTask<MagicSealScrollDefinition?> GetScrollAsync(int itemId,
        CancellationToken cancellationToken);
}
