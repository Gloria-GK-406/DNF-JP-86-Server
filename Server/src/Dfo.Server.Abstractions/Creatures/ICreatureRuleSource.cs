using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.Creatures;

/// <summary>Archive backed rules for one creature equipment template.</summary>
/// <remarks>TemplateId is an equipment item ID; CreatureId is the separate creature script ID.</remarks>
public sealed record CreatureRule(
    int TemplateId,
    int CreatureId,
    int MaxLevel,
    bool? HungerEnabled,
    IReadOnlyList<int> ExperienceThresholds,
    IReadOnlyList<CreatureEvolution> Evolutions,
    int? HatchOutputTemplateId,
    bool IsEgg)
{
    /// <summary>The equipment script's [output index], including non-egg evolution outputs.</summary>
    public int? OutputTemplateId { get; init; }
}

/// <summary>Experience bonus declared on a creature artifact.</summary>
public sealed record CreatureArtifactExperienceRule(
    int TemplateId,
    int RequiredCreatureLevel,
    float BonusPercent);

public interface ICreatureRuleSource
{
    ValueTask<CreatureRule?> GetAsync(int itemTemplateId, CancellationToken cancellationToken);

    ValueTask<CreatureArtifactExperienceRule?> GetArtifactExperienceAsync(
        int itemTemplateId, CancellationToken cancellationToken);
}
