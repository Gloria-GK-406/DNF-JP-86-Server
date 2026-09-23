namespace Dfo.Server.Abstractions.World;

/// <summary>
/// What an occasion drops: the odds it carries, and what those odds actually yield.
/// </summary>
/// <remarks>
/// Two questions rather than one, and the seam between them is deliberate. The rates are what the
/// archive's tables say this kill is worth before anything a character is owed; the draw is what
/// those rates produce once the seed is applied. Between the two sits the compensation seam, which
/// raises a character's odds from state that crosses runs — state these rules may not read and do
/// not hold. Handing the adjusted rates back in is what keeps that so.
/// <para>
/// The draw is deterministic in its inputs. The same kill of the same monster in the same room of
/// the same run draws the same list, because the seed is derived from exactly those things: nothing
/// is read from the clock and nothing comes from a shared generator.
/// </para>
/// </remarks>
public interface IDropRuleService
{
    /// <summary>
    /// The rates this kill carries, or null when the rate table declares no band covering the
    /// monster's level.
    /// </summary>
    /// <remarks>
    /// Null rather than empty rates, because the two mean different things: a monster at a level
    /// the table does not reach is a gap in the reading, and a caller has a warning to write about
    /// it. Rates that are simply all zero are an ordinary answer.
    /// </remarks>
    DropRates? RatesFor(MonsterDropRequest kill);

    /// <summary>What this kill draws at these rates.</summary>
    /// <param name="rates">
    /// The rates to draw at, which the caller may have had raised on the character's behalf. Passed
    /// in rather than fetched, so that whatever raised them stays outside the rules.
    /// </param>
    IReadOnlyList<RolledDrop> DrawFor(MonsterDropRequest kill, DropRates rates);
}
