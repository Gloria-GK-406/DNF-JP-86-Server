using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>
/// One character's fatigue: what a day gives them, what is spent of it, and what is left.
/// </summary>
/// <param name="Total">
/// Everything the character may spend before the daily reset. The pair of this and
/// <paramref name="Used"/> is what the entry acknowledgement carries -- a live A21 session was
/// measured sending 188 there
/// (<c>protocol/game/client-to-server/0x0004.md</c>, offsets 13 and 15).
/// </param>
/// <param name="Used">How much of the total is already spent.</param>
/// <remarks>
/// Not <see cref="CharacterFatigueState"/>, and deliberately beside it rather than instead of it:
/// that record is the shape the entry frame carries and lives in the data layer with the rest of
/// the character's entry state. This is the answer a caller asks a service for, and it answers the
/// third number -- what is left -- which the frame has no field for and every spender needs.
/// </remarks>
public readonly record struct CharacterFatigueBalance(ushort Total, ushort Used)
{
    /// <summary>What is left to spend.</summary>
    /// <remarks>
    /// Floored at zero. Both fields are unsigned, so a used count past the total would otherwise
    /// wrap to a very large remainder -- the one arithmetic mistake this number can make, and the
    /// one that would read as an unlimited allowance rather than as an exhausted one.
    /// </remarks>
    public ushort Remaining =>
        this.Used >= this.Total ? (ushort)0 : (ushort)(this.Total - this.Used);
}

/// <summary>
/// A character's fatigue, and the two directions a day's play moves it.
/// </summary>
/// <remarks>
/// <b>A seam ahead of the system behind it: fatigue is not implemented.</b> The only implementation
/// registered today is <c>UnaccountedCharacterFatigueDomainService</c>, which answers a fixed pair
/// and keeps no account of anything reported to it. It is here so that the places that spend
/// fatigue and the places that give it back can be found and called while the system that would
/// record them does not exist, and so that building that system is one implementation rather than
/// a search for the callers it never had.
/// <para>
/// The rules it will keep are written down and none of them are kept here: a point charged for
/// each newly entered room that holds monsters, nothing charged for walking back into a cleared
/// one, a floor of zero, and no entry at all once it reaches zero
/// (<c>Docs/DungeonEntryNotes.md</c> section 8, dictated 2026-08-27). The day's total is 156 with
/// a further 32 for a black diamond, recovered by a fatigue potion and reset daily -- the same
/// section, from the same sitting; 156 + 32 is the 188 the measured session sends. What time of
/// day that reset happens is not written down anywhere, there or here.
/// </para>
/// <para>
/// Neither verb asks permission. Whether a character has enough to enter is a separate question
/// with its own refusal on the wire (<c>SelectDungeonRefusal.InsufficientFatigue</c>), and nothing
/// asks it yet; a report that could be refused would be a gate, and a gate that always says yes is
/// the kind of thing that is mistaken for a working one.
/// </para>
/// </remarks>
public interface ICharacterFatigueDomainService
{
    /// <summary>What this character has, has spent, and has left.</summary>
    ValueTask<CharacterFatigueBalance> BalanceOfAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>Reports that this character spent that many points.</summary>
    ValueTask SpendAsync(
        CharacterId characterId, ushort points, CancellationToken cancellationToken);

    /// <summary>Reports that this character got that many points back.</summary>
    /// <remarks>
    /// The other direction has a caller waiting rather than a hypothetical one: a fatigue potion
    /// returns points already spent. Nothing drinks one yet, which is why it is declared now --
    /// a seam that only counted downwards would have to be reopened for the first drink.
    /// </remarks>
    ValueTask RefundAsync(
        CharacterId characterId, ushort points, CancellationToken cancellationToken);
}
