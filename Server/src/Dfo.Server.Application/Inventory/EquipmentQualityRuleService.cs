using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Application.Inventory;

internal static class EquipmentQualitySeeds
{
    // A21 fixed attribute seeds (RVA 026430B0); they must not occur as ordinary random rolls.
    public static bool IsFixed(int seed) => seed is 999999993 or 999999995 or 999999996 or 999999998 or 999999999;
    public static int NewRandom()
    {
        int seed;
        do { seed = Random.Shared.Next(1, int.MaxValue); } while (IsFixed(seed));
        return seed;
    }
}

internal sealed class EquipmentQualitySeedSource : IEquipmentQualitySeedSource
{
    public int Next() => EquipmentQualitySeeds.NewRandom();
}

internal sealed class EquipmentQualityRuleService(IEquipmentDefinitionSource equipment,
    IEquipmentQualityDefinitionSource materials) : IEquipmentQualityRuleService
{
    public async ValueTask<int?> RollAsync(int equipmentId, int materialId, int randomSeed, CancellationToken ct)
    {
        if (randomSeed <= 0 || EquipmentQualitySeeds.IsFixed(randomSeed)) throw new ArgumentOutOfRangeException(nameof(randomSeed));
        var box = await materials.FindAsync(materialId, ct).ConfigureAwait(false);
        if (box is null) return null;
        var definition = await equipment.GetEquipmentAsync(equipmentId, ct).ConfigureAwait(false);
        if (definition is null || definition.EquipmentType is not ("weapon" or "title name" or "coat" or "shoulder"
            or "pants" or "shoes" or "waist" or "amulet" or "wrist" or "ring" or "support" or "magic stone")
            || box.AllowedEquipmentTypes is { } allowed && !allowed.Contains(definition.EquipmentType)
            || definition.ImpossibleContents.Contains("kaleido") || definition.ImpossibleContents.Contains("reset item attr"))
            return null;
        // A21 classifies this reserved seed as band 4 and uses fixed attribute values.
        return box.Highest ? 999999998 : randomSeed;
    }
}
