using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>One reason an award was as large as it was, and how much of it that reason is.</summary>
public readonly record struct ExperienceContribution(
    string Source,
    double Rate,
    uint Amount,
    ExperienceBonusKind Kind = ExperienceBonusKind.Unlisted);

/// <summary>
/// What one award came to, and where the character stands after it.
/// </summary>
/// <param name="Base">What the occasion itself was worth, before anything the character carries.</param>
/// <param name="Contributions">What each entitlement added, named.</param>
/// <param name="Gained">The whole award: the base and every contribution.</param>
/// <param name="LeveledUp">
/// Whether the award took the character past a threshold. Callers act on this rather than compare
/// levels themselves, because what a level change requires -- refreshing what the character's level
/// decides -- is easy to forget and impossible to notice from a number that merely looks right.
/// </param>
public sealed record ExperienceAward(
    uint Base,
    IReadOnlyList<ExperienceContribution> Contributions,
    uint Gained,
    byte Level,
    uint Total,
    bool LeveledUp)
{
    /// <summary>An award of nothing, for an occasion that turned out not to pay.</summary>
    /// <remarks>
    /// Distinct from no award at all, which the verb reports as nothing rather than as an award:
    /// this knows where the character stands and that knows only that there was nobody to ask. A
    /// caller that could not tell them apart would take a placeholder for the character's own
    /// standing and tell the client it was level 1 with no experience.
    /// </remarks>
    public static ExperienceAward Nothing(byte level, uint total) =>
        new(0, [], 0, level, total, false);


    /// <summary>The award as one line, for the log that has to explain it afterwards.</summary>
    /// <remarks>
    /// Experience is a number nobody can check by looking, so an award that cannot say where it
    /// came from can only be investigated by playing the game again. This is what makes a live
    /// session's log usable as evidence.
    /// </remarks>
    public override string ToString() =>
        this.Contributions.Count == 0
            ? $"{this.Gained} (base {this.Base}) -> level {this.Level} at {this.Total}"
            : $"{this.Gained} (base {this.Base} + "
                + string.Join(" + ", this.Contributions.Select(c => $"{c.Source} {c.Amount}"))
                + $") -> level {this.Level} at {this.Total}";
}

/// <summary>What an occasion turned out to be worth to this character.</summary>
/// <param name="Base">What the occasion itself was worth.</param>
/// <param name="Contributions">What each entitlement added on top, named.</param>
public readonly record struct ExperienceValuation(
    uint Base,
    IReadOnlyList<ExperienceContribution> Contributions)
{
    /// <summary>An occasion that turned out to pay nothing.</summary>
    public static ExperienceValuation Nothing { get; } = new(0, []);
}

/// <summary>
/// Adds experience to a character: raising the total, crossing level thresholds, persisting.
/// </summary>
/// <remarks>
/// One implementation for every source of experience. Killing a monster, clearing a dungeon and
/// handing a quest in each work out a different amount, and each then does the same thing with it;
/// a second copy of "the same thing" is how this code already produced a wrong total, when a quest
/// finished twice wrote the same figure both times because its own copy read the character from an
/// entry-time snapshot rather than from the store.
/// <para>
/// It sends nothing. What the client is told about an award, and what a level change requires
/// besides, are the caller's -- levelling has consequences beyond experience, and they belong where
/// the frames are decided rather than inside the arithmetic.
/// </para>
/// </remarks>
public interface ICharacterExperienceDomainService
{
    /// <summary>
    /// How much experience a character must hold to be this level, or null when the table declares
    /// no such level.
    /// </summary>
    /// <remarks>
    /// The same table this service levels against, asked directly. Entry <c>n-1</c> is what a
    /// character must hold to be level <c>n</c>, so reaching level two means holding the first
    /// entry.
    /// </remarks>
    uint? ThresholdForLevel(byte level);

    /// <summary>
    /// Puts the character at that level holding that much experience, and answers whether the row
    /// took the write.
    /// </summary>
    /// <remarks>
    /// The tutorial grants a level outright rather than earning it, so it has nothing to award and
    /// cannot go through <see cref="AwardAsync"/>. It still belongs here: this is where the
    /// character's level and experience are written, and a caller that reached the row itself would
    /// be writing progression from outside the one place that owns it.
    /// <para>
    /// <c>false</c> means the row did not take the write, which is not the same as writing a value
    /// that happened to already be there -- the caller refuses rather than reporting a grant the
    /// store disagrees with.
    /// </para>
    /// </remarks>
    ValueTask<bool> TrySetProgressAsync(
        CharacterId characterId, byte level, uint experience, CancellationToken cancellationToken);

    /// <summary>
    /// Reads where a character stands, asks what the occasion is worth to them, and awards it.
    /// </summary>
    /// <param name="valuation">
    /// What the occasion pays, asked with the character's level as it stands right now.
    /// </param>
    /// <remarks>
    /// The level is handed to the caller rather than taken from it, and that inversion is the whole
    /// point of the shape. What an occasion pays depends on the character's level, and so does
    /// where the award lands; a caller that supplied its own copy of the level could supply a stale
    /// one, which is exactly the defect this replaces -- a quest finished twice wrote the same
    /// total both times because the copy it added to came from an entry-time snapshot. Here the
    /// level is read once, and nobody can price against a different one.
    /// </remarks>
    /// <returns>
    /// What was awarded and where the character now stands, or nothing when there is no such
    /// character to award to.
    /// </returns>
    ValueTask<ExperienceAward?> AwardAsync(
        CharacterId characterId,
        Func<byte, ExperienceValuation> valuation,
        CancellationToken cancellationToken);
}
