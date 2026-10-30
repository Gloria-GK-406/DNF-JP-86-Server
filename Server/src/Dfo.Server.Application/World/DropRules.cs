using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;

using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>
/// What decides whether an occasion drops anything, and what.
/// </summary>
/// <remarks>
/// Pure. It takes numbers and answers numbers: no dungeon, no monster, no character. The rarity
/// a piece is drawn at is a parameter rather than something rolled inside, which is what lets one
/// pick serve both the ordinary case -- roll a rarity, then draw at it -- and a constrained one,
/// where a caller asks for a piece of a named quality.
/// </remarks>
internal static class DropRules
{
    /// <summary>What a slot yields when it hits.</summary>
    /// <remarks>See <see cref="DropItemKind"/>: this mapping is the reference server's.</remarks>
    public static DropItemKind KindOf(DropRateSlot slot) => slot switch
    {
        DropRateSlot.Gold => DropItemKind.Gold,
        DropRateSlot.ItemType1 => DropItemKind.Stackable,
        DropRateSlot.ItemType2 => DropItemKind.Equipment,
        DropRateSlot.ItemType3 => DropItemKind.Stackable,
        DropRateSlot.ItemType4 => DropItemKind.Avatar,
        _ => DropItemKind.Gold,
    };

    /// <summary>
    /// Every slot's rate for this occasion, after the three multipliers the table declares.
    /// </summary>
    /// <remarks>
    /// Rate times rank times difficulty times party size, held at the basis. The archive's own
    /// steepest product stays under it -- a boss's gold comes to 4950 against a basis of 10000 --
    /// so the cap never bites on the monster table as it stands. It is here because the margin is
    /// only a factor of two: one more multiplier, or a table whose rank group is steeper, would
    /// pass the basis, and a rate above the basis is a certainty wearing the costume of a chance.
    /// <para>
    /// The level-gap penalty of <c>[drop bonus of dungeon level increase]</c> is deliberately not
    /// applied here. It exists, it is archive-declared, and nothing in this phase's acceptance
    /// asks for it; leaving it out pays slightly too much rather than inventing a number, and the
    /// omission is recorded rather than hidden.
    /// </para>
    /// </remarks>
    public static DropRates RatesFor(
        DropRateTable table,
        DropRateBand band,
        int monsterRank,
        DungeonDifficulty difficulty,
        int partyMemberCount,
        int basis)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(band);

        var slots = new List<SlotRate>(band.Rates.Count);
        for (var index = 0; index < band.Rates.Count; index++)
        {
            var slot = (DropRateSlot)index;
            var rate = band.RateOf(slot)
                * table.MonsterRankRate(slot, monsterRank)
                * table.DifficultyRate(slot, (int)difficulty)
                * table.PartyMemberRate(slot, partyMemberCount);
            slots.Add(new SlotRate(slot, Math.Min(rate, basis)));
        }

        return new DropRates(slots, basis);
    }

    /// <summary>
    /// Which slots hit, in slot order.
    /// </summary>
    /// <remarks>
    /// <b>Each slot is rolled on its own.</b> That is the reference server's reading and it has no
    /// second source, but the archive argues for it: the rank multipliers change a slot's rate by
    /// up to eighty-four times, and under a single mutually-exclusive distribution a multiplier
    /// could only ever change <i>which</i> of the five a kill paid, never how many. A boss that
    /// drops gold and a weapon together -- which the captures show -- would then be impossible.
    /// </remarks>
    public static IReadOnlyList<DropRateSlot> Gate(DropRates rates, DropRandom random)
    {
        ArgumentNullException.ThrowIfNull(rates);
        ArgumentNullException.ThrowIfNull(random);

        var hits = new List<DropRateSlot>();
        foreach (var slot in rates.Slots)
        {
            if (slot.Rate > 0 && random.Next(rates.Basis) <= slot.Rate)
                hits.Add(slot.Slot);
        }

        return hits;
    }

    /// <summary>The quality this slot's ladder draws, or common when it declares no ladder.</summary>
    public static DropRarity RollRarity(
        DropRateTable table, DropRateSlot slot, DropRandom random)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(random);

        if (table.LadderFor(slot) is not { } ladder || ladder.HighestRoll <= 0)
            return DropRarity.Common;

        return (DropRarity)ladder.BandOf(random.Next(ladder.HighestRoll));
    }

    /// <summary>
    /// One item drawn from the band at that quality, or nothing when the band holds nothing.
    /// Answers the whole entry, because what the piece is created carrying travels with it.
    /// </summary>
    /// <remarks>
    /// Weighted by each item's own creation rate. An empty band is an ordinary answer: the
    /// archive fills five of the ladder's six qualities, and a monster whose band happens to
    /// declare nothing at the quality that was rolled simply drops nothing from that slot. It is
    /// not an error and must not become one -- refusing here would turn a quiet miss into a kill
    /// that fails.
    /// </remarks>
    public static DropPoolEntry? Pick(
        DropItemPool pool, ItemGradeBand band, DropRarity rarity, DropRandom random)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(random);

        var candidates = pool.Band(band, rarity);
        if (candidates.Count == 0)
            return null;

        var total = 0L;
        foreach (var candidate in candidates)
            total += Math.Max(0, candidate.Weight);
        if (total <= 0 || total > int.MaxValue)
            return null;

        var roll = random.Next((int)total);
        var running = 0L;
        foreach (var candidate in candidates)
        {
            running += Math.Max(0, candidate.Weight);
            if (roll <= running)
                return candidate;
        }

        return candidates[^1];
    }

    /// <summary>
    /// What a kill of that level pays in gold when the gold slot hits.
    /// </summary>
    /// <remarks>
    /// Base amount for the level, times the rank's own multiple, varied inside the declared band.
    /// All three are the archive's: <c>[gold drop ref table]</c> in
    /// <c>etc/itemdropinfo_common.etc</c>, and <c>[gold quantity]</c> and <c>[gold volume]</c> in
    /// the rate table itself.
    /// </remarks>
    public static int GoldAmount(GoldDropTable gold, int level, int monsterRank, DropRandom random)
    {
        ArgumentNullException.ThrowIfNull(gold);
        ArgumentNullException.ThrowIfNull(random);

        var basis = gold.BaseFor(level);
        if (basis <= 0)
            return 0;

        var (low, high) = gold.VolumeBand;
        var percent = high > low ? low + random.Next(high - low + 1) - 1 : low;
        var amount = (long)basis * gold.QuantityFor(monsterRank) * percent / 100;
        return amount <= 0 ? 1 : (int)Math.Min(amount, int.MaxValue);
    }
}

/// <summary>
/// The one number on this path that the archive does not declare.
/// </summary>
/// <remarks>
/// A rate table's five rates are numerators, and nothing in the <c>itemdropinfo_*</c> family says
/// what they are numerators over. The rarity ladders each close with their own basis; these do
/// not.
/// <para>
/// There are two candidates and no way to choose between them from the archive.
/// <c>Etc/ServerParameter.etc</c> declares <c>[basis of item dicision] 2500</c>, which sits
/// directly beside that file's <b>own</b> <c>[drop prob]</c> -- a different table, three bands of
/// two rates -- so it most plausibly belongs to that one. The reference server uses 10000, which
/// it declares nowhere.
/// <para>
/// The two differ by a factor of four: an ordinary monster's gold is either 7.5% or 30%.
/// </para>
/// </para>
/// <para>
/// So it is a server policy rather than a reading: configurable, defaulted to 10000 because that
/// is the value whose resulting shape matches how the game is remembered to play -- an ordinary
/// monster paying about one time in thirteen, a boss about half the time -- and passed into the
/// rules, which know no basis of their own. The rules can therefore never be wrong about this,
/// only configured wrongly, and a test that changes it can prove the rules read it.
/// </para>
/// </remarks>
internal sealed class DropRateOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "DropRates";

    /// <summary>What the archive's rate numerators are numerators over.</summary>
    public int RateBasis { get; init; } = 10000;
}
