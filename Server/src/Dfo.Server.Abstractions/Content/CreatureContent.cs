namespace Dfo.Server.Abstractions.Content;

/// <summary>What a creature must reach and complete before it can evolve.</summary>
/// <param name="RequiredLevel">Level the creature must reach, or null when unstated.</param>
/// <param name="RequiredQuestId">Quest that unlocks evolution, or null when unstated.</param>
/// <param name="EvolvesIntoCreatureId">The creature it becomes.</param>
public readonly record struct CreatureEvolution(
    int? RequiredLevel,
    int? RequiredQuestId,
    int EvolvesIntoCreatureId);

/// <summary>
/// A creature as the content defines it.
/// </summary>
/// <remarks>
/// Ambient lines are the greetings the client shows when a creature is in town. They come
/// from a companion script file, which only some creatures have; an empty list means the
/// content supplies none, which is why the entry packet for them is skipped rather than
/// padded.
/// </remarks>
/// <param name="Name">
/// The creature's display name, or empty. Some content declares an empty name, so this is
/// passed through rather than substituted.
/// </param>
/// <param name="Evolutions">
/// Evolution steps that lead somewhere. The content declares the evolution tags on
/// creatures that do not evolve, using zero as a placeholder; those are not steps and do
/// not appear here.
/// </param>
public sealed record CreatureDefinition(
    int CreatureId,
    string Name,
    int StartLevel,
    int PermissionLevel,
    int MaximumLevel,
    IReadOnlyList<string> ArtifactSlots,
    IReadOnlyList<CreatureEvolution> Evolutions,
    IReadOnlyList<string> AmbientLines)
{
    /// <summary>True when the content supplies at least one town greeting.</summary>
    public bool HasAmbientLines => AmbientLines.Count > 0;
}

/// <summary>Static creature definitions.</summary>
public interface ICreatureDefinitionSource
{
    /// <summary>Every creature the content defines, in list order.</summary>
    ValueTask<IReadOnlyList<CreatureDefinition>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>One creature, or null when the content does not define it.</summary>
    ValueTask<CreatureDefinition?> GetAsync(int creatureId, CancellationToken cancellationToken);

    /// <summary>
    /// The creature a piece of equipment denotes, or null when it denotes none.
    /// </summary>
    /// <remarks>
    /// Equipment and creatures are numbered in disjoint spaces — the creature list runs to
    /// about twenty thousand, creature equipment starts above sixty-three thousand, and no
    /// identifier appears in both. An equipped creature is therefore an equipment
    /// identifier, and turning it into a creature means going through the equipment
    /// definition and matching on the file it names, exactly as the legacy resolver does.
    /// Passing an equipment identifier to <see cref="GetAsync"/> silently finds nothing.
    /// </remarks>
    /// <param name="itemTemplateId">The equipment identifier held in the creature slot.</param>
    ValueTask<CreatureDefinition?> FindByEquipmentAsync(
        int itemTemplateId,
        CancellationToken cancellationToken);
}
