using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// The odds one card position carries: how many dice values let it through, out of how many the
/// dice has.
/// </summary>
/// <remarks>
/// A count of winning dice values rather than a bare probability, because that is the form the
/// roll takes -- <see cref="DropRandom.Hits(int, int)"/> rolls one to the basis and compares
/// against it -- and a probability handed around instead would have to be turned back into one at
/// the roll anyway.
/// <para>
/// <see cref="ClearRewardRules.ChanceOf"/> counts the dice values that pass the threshold, and
/// <see cref="DropRandom.Hits(int, int)"/> lets a roll through when it comes in at or under that
/// count. The two put the winning half of the dice at opposite ends -- the composition's winners
/// are the high values, the roll's are the low ones -- and nothing turns on which end, because the
/// dice is uniform and only how many values win decides anything.
/// </para>
/// <para>
/// Where the basis comes from is answered in one place: <see cref="ClearRewardRateOptions"/>.
/// </para>
/// </remarks>
public readonly record struct ClearRewardChance(int Rate, int Basis)
{
    /// <summary>What share of positions these odds let through.</summary>
    public double Probability => this.Basis <= 0 ? 0 : (double)this.Rate / this.Basis;

    /// <summary>Whether no position at these odds can hold anything.</summary>
    public bool IsImpossible => this.Rate <= 0 || this.Basis <= 0;
}

/// <summary>
/// <b>What the clear-reward numbers are counted out of, what the dice is rolled over, and whether
/// a position that was drawn and came up blank hands out the fallback its card declares.</b>
/// </summary>
/// <remarks>
/// One number, used for three things that are the same thing: what a whole-number rate out of
/// <c>etc/itemdropinfo_clearreward.etc</c> is a share of, how many values the dice
/// <see cref="ClearRewardRules.ChanceOf"/> counts over has, and the scale the party multiplier is
/// taken to whole parts of.
/// <para>
/// <b>The archive does not name it, but it does corroborate it, and only this much of the
/// composition has that.</b> Three things in the file agree on ten thousand:
/// <c>[drop kind prob]</c> declares <c>10000 0</c>, whose two entries sum to ten thousand;
/// <c>[drop item type prob]</c> declares <c>0 9500 0 500</c>, whose four sum to ten thousand; and
/// the <c>default</c> profile of <c>[drop prob]</c> pays exactly 10000 in every one of its bands,
/// which is what a full share looks like when the profiles beside it pay less. None of that is a
/// declaration of a basis -- the file states no such thing anywhere -- but it is the file's own
/// arithmetic rather than an outside reading, which is more than the five decisions named on
/// <see cref="ClearRewardRules"/> can claim.
/// </para>
/// <para>
/// Configuration rather than a constant, so that an operator can move it and a test can prove the
/// rules read it rather than hold an opinion about it. The rules default nothing:
/// <see cref="ClearRewardRules.ChanceOf"/> takes the basis as a parameter, so it can be configured
/// wrongly but can never be wrong of its own accord.
/// </para>
/// <para>
/// <b>It also carries the three fallback switches</b>, on <see cref="BlankItems"/>. They are here
/// rather than in an options type of their own because this is the clear-reward path's one
/// settings object and a second would have to be registered beside it; what they answer is on
/// <see cref="ClearRewardBlankItemSwitches"/>. Unlike <see cref="RateBasis"/> they are not read
/// from configuration today -- the registration in <c>ApplicationModule</c> builds this object and
/// reads the basis key alone -- so switching one on is a change to that registration. Off is what
/// the archive's two items are worth here, so nothing is waiting on the key.
/// </para>
/// </remarks>
public sealed class ClearRewardRateOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ClearRewardRates";

    /// <summary>What the clear-reward table's whole-number rates are shares of.</summary>
    public int RateBasis { get; set; } = 10000;

    /// <summary>Temporary acceptance switch; never enabled by default.</summary>
    public bool DebugGuaranteePaidEquipment { get; set; }

    /// <summary>
    /// Whether a card position that came up holding nothing is handed the fallback its card
    /// declares. See <see cref="ClearRewardBlankItemSwitches"/>; all three are off.
    /// </summary>
    /// <remarks>
    /// Read-only so it can never be null, and still writable in an object initializer
    /// (<c>BlankItems = { GoldCard = true }</c>) and bindable into by configuration.
    /// </remarks>
    public ClearRewardBlankItemSwitches BlankItems { get; } = new();
}

/// <summary>
/// Which card's fallback table is being asked about.
/// </summary>
/// <remarks>
/// <b>Three cards, and deliberately not <see cref="ClearRewardCardKind"/>.</b> That enum is what a
/// flip carries, and the flip protocol carries two values: <c>CardRewardPayloadCodec</c>'s
/// <c>ReadSelectCardRequest</c> reads a face byte of zero or one and drops the frame for anything
/// else. The archive's fallbacks are keyed on something wider than that -- it declares one for the
/// paid card, one for the PC-room card, and none at all for the free card -- so the two are
/// separate enums rather than one stretched over both.
/// </remarks>
public enum ClearRewardBlankCard
{
    /// <summary>
    /// The free card, which <b>the archive declares no fallback table for at all</b>.
    /// </summary>
    /// <remarks>
    /// <c>etc/itemdropinfo_clearreward.etc</c> holds <c>[gold card blank item]</c> and
    /// <c>[pcroom card blank item]</c> and no third section of that shape. The asymmetry is the
    /// file's own and nothing here evens it out: a free card is not given the paid card's rows,
    /// because that would be this server declaring a fallback the archive does not. So this member
    /// exists to be switched, and switching it on still hands out nothing on this archive -- see
    /// <see cref="ClearRewardBlankItemSwitches.FreeCard"/>.
    /// </remarks>
    Free = 0,

    /// <summary>The paid card: <c>[gold card blank item]</c>, six weighted rows.</summary>
    /// <remarks>
    /// The card face the archive calls the gold card, not an amount of gold. A bare <c>Gold</c>
    /// in this repository always means the money, which is why this member carries the noun.
    /// </remarks>
    GoldCard = 1,

    /// <summary>
    /// The PC-room card: <c>[pcroom card blank item]</c>, one row.
    /// </summary>
    /// <remarks>
    /// <b>No flip on this server can name it.</b> There is no PC-room card in
    /// <see cref="ClearRewardCardKind"/> and the flip protocol has no third face byte to carry
    /// one, so the table is read and the switch is here while the path that would use them is not
    /// built. That is this phase's boundary rather than an oversight: what the row holds is item
    /// 7454, which is unusable here for the reason on
    /// <see cref="ClearRewardBlankItemSwitches.PcRoomCard"/>.
    /// </remarks>
    PcRoom = 2,
}

/// <summary>
/// <b>Whether a position that was drawn and came up blank hands out the fallback its card
/// declares. Every one of the three is off, and each says in its own place why.</b>
/// </summary>
/// <remarks>
/// The archive declares the fallbacks, so this server reads them and can draw from them; what it
/// does not declare is that a server in 2026 should hand them out, and both of the items it names
/// are unusable here. So the mechanism is built and switched off rather than left unbuilt: an
/// operator who wants either of them turns one switch, and nothing about the reading has to be
/// rediscovered first.
/// <para>
/// <b>Off is not merely a small payout, it is today's behaviour exactly.</b> With all three off no
/// fallback table is looked at, no roll is spent on one, and a position that holds nothing holds
/// nothing -- which is what this server did before the switches existed.
/// </para>
/// <para>
/// Three switches rather than one because the three cards are three different questions: the
/// items differ, the reasons they are unusable differ, and the free card has no table to hand out
/// from in the first place.
/// </para>
/// <para>
/// <b>None of the three reaches an occasion the archive's tables could not answer.</b> A fallback
/// is what a thrown dice left empty, and a table that was never answered was never thrown
/// against; paying one there would turn a failure to read the archive into a payout. Which routes
/// those are is on <c>ClearRewardRuleService.Undrawn</c>.
/// </para>
/// </remarks>
public sealed class ClearRewardBlankItemSwitches
{
    /// <summary>
    /// The free card's fallback. Off, and <b>on it still hands out nothing</b>.
    /// </summary>
    /// <remarks>
    /// The archive declares the free card no fallback table -- see
    /// <see cref="ClearRewardBlankCard.Free"/> -- so this switch has nothing behind it on A21 and
    /// is here because the three cards are asked about the same way. A file that did declare one
    /// would be handed out by turning this on and changing nothing else.
    /// </remarks>
    public bool FreeCard { get; set; }

    /// <summary>
    /// The paid card's fallback, <c>[gold card blank item]</c>. Off.
    /// </summary>
    /// <remarks>
    /// <b>Off because the item the archive names no longer exists.</b> All six rows name item
    /// 10002120, which <c>stackable/stackable.lst</c> resolves to
    /// <c>event/chn_event_20130723_GhostSeria/chn_stk_signs_of_seria.stk</c> --
    /// 幽灵赛丽亚的痕迹, a July 2013 event token whose own <c>[explain]</c> says it was
    /// to be deleted wholesale on 2013-09-03. Handing out an item the archive itself declares
    /// expired is not a floor under a blank card; it is a token nothing can be done with.
    /// <para>
    /// Read and drawable all the same, because the reading is the archive's and the decision not
    /// to pay it out is this server's. A deployment that revives the 2013 event, or one that
    /// replaces the rows, turns this on.
    /// </para>
    /// </remarks>
    public bool GoldCard { get; set; }

    /// <summary>
    /// The PC-room card's fallback, <c>[pcroom card blank item]</c>. Off.
    /// </summary>
    /// <remarks>
    /// <b>Off because what it pays belongs to a system this server does not have.</b> Its single
    /// row is <c>50 7454 1</c>, and item 7454 is 钻石硬币 -- a coin that starts the
    /// black-diamond vending machine, which only a black-diamond member can start. This server
    /// implements no black-diamond membership, so the coin would be handed out with nothing to
    /// spend it on.
    /// <para>
    /// It is also not reachable from a flip here at all; see
    /// <see cref="ClearRewardBlankCard.PcRoom"/>. Switching it on therefore changes what
    /// <see cref="Allows"/> answers and nothing a player can see, which is the state this phase
    /// leaves it in on purpose.
    /// </para>
    /// </remarks>
    public bool PcRoomCard { get; set; }

    /// <summary>Whether that card hands out the fallback it declares.</summary>
    /// <remarks>
    /// The one place the three are read as three, so a caller holding a card asks rather than
    /// remembering which property goes with which. A card this type does not know answers false,
    /// which is the same "not switched on" every unset switch gives.
    /// </remarks>
    public bool Allows(ClearRewardBlankCard card) => card switch
    {
        ClearRewardBlankCard.Free => this.FreeCard,
        ClearRewardBlankCard.GoldCard => this.GoldCard,
        ClearRewardBlankCard.PcRoom => this.PcRoomCard,
        _ => false,
    };
}

/// <summary>
/// What decides whether a card position holds anything.
/// </summary>
/// <remarks>
/// Pure, and deliberately thin. Only two steps of producing a card face are new work: turning a
/// maze's room count into a base rate, and composing that with the archive's other numbers and a
/// dice roll into a single chance. The three steps after them -- roll a quality on the table's
/// ladder, take the level's grade band, draw one weighted piece from the pool at that grade and
/// quality -- are the existing drop parts and are called rather than restated, so this repository
/// holds one ladder semantics and not two. See
/// <see cref="Dfo.Server.Abstractions.Content.RarityLadder"/> for what a second one costs.
/// <para>
/// <b>Five decisions here are this server's own and the archive states none of them.</b> They are
/// named one by one where each is performed, and this is the list of them, in the order the
/// ordinals elsewhere use, so that no reader has to assemble it: first, that the numbers are
/// joined by <b>addition</b>; second, that the party multiplier is applied to the <b>dice</b>
/// (both on <see cref="ChanceOf"/>); third, that a maze with no row of its own is priced at the
/// table's <b>largest declared row</b> (on <see cref="MapBaseRate"/>); fourth, that
/// <c>[drop prob]</c> is read as the <b>threshold</b> the sum has to pass rather than as a rate of
/// its own, whose other half is which profile's bar is taken (on <see cref="ChanceOf"/>, and again
/// where the profile is chosen); and fifth, that the comparison is carried out in <b>whole
/// numbers</b> rather than in reals (on <see cref="ChanceOf"/>). The archive says nothing about
/// any of the five; a reference implementation of the same file composes them differently, and
/// that is another server's reading rather than evidence.
/// </para>
/// <para>
/// <b>The fallback draw on <see cref="BlankItemOf"/> is not one of the five and is not a
/// composition at all, and it answers only for a position that was drawn.</b> It is the archive's
/// own weights read the way a pool's weights are read, and the decision beside it -- whether a
/// position that came up blank hands its card's fallback out -- is a switch rather than a reading,
/// kept on <see cref="ClearRewardBlankItemSwitches"/> where its three answers and their three
/// reasons are written down.
/// </para>
/// </remarks>
public static class ClearRewardRules
{
    /// <summary>
    /// How many positions one card face has.
    /// </summary>
    /// <remarks>
    /// Four, and the archive is not where that comes from: the clear-reward table's own count
    /// section says how many level bands a profile holds, which is separately four and has nothing
    /// to do with positions on a face. The number lives here because these rules answer a whole
    /// face at once -- one call, four positions -- and that is what makes "the same position of
    /// the same clear holds the same thing" a property of being asked once rather than of every
    /// caller remembering to pass the same seed.
    /// </remarks>
    public const int CardSlotCount = 4;

    /// <summary>
    /// What a maze of this many rooms contributes before anything else is added.
    /// </summary>
    /// <remarks>
    /// <c>[reward item rate per map max count]</c> of
    /// <c>etc/itemdropinfo_clearreward.etc</c>, keyed on how many rooms a maze holds in total. A21
    /// declares five rows -- one room through five, paying 2000, 3000, 4000, 5000 and 6000 -- and
    /// no more.
    /// <para>
    /// <b>This server's decision, and the archive says nothing about it: a maze the table declares
    /// no row for is priced at the largest row it does declare.</b> On A21 that is the five-room
    /// row, so every maze of six rooms or more contributes 6000. Three things argue for it and
    /// none of them is the file: real mazes are routinely larger than five rooms -- dungeon 144's
    /// five hold ten, six, four, six and four -- so a count with no row is the ordinary case and
    /// not an edge; a bigger maze must not pay less than a smaller one, which rules out answering
    /// nothing; and holding the largest row is the one answer that adds no number the file does
    /// not contain. Answering nothing is what this server did until this revision, and it emptied
    /// both faces of every clear of three of dungeon 144's five mazes.
    /// </para>
    /// <para>
    /// <b>How far the run got makes no difference here.</b> The table is keyed on how many rooms
    /// the maze holds and on nothing else; scaling it by the share of rooms entered would be a
    /// second reading laid on top of the first, and the file gives no ground for one.
    /// </para>
    /// <para>
    /// A count below the table's smallest row is answered the same way as one above its largest,
    /// and does not arise: a maze holds at least the room its run is created standing in.
    /// </para>
    /// <para>
    /// Null only when the table declares no rows whatever, which is a file that has lost the
    /// section rather than a maze the section does not cover. That is the same "absent is not
    /// neutral" rule the rest of this path keeps: a caller handed null answers impossible rather
    /// than inventing a base rate.
    /// </para>
    /// </remarks>
    /// <param name="mapCountRates">
    /// <c>[reward item rate per map max count]</c> as the table read it, keyed on room count.
    /// </param>
    /// <param name="mapMaxCount">How many rooms the run's maze holds.</param>
    public static int? MapBaseRate(IReadOnlyDictionary<int, int> mapCountRates, int mapMaxCount)
    {
        ArgumentNullException.ThrowIfNull(mapCountRates);

        if (mapCountRates.TryGetValue(mapMaxCount, out var declared))
            return declared;

        int? largest = null;
        var largestCount = int.MinValue;
        foreach (var (count, rate) in mapCountRates)
        {
            if (count <= largestCount)
                continue;

            largestCount = count;
            largest = rate;
        }

        return largest;
    }

    /// <summary>
    /// The odds one position carries: how many of the dice's values carry the sum past the
    /// threshold.
    /// </summary>
    /// <remarks>
    /// The composition, in full:
    /// <code>
    /// map base + difficulty bonus + dice(1..basis) * party multiplier &gt; threshold
    /// </code>
    /// and what comes back is how many of the basis's dice values satisfy it.
    /// <para>
    /// <b>The first two of the five decisions this server makes about the archive's numbers are
    /// made here, and the archive states neither.</b>
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <b>The numbers are added, not multiplied.</b> <c>[dungeon difficulty drop bonusrate]</c> is
    /// a <i>bonus</i> rate by its own name, which is what a sum takes and not what a product does,
    /// and its first entry is zero -- under a product that zero would empty a whole face on the
    /// easiest run in the game, and under a sum it simply adds nothing. The inversion in that
    /// table is passed through as the archive wrote it: its fifth entry, 1200, is smaller than its
    /// fourth, 1400, so the fourth difficulty pays less than the third. Nothing here sorts or
    /// smooths it.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>The party multiplier multiplies the dice and nothing else.</b>
    /// <c>[party member drop bonusrate]</c> is the one fractional number in the composition, so it
    /// has to multiply something. Applied to the whole sum, a full party would carry every position
    /// of an ordinary clear outright -- 7400 times 1.6 is 11840 against a threshold of 10000, and
    /// 6000 times 1.6 plus 1400 is 11000 -- which nothing else in the file can do. On the
    /// dice it is a diminishing return instead: it raises the odds without ever making a position
    /// certain, and that is the shape this server chose it for.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>The threshold is <c>[drop prob]</c>, read as a bar rather than as a rate.</b> That is the
    /// fourth of the five; which profile's bar is taken is the other half of it, and both are named
    /// where the profile is chosen. What the reading rests on is the file's own numbers:
    /// <c>default</c> pays 10000 in every band while the profiles named for occasions pay less --
    /// 9000 for <c>event</c> and 8000 for <c>pcroom event</c> at the level this server's clears are
    /// fought at -- so read as a bar it is the named occasions that are the generous ones. A lower
    /// bar is a bonus, which is what an occasion named <c>event</c> ought to be.
    /// </para>
    /// <para>
    /// <b>This server's decision, and the archive says nothing about it: the comparison is carried
    /// out in whole numbers rather than in reals.</b> That is the fifth of the five, and the reason
    /// for it is <b>reproducibility</b> rather than accuracy -- the file states no way of comparing
    /// at all, so neither reading is the archive's and neither can be the more faithful one. A face
    /// on this server is fixed by its seed and has to be replayable from that seed afterwards, and
    /// a comparison carried out in reals is not replayable in that sense: floating-point
    /// contraction, a fused multiply-add and one JIT differing from another can each move the last
    /// bits of the same product, so two machines replaying one seed could deal different cards.
    /// Taking the multiplier to whole parts of the basis once (1.6 to 16000) and comparing whole
    /// numbers from there removes that whole class of divergence rather than narrowing it.
    /// </para>
    /// <para>
    /// There is a value where the two readings part, which is what makes this a decision rather
    /// than an implementation detail. The party multipliers are stored by the archive as singles,
    /// so 1.6 arrives as 1.60000002384 and 1.4 as 1.39999997616; compared as reals, a party of four
    /// at a 2600 shortfall lets the dice value 1625 through on the strength of the eighth decimal
    /// place, which is 8376 positions in ten thousand where the whole-number reading gives 8375.
    /// </para>
    /// <para>
    /// A sum that clears the threshold before the dice is rolled carries every position, and a
    /// shortfall the dice cannot make up carries none. Both are ordinary answers on a table this
    /// server has not seen; on A21 neither occurs, because the largest sum without the dice is
    /// 7400 against a threshold of 10000 and the smallest is 2000.
    /// </para>
    /// </remarks>
    /// <param name="mapBaseRate">See <see cref="MapBaseRate"/>.</param>
    /// <param name="difficultyBonus">
    /// What <c>[dungeon difficulty drop bonusrate]</c> declares for the run's difficulty.
    /// </param>
    /// <param name="partyRate">
    /// What <c>[party member drop bonusrate]</c> declares for the party's size, already a real.
    /// </param>
    /// <param name="threshold">
    /// What the sum has to pass: the <c>[drop prob]</c> band's rate for the occasion.
    /// </param>
    /// <param name="basis">See <see cref="ClearRewardRateOptions"/>.</param>
    public static ClearRewardChance ChanceOf(
        int mapBaseRate, int difficultyBonus, double partyRate, int threshold, int basis)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(basis);

        var shortfall = (long)threshold - mapBaseRate - difficultyBonus;
        if (shortfall < 0)
            return new ClearRewardChance(basis, basis);

        var scaledParty = double.IsFinite(partyRate) && partyRate > 0
            ? (long)Math.Round(partyRate * basis, MidpointRounding.AwayFromZero)
            : 0;
        if (scaledParty <= 0)
            return new ClearRewardChance(0, basis);

        // A dice value loses when its scaled contribution stays at or under the shortfall, so the
        // losers are the values from one up to the whole number below the crossing point.
        var losing = shortfall * basis / scaledParty;
        return new ClearRewardChance((int)Math.Clamp(basis - losing, 0L, basis), basis);
    }

    /// <summary>
    /// What a free card pays in gold: the level's base, scaled by the difficulty, moved either way
    /// inside the band the level declares.
    /// </summary>
    /// <remarks>
    /// <b>Three archive numbers and nothing else.</b> The base and the band are the second and
    /// third columns of <c>[gold drop ref table]</c>; the multiplier is
    /// <c>[dungeon difficulty gold drop bonusrate]</c>, which sits in the clear-reward file itself.
    /// Each can be pointed at a line of the archive.
    /// <para>
    /// <b>How the three combine is this server's, and is named here rather than hidden.</b> The
    /// archive declares the numbers and says nothing about their arithmetic. The A21 reference
    /// server multiplies the same three and then piles on four more factors -- a weighted kill
    /// count, a rank bonus, a division by the party size, and a bare <c>175/1000</c>. That last
    /// one appears twice in its source with no comment and no matching number anywhere in the
    /// archive, and this repository has already been bitten once by copying an unsourced constant
    /// out of it: a rarity denominator written as a flat million, which quietly put two whole
    /// bands out of reach. None of the four is taken.
    /// </para>
    /// <para>
    /// <b>The result is never zero.</b> A flipped free card pays whether or not anything was
    /// behind it -- that is what makes it the floor it is meant to be -- so an amount that rounds
    /// away comes back as one rather than as nothing. A level the archive gives no base for is the
    /// one case that pays nothing, because there is no figure to pay.
    /// </para>
    /// </remarks>
    /// <param name="baseAmount">What one gold drop is worth at the dungeon's basis level.</param>
    /// <param name="difficultyGoldRate">What this difficulty multiplies that by.</param>
    /// <param name="variancePercent">How far either way the amount may move, as a percentage.</param>
    /// <param name="random">The run's own generator, so a clear pays the same on every asking.</param>
    public static int CardGoldOf(
        int baseAmount, double difficultyGoldRate, int variancePercent, DropRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (baseAmount <= 0 || !double.IsFinite(difficultyGoldRate) || difficultyGoldRate <= 0)
            return 0;

        var scaled = baseAmount * difficultyGoldRate;
        if (variancePercent > 0)
        {
            var moved = random.Next((2 * variancePercent) + 1) - 1 - variancePercent;
            scaled += scaled * moved / 100.0;
        }

        var amount = (long)Math.Round(scaled, MidpointRounding.AwayFromZero);
        return amount <= 0 ? 1 : (int)Math.Min(amount, int.MaxValue);
    }


    /// <summary>
    /// One row drawn from a card's fallback table by the weights the archive wrote, or nothing
    /// when the table has no row to draw.
    /// </summary>
    /// <remarks>
    /// Weighted exactly as <see cref="DropRules.Pick"/> weights an item out of a pool: the
    /// weights are summed, one value of that sum is rolled, and the row the running total first
    /// reaches is the one drawn. The archive writes its fallback rows the way it writes a pool --
    /// a weight in the first column -- so drawing them any other way would be a second reading of
    /// the same column.
    /// <para>
    /// <b>A table with no row to draw is answered before the generator is touched.</b> That is
    /// what lets a caller pass an empty list to mean "this card hands out nothing" and get back
    /// the face it would have had if it had never asked: no value of the generator is spent, so
    /// nothing after it moves. A row weighted at zero is not a row to draw, the same way a rarity
    /// band of width zero is not a band -- the archive turns a row off by weighting it out.
    /// </para>
    /// <para>
    /// A21's six paid rows sum to exactly ten thousand and its one PC-room row to fifty, so
    /// neither table's weights are shares of anything fixed; what a weight is a share of is the
    /// sum of the weights beside it, which is the only reading that fits both.
    /// </para>
    /// </remarks>
    /// <param name="rows">
    /// <c>[gold card blank item]</c> or <c>[pcroom card blank item]</c> as the table read it, or
    /// no rows at all for a card that is not to hand anything out.
    /// </param>
    /// <param name="random">
    /// The face's own generator, at whatever value the face has reached.
    /// </param>
    public static ClearRewardBlankItem? BlankItemOf(
        IReadOnlyList<ClearRewardBlankItem> rows, DropRandom random)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(random);

        var total = 0L;
        foreach (var row in rows)
            total += Math.Max(0, row.Weight);
        if (total <= 0 || total > int.MaxValue)
            return null;

        var roll = random.Next((int)total);
        var running = 0L;
        foreach (var row in rows)
        {
            running += Math.Max(0, row.Weight);
            if (roll <= running)
                return row;
        }

        return rows[^1];
    }
}
