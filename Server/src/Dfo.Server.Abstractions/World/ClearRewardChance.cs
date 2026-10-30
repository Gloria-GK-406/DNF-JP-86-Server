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
/// <c>ClearRewardRules.ChanceOf</c> counts the dice values that pass the threshold, and
/// <see cref="DropRandom.Hits(int, int)"/> lets a roll through when it comes in at or under that
/// count. The two put the winning half of the dice at opposite ends -- the composition's winners
/// are the high values, the roll's are the low ones -- and nothing turns on which end, because the
/// dice is uniform and only how many values win decides anything.
/// </para>
/// <para>
/// Where the basis comes from is answered in one place: <c>ClearRewardRateOptions</c>.
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
    /// <c>ClearRewardBlankItemSwitches.FreeCard</c>.
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
    /// <c>ClearRewardBlankItemSwitches.PcRoomCard</c>.
    /// </remarks>
    PcRoom = 2,
}
