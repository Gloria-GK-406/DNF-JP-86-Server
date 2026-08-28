using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

/// <summary>
/// The one place experience is added to a character.
/// </summary>
/// <remarks>
/// The character's standing is read from the store at the moment of the award and written back in
/// the same breath, so no caller can price or add against a stale copy. That is not a general
/// principle applied hopefully: it is the specific defect this replaces, where a quest finished
/// twice wrote the same total both times because the copy it added to was the entry-time snapshot.
/// </remarks>
internal sealed class CharacterExperienceGrant(
    ICharacterTable characters,
    IDungeonExperienceProgressionPolicySource progression) : ICharacterExperienceGrant
{
    public async ValueTask<ExperienceAward?> AwardAsync(
        CharacterId characterId,
        Func<byte, ExperienceValuation> valuation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(valuation);

        var progress = await characters.FindProgressAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        // Not "an award of nothing": there is no character here, so there is no standing to report
        // either. Answering with a placeholder level and total would have the caller send those to
        // the client as the character's own.
        if (progress is null)
            return null;

        var level = checked((byte)Math.Clamp(progress.Level, 1, byte.MaxValue));
        var held = checked((uint)Math.Max(0, progress.Experience));

        var (@base, contributions) = valuation(level);
        var gained = @base;
        foreach (var contribution in contributions)
            gained = AddSaturating(gained, contribution.Amount);

        if (gained == 0)
            return ExperienceAward.Nothing(level, held);

        var (nextLevel, total) = this.Advance(level, held, gained);
        if (total != held
            && await characters.SetProgressAsync(characterId, nextLevel, total, cancellationToken)
                .ConfigureAwait(false) != 1)
        {
            // The row did not take the write, so no award happened -- which is not the same as an
            // award of nothing, and callers act differently on the two. Reporting the higher total
            // would tell the client a figure the store disagrees with, and it would go on drawing
            // it until the next entry.
            return null;
        }

        // What is reported is what landed, which at the top of the table is less than what was
        // valued. A character at the ceiling gains nothing however much they earn, and one just
        // below it gains only the room that was left.
        //
        // The distinction is not academic. Every caller reports this award to the client, and two
        // of them draw a breakdown from it: the experience notice declares each contribution and
        // the client works out the base by subtracting them from the gain it computes for itself
        // -- so an award claiming more than the total moved makes that base come out negative. The
        // result screen has the same problem in reverse, reporting experience that never landed.
        var landed = total - held;
        return landed == gained
            ? new ExperienceAward(
                @base, contributions, gained, nextLevel, total, LeveledUp: nextLevel > level)
            // Clamped. The parts can no longer be attributed -- there is no honest way to say
            // which of them the ceiling took -- so what landed is reported whole, as the occasion
            // itself. The total is right and the breakdown is coarse, which is the same trade
            // Unlisted makes elsewhere.
            : new ExperienceAward(
                landed, [], landed, nextLevel, total, LeveledUp: nextLevel > level);
    }

    /// <summary>
    /// Where the character stands after the award, level included.
    /// </summary>
    /// <remarks>
    /// The archive's <c>character/ExpTable.tbl</c> lists what a character must hold to be each
    /// level, so the new level is however many of those the new total has passed. Walked rather
    /// than solved in one step because a single award can cross more than one threshold at low
    /// levels, where the first few are a few hundred apart and one kill in a weighted dungeon can
    /// pay more than that.
    /// <para>
    /// The total saturates at the last threshold rather than growing past it. A character at the
    /// ceiling keeps killing and stays there, which is what the client draws; letting it run on
    /// would leave a total no level in the table accounts for.
    /// </para>
    /// </remarks>
    private (byte Level, uint Total) Advance(byte level, uint held, uint gained)
    {
        var thresholds = progression.Load().LevelThresholds;
        var total = (ulong)held + gained;
        if (thresholds.Count > 0)
            total = Math.Min(total, thresholds[^1]);

        var next = level;
        while (next < thresholds.Count + 1 && total >= thresholds[next - 1])
            next++;
        return (next, checked((uint)total));
    }

    private static uint AddSaturating(uint value, uint delta) =>
        uint.MaxValue - value < delta ? uint.MaxValue : value + delta;
}
