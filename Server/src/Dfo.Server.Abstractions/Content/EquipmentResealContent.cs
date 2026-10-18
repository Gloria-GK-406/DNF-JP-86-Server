namespace Dfo.Server.Abstractions.Content;

public sealed record EquipmentResealTable(IReadOnlyList<int> GradeBands,
    IReadOnlyList<int> RarityWeights, IReadOnlyList<int> RepeatWeights, IReadOnlyList<int> AllowedRarities);

public interface IEquipmentResealContentSource
{
    EquipmentResealTable Load();
}
