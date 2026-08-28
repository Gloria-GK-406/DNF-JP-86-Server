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
    byte Difficulty,
    double DungeonWeight,
    int PartyMemberCount);

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
/// <b>Two of this rule's numbers rest on the reference server alone</b>, and are marked where they
/// are written: <see cref="MonsterWorthDivisor"/>, which no evidence can separate from the worth
/// table it divides, and all but one band of <see cref="LevelGapRate"/>. A third,
/// <see cref="RankRates"/>, is evidenced for its first two entries only.
/// </para>
/// </remarks>
public static class MonsterExperienceRules
{
    /// <summary>
    /// What each rank multiplies a kill by.
    /// </summary>
    /// <remarks>
    /// <b>Carried from the reference server, and only half evidenced.</b> Its own log names these
    /// four as a fallback it uses when a dungeon declares nothing of its own, and the archive has
    /// no declaration this server has found. The captures cover the first two -- an ordinary
    /// monster at 1 and a champion at 2 -- and say nothing about the rest.
    /// </remarks>
    private static readonly double[] RankRates = [1.0, 2.0, 3.0, 4.0];

    /// <summary>
    /// What a monster's own worth is divided by before anything else is applied to it.
    /// </summary>
    /// <remarks>
    /// <b>Carried from the reference server, and not independently checkable.</b> It cannot be
    /// separated from <see cref="MonsterExperienceWorth"/>'s own calibration by any evidence in
    /// hand: halving the worth and doubling that table give the same four captured awards, so the
    /// captures constrain the product and say nothing about either factor. It is transcribed
    /// because the two halves were transcribed together and agree with each other, not because
    /// anything measured says a kill pays half of what its level is worth.
    /// <para>
    /// The consequence for a reader: <see cref="MonsterExperienceWorth"/> is not "what a kill
    /// pays". It is the table that, halved and scaled, produces what a kill pays.
    /// </para>
    /// </remarks>
    private const double MonsterWorthDivisor = 2.0;

    /// <summary>What a kill of these circumstances is worth.</summary>
    public static uint WorthOf(in KillCircumstances kill, ExperienceParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var partySize = Math.Max(1, kill.PartyMemberCount);

        var worth = MonsterExperienceWorth.For(kill.MonsterLevel);
        if (worth == 0)
            return 0;

        var shared = Floor(
            worth / MonsterWorthDivisor
            * parameters.PartyMemberRate(partySize)
            * parameters.DifficultyRate(kill.Difficulty)
            * kill.DungeonWeight
            * RankRate(kill.Rank)
            * parameters.MonsterOverallRate);

        return Floor(shared * LevelGapRate(kill.CharacterLevel, kill.MonsterLevel) / partySize);
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

    /// <summary>What this rank multiplies a kill by.</summary>
    public static double RankRate(MonsterRank rank) =>
        (int)rank >= 0 && (int)rank < RankRates.Length ? RankRates[(int)rank] : RankRates[0];

    /// <summary>
    /// What the gap between a character's level and a monster's does to a kill's worth.
    /// </summary>
    /// <param name="characterLevel">The level of the character who made the kill.</param>
    /// <param name="monsterLevel">The level of what was killed.</param>
    /// <remarks>
    /// <b>Eight distinct rates, one of them evidenced.</b> Every captured sample has the monster
    /// one to three levels above the character, which is the 1.12 rate; the other seven are the
    /// reference server's word and nothing else, and the archive has no table matching them.
    /// (<c>etc/worldmapexppenaltytable.etc</c> is a different table for a different purpose --
    /// its values are 0.3 through 1.0 over a wider range -- and must not be confused for this one.)
    /// <para>
    /// The shape is not a formula and does not compress: twelve arms carrying eight distinct rates,
    /// asymmetric, with a bump above 1 where the monster is a little above the character. Writing
    /// it as an expression would need as many parameters as it has values. It is a hand-tuned
    /// curve discouraging both power-levelling and farming what a character has outgrown.
    /// </para>
    /// </remarks>
    public static double LevelGapRate(int characterLevel, int monsterLevel) =>
        (monsterLevel - characterLevel) switch
        {
            <= -7 => 0.05,
            -6 => 0.20,
            -5 => 0.50,
            -4 => 0.75,
            >= -3 and <= 0 => 1.00,
            // The only band the captures reach, and the only one above 1: a monster a little
            // above the character pays a premium.
            >= 1 and <= 3 => 1.12,
            4 or 5 => 1.00,
            6 => 0.75,
            7 => 0.70,
            8 => 0.60,
            9 => 0.50,
            _ => 0.05,
        };

    private static uint Floor(double value) =>
        value <= 0 || double.IsNaN(value) ? 0
        : value >= uint.MaxValue ? uint.MaxValue
        : (uint)Math.Floor(value);
}
