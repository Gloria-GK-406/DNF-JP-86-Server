namespace Dfo.Server.Abstractions.Creatures;

/// <summary>Immutable inputs for pet behavior. Archive access stays behind this rule boundary.</summary>
public interface ICreatureRuleService
{
    ValueTask<CreatureRule?> GetAsync(int itemTemplateId, CancellationToken cancellationToken);
    ValueTask<CreatureArtifactExperienceRule?> GetArtifactExperienceAsync(int itemTemplateId,
        CancellationToken cancellationToken);
    ValueTask<int> BirthExpirationAsync(int itemTemplateId, DateTimeOffset at,
        CancellationToken cancellationToken);
}
