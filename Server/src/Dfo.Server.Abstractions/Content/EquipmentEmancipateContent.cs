namespace Dfo.Server.Abstractions.Content;

/// <summary>One item and how many of it.</summary>
public readonly record struct EmancipateItemCount(int ItemId, int Count);

/// <summary>Where an equipment template stands on a growth line: <c>[equipment grow] group star</c>.</summary>
public readonly record struct EquipmentGrowthPosition(int Group, int Star);

/// <summary>
/// An equipment template's <c>[emancipate]</c> block: what turning it into its next template takes
/// and yields. Facts only; which ticket may act on it and what the new piece keeps are rules.
/// </summary>
/// <param name="Type"><c>[emancipate type]</c> when declared.</param>
/// <param name="Growth"><c>[equipment grow]</c> when declared (dragon-leap weapons).</param>
/// <param name="Inputs"><c>[input]</c> pairs, in declaration order; empty when the list is not well-formed pairs.</param>
/// <param name="Outputs">
/// <c>[output]</c> pairs, in declaration order; dragon-leap and requiem name exactly one. Empty when
/// the list is not well-formed pairs: some A21 accessories declare a bare <c>1</c> there.
/// </param>
public sealed record EquipmentEmancipateDefinition(
    int? Type,
    EquipmentGrowthPosition? Growth,
    IReadOnlyList<EmancipateItemCount> Inputs,
    IReadOnlyList<EmancipateItemCount> Outputs);

public interface IEquipmentEmancipateDefinitionSource
{
    /// <summary>
    /// The template's <c>[emancipate]</c> blocks in declaration order; empty when the item is not
    /// equipment or declares none. Some A21 accessories declare several.
    /// </summary>
    ValueTask<IReadOnlyList<EquipmentEmancipateDefinition>> GetEquipmentAsync(int itemId, CancellationToken cancellationToken);
}
