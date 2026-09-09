using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>Shared, immutable level 1–86 base stats; does not read character state.</summary>
public interface ICharacterStatRuleService
{
    ValueTask InitializeAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<CharacterCareer> SupportedCareers { get; }
    CharacterBaseStats GetStats(CharacterCareer career, int level);
}

