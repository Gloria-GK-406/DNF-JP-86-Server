using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Abstractions.Content;

public sealed record EquipmentCompoundTemplate(int ItemId, int Rarity, int Level, string Group,
    bool Legacy, string AttachType, int CreationRate, bool GeneratedUnique);

public sealed record EquipmentCompoundLevel(int Level, int SameWeight, int HigherWeight);

public sealed record EquipmentCompoundContent(
    IReadOnlyDictionary<int, IReadOnlySet<string>> Parts,
    IReadOnlySet<int> ExcludedItems,
    IReadOnlyDictionary<int, double> ItemWeights,
    double LegacyWeight,
    IReadOnlyDictionary<(int Rarity, bool Directed), IReadOnlyList<RecipeAmount>> Materials,
    IReadOnlyDictionary<(int Rarity, bool Directed), IReadOnlyDictionary<int, int>> Multipliers,
    IReadOnlyDictionary<int, IReadOnlyList<EquipmentCompoundLevel>> Levels,
    IReadOnlyDictionary<int, EquipmentCompoundTemplate> Templates,
    IReadOnlyDictionary<(int Rarity, int Level), IReadOnlyList<EquipmentCompoundTemplate>> Pools);

public interface IEquipmentCompoundDefinitionSource
{
    EquipmentCompoundContent Load();
}
