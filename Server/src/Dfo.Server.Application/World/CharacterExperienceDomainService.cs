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
internal sealed class CharacterExperienceDomainService(
    ICharacterTable characters,
    IExperienceRuleService experienceRules) : ICharacterExperienceDomainService
{
    public uint? ThresholdForLevel(byte level)
    {
        var thresholds = experienceRules.Progression().LevelThresholds;
        return level < 2 || thresholds.Count < level - 1 ? null : thresholds[level - 2];
    }

    public async ValueTask<bool> TrySetProgressAsync(
        CharacterId characterId, byte level, uint experience, CancellationToken cancellationToken) =>
        await characters.SetProgressAsync(characterId, level, experience, cancellationToken)
            .ConfigureAwait(false) == 1;

    public async ValueTask<ExperienceAward?> AwardAsync(
        CharacterId characterId,
        Func<byte, ValueTask<ExperienceBreakdown>> valuation,
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

        var breakdown = await valuation(level).ConfigureAwait(false);
        var gained = breakdown.Total;

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

        // Earning experience and advancing progression are independent results.
        return new ExperienceAward(breakdown, nextLevel, total, LeveledUp: nextLevel > level);
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
        var thresholds = experienceRules.Progression().LevelThresholds;
        var total = (ulong)held + gained;
        if (thresholds.Count > 0)
            total = Math.Min(total, thresholds[^1]);

        var next = level;
        while (next < thresholds.Count + 1 && total >= thresholds[next - 1])
            next++;
        return (next, checked((uint)total));
    }

}
