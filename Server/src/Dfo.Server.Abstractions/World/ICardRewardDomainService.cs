using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// One clear, as much of it as fixing that clear's cards needs and the run does not carry.
/// </summary>
/// <remarks>
/// Two facts, and deliberately only two. Everything else the occasion carries is taken off the run
/// the character is in rather than handed in beside it, so that no caller can settle a clear
/// against a difficulty or a maze the run does not have.
/// </remarks>
/// <param name="CharacterLevel">
/// Which of the threshold profile's level bands this clear is drawn against, and nothing else.
/// Handed in, because what the caller knows is a character and what the settlement knows is a run.
/// <para>
/// <b>Not the grade band, and no longer the price.</b> Both of those are reached by the dungeon's
/// own <c>[basis level]</c>, which the run carries and this record therefore does not: see
/// <see cref="ClearRewardOccasion.DungeonBasisLevel"/> for the reading and for the gear tap the
/// other way round opened. That this one band is still the character's is itself a decision and
/// not a leftover -- it is argued on <see cref="ClearRewardOccasion.CharacterLevel"/>, and it is
/// held down by a judgement that would go red if it were quietly changed either way.
/// </para>
/// </param>
/// <param name="PartyMemberCount">
/// How many were in the party, counted from one. A parameter rather than a constant for the reason
/// <see cref="MonsterKill.PartyMemberCount"/> is one.
/// </param>
public sealed record ClearRewardSettlement(
    CharacterId CharacterId, int CharacterLevel, int PartyMemberCount = 1);

/// <summary>
/// What settling a clear came to: its cards, and what a paid flip of it costs.
/// </summary>
/// <remarks>
/// The price travels with the cards rather than being fetched beside them, and that is the point
/// of this type. It is a fact about the dungeon the clear was fought in -- the row of
/// <c>[gold card cost table]</c> that the dungeon's own <c>[basis level]</c> reaches -- so a
/// caller that wanted it separately would have to hold the run in order to ask correctly, and
/// holding the run is this seam's business rather than a caller's.
/// <para>
/// <b>It was once a fact about the character's level, and what went wrong then is worth keeping
/// written down.</b> The level a clear settles at is read before the clear's own experience is
/// paid and every flip of it happens after, so a price keyed on the character moved between the
/// figure the result screen announced and the figure a flip would take -- a server quoting one
/// price and taking another. Fixing the price with the cards is what this type is; what has gone
/// is the premise rather than the fix. A
/// dungeon does not level, so there is no second figure left for the two to disagree about.
/// </para>
/// <para>
/// The judgement that pinned it is kept all the same --
/// <c>PaidCardFlipCostTests.A_clear_that_levelled_the_character_still_charges_what_it_announced</c>,
/// which settles a clear at one level and turns a card at the next. It holds a weaker claim now
/// than it was written for, and holds it for free; what it would still catch is anybody keying
/// this back on a character, which is the only way the old defect can return.
/// </para>
/// <para>
/// Which is also why it is never worked out twice. The figure here is always the one
/// <see cref="ClearRewardCardSet.PaidFlipCost"/> was fixed with, so every settlement of one clear
/// answers the figure the first one did, whatever level it is asked at.
/// </para>
/// </remarks>
/// <param name="Cards">
/// The clear's two faces, which of their positions have been claimed, and what a paid flip of
/// them costs.
/// </param>
/// <param name="PaidFlipCost">
/// What a paid flip of this clear costs, or null when this clear names no price. Always
/// <see cref="ClearRewardCardSet.PaidFlipCost"/> of <paramref name="Cards"/> -- repeated here so
/// that a caller with the settlement in hand does not have to reach through the cards for the one
/// figure the frame beside them carries.
/// <para>
/// <b>Null has two causes and this type carries neither of them separately.</b> The archive prices
/// a flip by level and answers nothing for a level it does not list; and a dungeon may switch its
/// own paid card off (<see cref="IClearRewardRuleService.OffersPaidFlipAsync"/>), which leaves the
/// clear with no flip for a price to be about. <see cref="ICardRewardDomainService.SettleAsync"/>
/// composes the two into one absence on purpose -- both refuse the flip and both cost the player
/// nothing -- so a caller reading this null may not conclude which of them happened.
/// </para>
/// <para>
/// Null rather than zero, for the reason <see cref="IClearRewardRuleService.PaidFlipCost"/> gives:
/// zero is a price -- a free flip -- and neither an unpriced level nor a switched-off dungeon is a
/// free one. What is said about such a clear on the wire is a decision about a frame, and is made
/// where that frame is built.
/// </para>
/// </param>
public sealed record ClearRewardSettlementResult(ClearRewardCardSet Cards, int? PaidFlipCost);

/// <summary>
/// A clear's cards: fixed once when the clear settles, handed over one position at a time.
/// </summary>
/// <remarks>
/// Where a clear's two faces are decided and where each one's seed is prepared. That division is
/// the point of this seam: <see cref="IClearRewardRuleService"/> answers what one occasion and one
/// seed come to, and how many faces a clear has and which facts each of them is drawn from is a
/// question about the world, which a rule service may not ask.
/// <para>
/// <b>The two faces are given different seeds here, and this is where that difference is made.</b>
/// The seed of a face is the caller's -- see <see cref="IClearRewardRuleService.FaceFor"/> -- and
/// with every fallback switch off, and all three are, two faces handed one seed have no other way
/// left to differ. A switched-on paid card would part them at their blank positions
/// (<see cref="ClearRewardBlankItemSwitches"/>), and that makes the case for two seeds stronger
/// rather than weaker: whether a clear's two faces differ may not be a function of a configuration
/// key. The seeds are derived from the run rather than drawn, because a clear settled twice has to
/// fix the same cards both times.
/// </para>
/// </remarks>
public interface ICardRewardDomainService
{
    /// <summary>
    /// Fixes this clear's two faces, or answers the ones it already has.
    /// </summary>
    /// <remarks>
    /// Idempotent: a clear whose cards are already fixed keeps them, and the rules are not asked a
    /// second time -- not for a face, and not for a price either. Null when the character is in no
    /// run.
    /// <para>
    /// What comes back is the whole settlement rather than the cards alone -- see
    /// <see cref="ClearRewardSettlementResult"/> for why the paid flip's price belongs to the same
    /// answer, and <see cref="ClearRewardCardSet.PaidFlipCost"/> for why it is fixed with them
    /// rather than answered afresh.
    /// </para>
    /// </remarks>
    ValueTask<ClearRewardSettlementResult?> SettleAsync(
        ClearRewardSettlement settlement, CancellationToken cancellationToken);

    /// <summary>
    /// Takes one position of one of this clear's faces, and answers what was behind it.
    /// </summary>
    /// <remarks>
    /// Null when there was nothing to take: no run, no cards fixed yet, no position under that
    /// number, or that position already claimed. A position that is claimed and holds nothing
    /// answers a slot, not null.
    /// </remarks>
    ValueTask<ClearRewardCardSlot?> ClaimAsync(
        CharacterId characterId,
        ClearRewardCardKind kind,
        int index,
        CancellationToken cancellationToken);
}
