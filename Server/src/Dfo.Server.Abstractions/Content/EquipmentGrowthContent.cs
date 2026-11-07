namespace Dfo.Server.Abstractions.Content;

/// <summary>How a growth group earns experience from one kind of event (<c>[exp acquire]</c>).</summary>
/// <param name="Event">The event name, such as <c>death</c>.</param>
/// <param name="Point"><c>[point]</c>: the base amount per event.</param>
/// <param name="KindRates"><c>[kind]</c>: kind number to percent.</param>
/// <param name="LevelCorrection"><c>[level correction]</c>: six integers whose meaning A21 does not show.</param>
/// <param name="DifficultyCorrection"><c>[difficulty correction]</c>: percent per difficulty, normal first.</param>
public sealed record EquipmentGrowthAcquisition(
    string Event,
    int Point,
    IReadOnlyDictionary<int, int> KindRates,
    IReadOnlyList<int> LevelCorrection,
    IReadOnlyList<int> DifficultyCorrection);

/// <summary>One <c>[penalty]</c> line of a star: <c>fatigue 30 20</c>.</summary>
public sealed record EquipmentGrowthPenalty(string Kind, int Threshold, int Percent);

/// <summary>One <c>[exp base]</c> row.</summary>
/// <param name="MaxExperience">Experience the star holds when full; 0 on the last star.</param>
/// <param name="GrowsByExperience">
/// Third column. The A21 client refuses scrolls on stars where it is set, so those stars advance
/// only by filling up.
/// </param>
public sealed record EquipmentGrowthStar(int Star, int MaxExperience, bool GrowsByExperience,
    IReadOnlyList<EquipmentGrowthPenalty> Penalties);

/// <summary>One <c>[group]</c> of <c>etc/equipment_grow.etc</c>.</summary>
public sealed record EquipmentGrowthGroup(int Group, int MaxLevel,
    IReadOnlyList<EquipmentGrowthAcquisition> Acquisitions, IReadOnlyList<EquipmentGrowthStar> Stars);

public interface IEquipmentGrowthDefinitionSource
{
    /// <summary>Null when the archive declares no such group.</summary>
    ValueTask<EquipmentGrowthGroup?> GetGroupAsync(int group, CancellationToken cancellationToken);
}
