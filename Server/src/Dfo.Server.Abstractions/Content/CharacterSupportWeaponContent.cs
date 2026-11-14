using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Content;

/// <summary>
/// Which careers carry a second weapon, and which weapon sub types they may carry there.
/// </summary>
/// <remarks>
/// The character file declares <c>[support weapon sub type]</c> either before its first
/// <c>[growtype N]</c> block, for every advancement of the job, or inside one, for that advancement.
/// In the 2026-11-13 revised archive only the female swordman's fifth block (grow type 4, 流浪武士)
/// lists a sub type, 5 (beam sword). The knight declares <c>[support weapon use]</c> with an empty
/// list: its second slot holds a shield, which is itself a <c>[support weapon]</c>, not a weapon.
/// <c>[support weapon apply option]</c> scales the second weapon's stats, which the client
/// computes; nothing on this side reads it.
/// </remarks>
public interface ICharacterSupportWeaponDefinitionSource
{
    /// <summary>The weapon sub types this career may wear as a second weapon; empty when it wears none.</summary>
    /// <remarks>Awakening does not change the allowance: the block belongs to the advancement.</remarks>
    ValueTask<IReadOnlySet<int>> SubTypesAsync(CharacterCareer career, CancellationToken cancellationToken);
}
