using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Content;

/// <summary>Complete archive declarations, preserving raw precision until the final projection.</summary>
public interface ICharacterStatDefinitionSource
{
    ValueTask<IReadOnlyList<CharacterStatDefinition>> GetAllAsync(CancellationToken cancellationToken);
    ValueTask<CharacterBaseStats> GetInitialAsync(byte job, CancellationToken cancellationToken);
}

public sealed record CharacterStatDefinition(
    byte Job,
    CharacterStatValues Initial,
    IReadOnlyDictionary<CharacterCareer, CharacterStatValues> Growth);
