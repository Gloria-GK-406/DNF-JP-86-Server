using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// Where a compensation service will one day raise a character's odds.
/// </summary>
/// <remarks>
/// The seam, and only the seam. What will sit behind it is a service of its own that counts a
/// character's kills, flips and levels since the last piece of gear, reads their warehouse, and
/// decides whether they are owed something -- state that crosses runs, needs persisting, and has
/// consumers other than this one.
/// <para>
/// <b>Only the rate-adjusting half is here.</b> The other thing such a service could do is add a
/// piece outright, and that half is deliberately absent: it changes the shape of the answer, and
/// nothing yet knows what it would add or when, so a seam for it would be a guess with a signature.
/// Raising a rate changes nothing structurally, so a caller written against it today is written
/// against the real thing.
/// </para>
/// <para>
/// An implementation that raises anything must be able to say so afterwards. An unexplained
/// number is worse than a wrong one, which the experience award already learned.
/// </para>
/// </remarks>
public interface IDropRateAdjuster
{
    /// <summary>This character's rates for this occasion, raised if they are owed anything.</summary>
    DropRates Adjust(CharacterId characterId, DropRates rates);
}
