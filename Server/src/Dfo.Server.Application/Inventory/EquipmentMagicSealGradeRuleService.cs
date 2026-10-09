using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentMagicSealGradeRuleService(IEquipmentMagicSealGradeDefinitionSource content,
    IEquipmentQualityDefinitionSource materials, IEquipmentDefinitionSource equipment)
    : IEquipmentMagicSealGradeRuleService
{
    public async ValueTask<EquipmentMagicSealGradeDecision> RollAsync(int equipmentId, int materialId,
        RandomOptionState selected, int randomSeed, CancellationToken ct)
    {
        if (selected.Type == 0) return new(EquipmentQualityRejection.InvalidOption);
        var box = await materials.FindAsync(materialId, ct).ConfigureAwait(false);
        // Highest-quality boxes have no evidenced magic-seal reset semantics yet.
        if (box is null || box.Highest) return new(EquipmentQualityRejection.MaterialUnavailable);
        var item = await equipment.GetEquipmentAsync(equipmentId, ct).ConfigureAwait(false);
        if (item is null || box.AllowedEquipmentTypes is { } allowed && !allowed.Contains(item.EquipmentType)
            || item.ImpossibleContents.Contains("reset random option"))
            return new(EquipmentQualityRejection.UnsupportedContent);
        var definition = await content.FindAsync(equipmentId, ct).ConfigureAwait(false);
        if (definition is null) return new(EquipmentQualityRejection.UnsupportedContent);
        if (!definition.KnownOptions.Contains(selected.Type) || definition.UnmodifiableOptions.Contains(selected.Type))
            return new(EquipmentQualityRejection.UnmodifiableOption);
        var bands = definition.Bands;
        if (bands.Count == 0) return new(EquipmentQualityRejection.UnsupportedContent);
        if (bands.Any(x => x.Minimum < 0 || x.MaximumExclusive > 101 || x.Minimum >= x.MaximumExclusive || x.Weight < 0))
            throw new InvalidDataException("Invalid magic-seal reset range.");
        var total = bands.Sum(x => x.Weight);
        if (total <= 0) return new(EquipmentQualityRejection.UnsupportedContent);
        var random = new Random(randomSeed);
        var roll = random.Next(total);
        foreach (var band in bands)
        {
            roll -= band.Weight;
            if (roll >= 0) continue;
            var grade = checked((byte)random.Next(band.Minimum, band.MaximumExclusive));
            // 70 reset_option writes this percentage into both quality bytes; not a stat magnitude.
            return new(EquipmentQualityRejection.None, new(selected.Type, grade, grade));
        }
        throw new InvalidOperationException("Invalid magic-seal reset weights.");
    }
}
