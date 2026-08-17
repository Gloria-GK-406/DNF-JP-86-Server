using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Content;

/// <summary>PVF-owned stats a character of a given job is born with.</summary>
/// <remarks>
/// <para>
/// The archive states a job's stats as three kinds of block: <c>[initial value]</c>, which is
/// what a level-one character has, and <c>[growtype N]</c> / <c>[awakening M]</c>, which are the
/// per-level increments applied on the way up. Only the first is read here, because nothing in
/// the server advances a character's level yet; a level-up path will need the other two, and
/// they live in the same file under the same stat tags.
/// </para>
/// <para>
/// Deriving a stat is arithmetic over these numbers and nothing else, so this source hands back
/// the numbers and leaves any combining to its caller -- the same split
/// <see cref="ISkillPointDefinitionSource"/> uses.
/// </para>
/// </remarks>
public interface ICharacterStatDefinitionSource
{
    /// <summary>The stats a level-one character of <paramref name="job"/> starts with.</summary>
    /// <exception cref="InvalidOperationException">The archive declares no such job.</exception>
    ValueTask<CharacterBaseStats> GetInitialAsync(byte job, CancellationToken cancellationToken);
}
