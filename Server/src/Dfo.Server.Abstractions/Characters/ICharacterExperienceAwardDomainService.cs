using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>Experience earned on an occasion: priced for the character's current level and settled as an effect.</summary>
/// <remarks>
/// Above the character rather than on it, because settling goes through the effects and the effects reach
/// the character. Settling an already priced reward stays on <see cref="ICharacter.AddExpAsync(ExperienceBreakdown, CharacterProgressionState?, CancellationToken)"/>.
/// </remarks>
public interface ICharacterExperienceAwardDomainService
{
    /// <summary>Price and settle a reward in the caller's transaction, including max-level overflow.</summary>
    /// <returns>Null when the occasion's character does not exist.</returns>
    ValueTask<ExperienceAward?> AwardAsync(ExperienceCalculationContext source, DateTimeOffset at,
        SelectedSessionIdentity? session, IEffectFootprintRecorder footprint, CancellationToken ct);
}
