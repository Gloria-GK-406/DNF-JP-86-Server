namespace Dfo.Server.Abstractions.Content;

/// <summary>
/// Which of a rate table's parallel rates this is.
/// </summary>
/// <remarks>
/// Five of them, and they are not alternatives: a level band declares a rate for each, and an
/// occasion rolls every one separately. The archive names none of them -- what fixes the reading
/// is that all three bonus tables are declared in exactly as many groups as a band has rates, one
/// group to a rate.
/// <para>
/// That the four item slots are stackable, equipment, stackable and avatar is the reference
/// server's reading and has no second source. It is applied where a slot has to become a kind and
/// is named as a reading there; nothing in this file depends on it.
/// </para>
/// </remarks>
public enum DropRateSlot
{
    Gold = 0,
    ItemType1 = 1,
    ItemType2 = 2,
    ItemType3 = 3,
    ItemType4 = 4,
}

/// <summary>One level band of a rate table, and what it pays each slot.</summary>
/// <param name="Rates">
/// One rate per slot, in slot order. They are numerators over a basis this family of files does
/// not declare; see <c>DropRateOptions.RateBasis</c> for where that comes from and why.
/// </param>
public sealed record DropRateBand(int LowLevel, int HighLevel, IReadOnlyList<int> Rates)
{
    /// <summary>Whether this band covers that level.</summary>
    public bool Covers(int level) => level >= this.LowLevel && level <= this.HighLevel;

    /// <summary>What this band pays that slot, or nothing when it declares no such slot.</summary>
    public int RateOf(DropRateSlot slot) =>
        (int)slot >= 0 && (int)slot < this.Rates.Count ? this.Rates[(int)slot] : 0;
}

/// <summary>
/// A rarity ladder: cumulative thresholds and the basis that closes them.
/// </summary>
/// <remarks>
/// The archive writes one group as N thresholds followed by one more number, and that last number
/// is the basis. Every group read through this type closes that way -- "basis = last threshold +
/// 1" -- but that is the shape of the groups being read rather than a rule the family keeps: the
/// abyss table satisfies it in neither of its two groups, closing 1000001 after 1000001 in one
/// and 1000001 after 1000002 in the other, and is refused rather than read. A roll is an integer
/// from one up to <see cref="HighestRoll"/>, and the band it lands in is the first threshold it
/// does not exceed.
/// <para>
/// <b>The basis is read, never assumed.</b> The reference server hard-codes a million against a
/// basis of 1000003, which puts its highest roll one short of the epic band and two short of the
/// chronicle band -- so neither can ever be drawn, and nothing anywhere says so. That is the
/// single most consequential line in this whole subject and it looks entirely ordinary.
/// </para>
/// <para>
/// A threshold equal to the one before it is a band of width zero, which is how the archive turns
/// a band off. The abyss table does exactly that to white and pink.
/// </para>
/// </remarks>
public sealed record RarityLadder(IReadOnlyList<int> Values)
{
    /// <summary>The basis the archive closes the group with.</summary>
    public int Basis => this.Values[^1];

    /// <summary>The largest roll this ladder can produce. Rolls run from one up to it.</summary>
    public int HighestRoll => this.Basis - 1;

    /// <summary>How many bands the group declares.</summary>
    public int BandCount => this.Values.Count - 1;

    /// <summary>How many of the ladder's rolls land in that band.</summary>
    public int WeightOf(int band)
    {
        if (band < 0 || band >= this.BandCount)
            return 0;
        var upper = Math.Min(this.Values[band], this.HighestRoll);
        var lower = band == 0 ? 0 : Math.Min(this.Values[band - 1], this.HighestRoll);
        return Math.Max(0, upper - lower);
    }

    /// <summary>What share of rolls land in that band.</summary>
    public double ProbabilityOf(int band) =>
        this.HighestRoll <= 0 ? 0 : (double)this.WeightOf(band) / this.HighestRoll;

    /// <summary>Which band this roll lands in.</summary>
    /// <remarks>
    /// A roll above every threshold reads as the top band rather than as an error. It cannot
    /// happen for a well-formed group -- the last threshold is the highest roll -- and reading it
    /// as the top band is the answer that stays inside the ladder if one is ever malformed.
    /// </remarks>
    public int BandOf(int roll)
    {
        for (var band = 0; band < this.BandCount; band++)
        {
            if (roll <= this.Values[band])
                return band;
        }

        return this.BandCount - 1;
    }
}

/// <summary>The span of item grades a level may draw from, both ends included.</summary>
public readonly record struct ItemGradeBand(int Low, int High)
{
    public bool Covers(int grade) => grade >= this.Low && grade <= this.High;
}

/// <summary>
/// One of the archive's <c>itemdropinfo_*.etc</c> tables, read.
/// </summary>
/// <remarks>
/// Three of the family's seven files are read through this one: monster, monster-extra and
/// breakable object. The first two declare every section this record holds; the breakable-object
/// table declares all of them but <c>[party member drop bonusrate]</c>, which is absent from it
/// and comes back as <see cref="UnlistedRate"/> rather than as a rate of somebody else's. The
/// abyss table declares a difficulty-keyed section where the three declare <c>[drop prob]</c>;
/// the common and control files declare it not at all; and the clear-reward table declares named
/// profiles of one rate each, so it is read by <c>IClearRewardRateSource</c> rather than by this
/// one -- see <see cref="DropRateTables.ClearReward"/>. Nothing here is specific to monsters;
/// which file was read is the caller's choice, but it must be one of the three.
/// </remarks>
/// <param name="ArchivePath">Which file this was read from, so a log can say.</param>
/// <param name="LevelBands">The bands, in declaration order.</param>
/// <param name="RarityLadders">
/// One ladder per item slot. The reading that ladder <c>n</c> belongs to item slot <c>n</c> is the
/// reference server's and has no second source; what supports it is that gold has no rarity, which
/// leaves exactly four rates needing a ladder and exactly four ladders declared.
/// </param>
/// <param name="MonsterRankRates">What each monster rank multiplies each slot's rate by.</param>
/// <param name="DifficultyRates">What each difficulty multiplies each slot's rate by.</param>
/// <param name="PartyMemberRates">What each party size multiplies each slot's rate by.</param>
/// <param name="GradeBands">
/// What span of item grades each level draws from. Read from <c>[item drop ref table]</c>, whose
/// rows are level, how far below, and how far above. The reading checks itself: from level 86
/// upwards the archive moves the lower edge in step with the level so that it stays at grade 79,
/// which is the floor of A21's endgame gear.
/// </param>
public sealed record DropRateTable(
    string ArchivePath,
    IReadOnlyList<DropRateBand> LevelBands,
    IReadOnlyList<RarityLadder> RarityLadders,
    IReadOnlyList<IReadOnlyList<double>> MonsterRankRates,
    IReadOnlyList<IReadOnlyList<double>> DifficultyRates,
    IReadOnlyList<IReadOnlyList<double>> PartyMemberRates,
    IReadOnlyDictionary<int, ItemGradeBand> GradeBands)
{
    /// <summary>What a multiplier this table does not list is worth.</summary>
    /// <remarks>
    /// One. An unlisted rank, difficulty or party size is one this table does not describe, and
    /// substituting a neighbouring group's multiplier would silently pay it somebody else's rate.
    /// </remarks>
    public const double UnlistedRate = 1.0;

    /// <summary>The band covering that level, or null when none does.</summary>
    public DropRateBand? BandFor(int level) =>
        this.LevelBands.FirstOrDefault(band => band.Covers(level));

    /// <summary>The ladder that decides this slot's rarity, or null for a slot that has none.</summary>
    /// <remarks>Gold has none: the first ladder belongs to the first item slot.</remarks>
    public RarityLadder? LadderFor(DropRateSlot slot)
    {
        var index = (int)slot - 1;
        return index >= 0 && index < this.RarityLadders.Count ? this.RarityLadders[index] : null;
    }

    /// <summary>What this monster rank multiplies that slot's rate by.</summary>
    public double MonsterRankRate(DropRateSlot slot, int rank) =>
        Lookup(this.MonsterRankRates, slot, rank);

    /// <summary>What this difficulty multiplies that slot's rate by.</summary>
    public double DifficultyRate(DropRateSlot slot, int difficulty) =>
        Lookup(this.DifficultyRates, slot, difficulty);

    /// <summary>What a party of this size multiplies that slot's rate by.</summary>
    public double PartyMemberRate(DropRateSlot slot, int partyMemberCount) =>
        Lookup(this.PartyMemberRates, slot, partyMemberCount - 1);

    /// <summary>The grades this level draws from, or null when the table declares no such row.</summary>
    /// <remarks>
    /// The table covers levels one to two hundred; a level outside that has no band, which is an
    /// ordinary answer rather than a broken archive.
    /// </remarks>
    public ItemGradeBand? GradeBandFor(int level) =>
        this.GradeBands.TryGetValue(level, out var band) ? band : null;

    private static double Lookup(
        IReadOnlyList<IReadOnlyList<double>> groups, DropRateSlot slot, int index)
    {
        var group = (int)slot;
        if (group < 0 || group >= groups.Count)
            return UnlistedRate;
        var rates = groups[group];
        return index >= 0 && index < rates.Count ? rates[index] : UnlistedRate;
    }
}

/// <summary>The files this family holds.</summary>
public static class DropRateTables
{
    public const string Monster = "etc/itemdropinfo_monseter.etc";
    public const string MonsterExtra = "etc/itemdropinfo_monseter_extra.etc";
    public const string BreakableObject = "etc/itemdropinfo_object.etc";

    /// <summary>
    /// The clear-reward table. Not the same sections: seven of its sixteen are the family's and
    /// the other nine are declared by no other file of it. One of the seven it declares
    /// differently -- its <c>[drop prob]</c> is named profiles of one rate each rather than bands
    /// of five -- so the card flip is read by <c>IClearRewardRateSource</c> rather than by handing
    /// this path to <see cref="IDropRateTableSource"/>.
    /// </summary>
    public const string ClearReward = "etc/itemdropinfo_clearreward.etc";
}

/// <summary>Reads one of the archive's rate tables.</summary>
public interface IDropRateTableSource
{
    /// <summary>The table this file declares. Read once and held.</summary>
    DropRateTable Load(string archivePath);
}

/// <summary>
/// What a kill pays in gold: a base for each level, a multiple for each rank, and a band to
/// vary it inside.
/// </summary>
/// <remarks>
/// Three sections across two files. <c>[gold drop ref table]</c> lives in
/// <c>etc/itemdropinfo_common.etc</c> and is the whole world's; <c>[gold quantity]</c> and
/// <c>[gold volume]</c> live in the rate table being used, so a breakable object and a monster
/// can pay differently for the same level.
/// </remarks>
/// <param name="BaseAmounts">What one drop of gold is worth at each level.</param>
/// <param name="RankQuantities">
/// What each monster rank multiplies that by. Four of them, matching the four ranks a map may
/// call a monster: <c>1 2 4 6</c> in this archive.
/// </param>
/// <param name="VariancePercents">
/// The third number of each <c>[gold drop ref table]</c> row, by level. <b>It is banded, not
/// constant</b>: A21 declares <c>13</c> from level 1 to 64, <c>11</c> from 65 to 77 and <c>9</c>
/// from 78 to 200. Reading it as one figure would pass every check taken at a low level and be
/// wrong for every endgame one.
/// <para>
/// <b>Read as a percentage either side of the amount</b>, which is what the A21 reference server's
/// own gold path calls it and how it applies it. That naming is the only part of this column taken
/// from the reference; the three-wide row it sits in is the archive's own, so the column's place
/// is checkable even where its meaning rests on that reading.
/// </para>
/// <para>
/// Distinct from <see cref="VolumeBand"/>, which is the monster path's <c>[gold volume]</c> and
/// lives in the rate file rather than the shared one. A rate file declaring no volume band still
/// carries these.
/// </para>
/// </param>
/// <param name="VolumeBand">
/// The percentage band the amount is varied inside, both ends included. <c>100 110</c> here, so
/// gold is never less than its base and up to a tenth more.
/// </param>
public sealed record GoldDropTable(
    IReadOnlyDictionary<int, int> BaseAmounts,
    IReadOnlyDictionary<int, int> VariancePercents,
    IReadOnlyList<int> RankQuantities,
    (int Low, int High) VolumeBand)
{
    /// <summary>What one drop of gold is worth at that level, or nothing at an unlisted one.</summary>
    public int BaseFor(int level) =>
        this.BaseAmounts.TryGetValue(level, out var amount) ? amount : 0;

    /// <summary>
    /// How far either side of the base that level's amount may fall, as a percentage, or nothing
    /// at an unlisted level.
    /// </summary>
    public int VarianceFor(int level) =>
        this.VariancePercents.TryGetValue(level, out var percent) ? percent : 0;

    /// <summary>What this rank multiplies the base by, or once for an unlisted rank.</summary>
    public int QuantityFor(int monsterRank) =>
        monsterRank >= 0 && monsterRank < this.RankQuantities.Count
            ? this.RankQuantities[monsterRank]
            : 1;
}

/// <summary>Reads what a kill pays in gold.</summary>
public interface IGoldDropTableSource
{
    /// <summary>
    /// The gold table for that rate file, taking the level bases from the shared common file.
    /// </summary>
    GoldDropTable Load(string archivePath);
}
