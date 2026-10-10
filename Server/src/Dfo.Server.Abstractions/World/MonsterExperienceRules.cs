using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

/// <summary>What rank a monster is, in the numbering the client draws it by.</summary>
public enum MonsterRank : byte
{
    Ordinary = 0,
    Champion = 1,
    SuperChampion = 2,
    Boss = 3,
}

/// <summary>Everything about one kill that decides what it is worth.</summary>
/// <remarks>
/// Nothing about who made the kill beyond their level. What a character carries is added on top of
/// this and belongs to a different rule; anyone of the same level killing the same monster in the
/// same run earns the same amount here.
/// </remarks>
public readonly record struct KillCircumstances(
    int MonsterLevel,
    MonsterRank Rank,
    int CharacterLevel,
    DungeonDifficulty Difficulty,
    double DungeonWeight,
    int PartyMemberCount,
    bool IsNamedMonster = false);

/// <summary>
/// What one kill is worth, before anything the killing character carries.
/// </summary>
/// <remarks>
/// Confirmed against the reference server on 2026-08-27, in dungeon 144 at difficulty 0, for four
/// combinations: an ordinary level-5 monster pays 75, an ordinary level-4 monster 65, a champion
/// level-5 153 and a champion level-4 132. Those totals include a channel contribution that is not
/// this rule's; what this rule produces is the 72, 62, 146 and 126 underneath them, which that
/// server's own log recorded step by step.
/// <para>
/// The two roundings are separate and in this order: the shared amount is floored, then the result
/// of the level gap and the division by party size is floored again. Rounding once at the end
/// instead answers 63, 73, 126 and 147 -- wrong on three of the four. It is not enough that the
/// intermediate be fractional, which all four are; the champion level-4 sample stays at 126 either
/// way, and reading the rule off that one alone would leave the order looking like a free choice.
/// </para>
/// <para>
/// <b>The numbers this rule multiplies by come from two places, and the split is the point.</b>
/// The difficulty and party rates and the monster-wide rate are read from the archive and arrive
/// in <see cref="ExperienceParameters"/>; the dungeon's own weight arrives with the run. Those are
/// content. Everything else -- the divisor, the rank rates, the level-gap bands, the curve under
/// them -- is declared nowhere, was chosen by this server, and arrives in
/// <see cref="ExperienceTuning"/>, where each carries how well it is evidenced.
/// </para>
/// </remarks>
public static class MonsterExperienceRules
{
    /// <summary>What a kill of these circumstances is worth.</summary>
    public static uint WorthOf(
        in KillCircumstances kill, ExperienceParameters parameters, ExperienceTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(tuning);
        var partySize = Math.Max(1, kill.PartyMemberCount);

        var worth = MonsterExperienceWorth.For(kill.MonsterLevel, tuning.Curve);
        if (worth == 0)
            return 0;

        var shared = Floor(
            worth / tuning.WorthDivisor
            * parameters.PartyMemberRate(partySize)
            * parameters.DifficultyRate((int)kill.Difficulty)
            * kill.DungeonWeight
            * tuning.RankRate(kill.Rank)
            * (kill.IsNamedMonster ? 3.0 : 1.0)
            * parameters.MonsterOverallRate);

        return Floor(
            shared * tuning.LevelGapRate(kill.CharacterLevel, kill.MonsterLevel) / partySize);
    }

    /// <summary>Whether an actor of this declared type is a monster at all.</summary>
    /// <param name="declaredActorType">The type the room's actor list gives it.</param>
    /// <remarks>
    /// A room's list holds AI characters beside its monsters -- the tutorial's escort among them --
    /// and gives them running numbers from the same sequence, so a companion's death arrives
    /// looking exactly like a kill. Paying for one would pay a character's own level: the escort is
    /// level 80.
    /// <para>
    /// The archive's boundary is disputed by this repository's own readers. Whether a run is clear
    /// skips anything above 3, while the room frame's writer treats 5 and up as companions -- so
    /// type 4 is a monster to one and a companion to the other. This takes the first reading, which
    /// is the one that pays nothing for an actor whose nature is unsettled.
    /// </para>
    /// </remarks>
    public static bool IsMonster(byte declaredActorType) =>
        declaredActorType <= (byte)MonsterRank.Boss;


    private static uint Floor(double value) =>
        value <= 0 || double.IsNaN(value) ? 0
        : value >= uint.MaxValue ? uint.MaxValue
        : (uint)Math.Floor(value);
}
