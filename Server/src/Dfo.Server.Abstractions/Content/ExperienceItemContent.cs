namespace Dfo.Server.Abstractions.Content;

/// <summary>How a stackable item declares the experience it grants.</summary>
public enum ExperienceItemGrantKind
{
    Fixed = 0,
    PercentOfCurrentLevel = 1,
}

/// <summary>The archive facts needed to apply one experience-granting stackable.</summary>
public sealed record ExperienceItemDefinition(
    ExperienceItemGrantKind GrantKind,
    uint Value,
    int MinimumLevel,
    int MaximumLevel,
    bool IsReadable);

/// <summary>Reads experience item declarations without knowing any character state.</summary>
public interface IExperienceItemDefinitionSource
{
    /// <summary>
    /// Returns null when the item declares no supported experience effect. A recognized but
    /// malformed experience declaration is returned with <see cref="ExperienceItemDefinition.IsReadable"/>
    /// false so it cannot fall through to the generic consume-only path.
    /// </summary>
    ValueTask<ExperienceItemDefinition?> GetAsync(int itemId, CancellationToken cancellationToken);
}
