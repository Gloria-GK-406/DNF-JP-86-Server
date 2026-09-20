using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

/// <summary>
/// Fixing a clear's cards, and handing one of them over.
/// </summary>
/// <remarks>
/// The order is fixed and only the middle step is anybody else's: this prepares each face's
/// occasion and each face's seed, the rules say what that occasion and that seed come to, and the
/// run keeps the answer. Nothing here decides what a card holds, and nothing here reads the
/// archive; the only two things it is handed are the rules and the run.
/// </remarks>
internal sealed class CardRewardDomainService(
    IClearRewardRuleService cardRules,
    IDungeonRunDomainService runs) : ICardRewardDomainService
{
    public async ValueTask<ClearRewardSettlementResult?> SettleAsync(
        ClearRewardSettlement settlement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settlement);

        if (runs.Find(settlement.CharacterId) is not { } run
            || (settlement.RunId != default && settlement.RunId != run.RunId))
            return null;

        // Already fixed: hand back what was fixed rather than drawing a second pair of faces and
        // throwing them away. The run keeps the pair it has whatever arrives; not drawing a second
        // one is what keeps a re-sent result report from doing the whole draw again.
        //
        // The price comes back off the run with them, and the rules are not asked. That is a
        // reversal of an earlier reading of this seam, and the argument it lost to is worth
        // keeping: a price is the archive's answer about a level, so asking again rather than
        // keeping a copy means no copy can outlive the file it came from. True, and beaten by
        // consistency. The level a clear settles at is taken before the clear's own experience is
        // paid and the level a flip happens at is taken after, so recomputing here charges a
        // levelled character something other than the figure the result screen announced -- a
        // server quoting one price and taking another. Within one run the file cannot move; the
        // announced figure can, and must not.
        if (run.ClearRewardCards is { } already)
            return new ClearRewardSettlementResult(already, already.PaidFlipCost);

        // What a paid flip of this clear costs, asked of the rules and of nothing else, once, at
        // the basis level of the dungeon it was fought in. Null here is the archive pricing no
        // such level, and it stays null all the way out -- turning it into a zero would be this
        // seam handing out a free flip nobody priced.
        //
        // The level comes off the run rather than off the settlement, and that is the whole of
        // this task's change here. A clear is priced by what was cleared: dungeon 144 is set at
        // [basis level] 5 and costs 580 to turn a card in, whether the character standing in it
        // is level 5 or level 86. See ClearRewardOccasion.DungeonBasisLevel. It also settles the
        // quoting problem the block above argues about rather than merely surviving it -- the
        // dungeon's level cannot move between the announcement and the flip, where the
        // character's could and did -- but the block above stays, because what keeps the two
        // figures equal has to be the run keeping its answer and not a fact that happens to sit
        // still.
        //
        // Two questions, because they are two questions. What a flip costs is the archive's cost
        // table answering about a level; whether this clear has a flip to cost at all is the
        // dungeon answering about itself, and a dungeon that has switched its paid card off names
        // no price rather than a price of nothing -- see ClearRewardSettlementResult.PaidFlipCost
        // for why a price of nothing is a giveaway and not a refusal.
        //
        // Composed here rather than inside either answer: the rule service owns both readings and
        // owns neither's use, and this is the seam that decides what one settlement says.
        var cost = await cardRules
            .OffersPaidFlipAsync(run.Layout.DungeonId, cancellationToken).ConfigureAwait(false)
            ? cardRules.PaidFlipCost(run.Layout.BasisLevel)
            : null;

        var free = this.FaceFor(run, settlement, ClearRewardCardKind.Free);
        var paid = this.FaceFor(run, settlement, ClearRewardCardKind.Paid);

        // The answer's price is read back off the cards rather than taken from the local, so that
        // a settlement which lost the race to another one reports the winner's price beside the
        // winner's faces instead of its own beside somebody else's.
        var fixedRun = settlement.RunId == default
            ? runs.FixClearRewardCards(settlement.CharacterId, free, paid, cost)
            : runs.FixClearRewardCards(
                settlement.CharacterId, settlement.RunId, free, paid, cost);
        return fixedRun?.ClearRewardCards
            is { } fixedCards
            ? new ClearRewardSettlementResult(fixedCards, fixedCards.PaidFlipCost)
            : null;
    }

    public ValueTask<ClearRewardCardSlot?> ClaimAsync(
        CharacterId characterId,
        ClearRewardCardKind kind,
        int index,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(runs.TryClaimClearRewardCard(characterId, kind, index));

    /// <summary>
    /// One face of this clear: the occasion it is drawn for, and the seed it is drawn from.
    /// </summary>
    /// <remarks>
    /// The occasion's facts come off the run wherever the run has them -- the difficulty it was
    /// entered at, how many rooms the maze it was given holds, and what level the dungeon is set
    /// at -- and off the settlement where it does not.
    /// <para>
    /// The dungeon's basis level is the run's own, carried on the layout for the reason every
    /// other archive fact about the dungeon is: a run knows its layout, and asking the dungeon
    /// file again here would make this a second reader of content. It is what the grade band is
    /// taken at, and what the price above is taken at.
    /// </para>
    /// <para>
    /// How far the run got is no longer among these facts. It was passed for a task after the
    /// composition stopped reading it; the field it was passed into is gone, and the run still
    /// carries the rooms it walked into for anything that does need them.
    /// </para>
    /// </remarks>
    private ClearRewardCardFace FaceFor(
        DungeonRunState run, ClearRewardSettlement settlement, ClearRewardCardKind kind) =>
        cardRules.FaceFor(
            new ClearRewardOccasion(
                kind,
                settlement.CharacterLevel,
                run.Layout.BasisLevel,
                run.Difficulty,
                run.Layout.Rooms.Count,
                settlement.PartyMemberCount)
            {
                Kills = run.Kills,
            },
            SeedFor(run, kind));

    /// <summary>
    /// The seed one face of one clear is drawn from.
    /// </summary>
    /// <remarks>
    /// Both halves are facts the run already carries: the seed its maze was laid out from in the
    /// high thirty-two bits, and the dungeon it is a run of in the low ones. <b>Neither a clock
    /// nor a shared generator is read here</b>, and that is the requirement rather than a
    /// preference -- a settlement that consulted either would fix different cards the second time
    /// it ran, and a re-sent result report is exactly that second time.
    /// <para>
    /// The kind goes in by exclusive or with the dungeon, and with every fallback switch off --
    /// and all three are -- that is what makes the two faces of one clear differ. A switched-on
    /// paid card would part them at their blank positions as well
    /// (<see cref="ClearRewardBlankItemSwitches"/>), which is a reason to derive the two seeds
    /// apart rather than a reason not to: whether a clear's two faces differ may not be a function
    /// of a configuration key. Exclusive or rather than a mix, so that two distinct kinds can never
    /// come out as one seed for any dungeon whatever.
    /// </para>
    /// </remarks>
    private static ulong SeedFor(DungeonRunState run, ClearRewardCardKind kind) =>
        ((ulong)(uint)run.Layout.Seed << 32) | ((uint)run.Layout.DungeonId ^ (uint)kind);
}
