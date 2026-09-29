using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>
/// The one reader of the clear-reward rate table, and the only caller of the clear-reward rules.
/// </summary>
/// <remarks>
/// Three content sources and one named assumption, and nothing else. It is handed no repository,
/// no registry, no directory, no store and no domain service, because a rule service that could
/// look something up would become a second owner of whatever it looked up; every fact it works
/// from arrives in the occasion it is asked about.
/// <para>
/// The third source is the dungeon list, and it is read for one thing only: whether a dungeon has
/// switched its own paid card off. It is not read for a card's contents. The one dungeon-level
/// declaration about contents that the archive carries -- <c>[result card]</c> -- is declared by
/// four dungeons, all four of which declare no <c>[map specification]</c> and so have no
/// definition to read here at all, so nothing is built on it.
/// </para>
/// <para>
/// The order of the work is fixed, and only its first step is new here. Composing
/// the archive's numbers into one chance is new and lives in <see cref="ClearRewardRules"/>.
/// Rolling a quality, taking the level's grade band and drawing one
/// weighted piece are the existing drop parts, called here rather than restated: the quality is
/// rolled by walking the table's own <see cref="RarityLadder"/> -- a roll of one to its
/// <see cref="RarityLadder.HighestRoll"/>, then the band that roll lands in -- and the draw is
/// <see cref="DropRules.Pick"/>. Both steps are the archive's ladder and the archive's weights,
/// with no second copy of either living here.
/// </para>
/// <para>
/// A step after those, and one nothing reaches as this server is deployed: a position that was
/// <b>drawn</b> and came up holding nothing is handed the fallback the archive declares for its
/// card, <b>if that card's switch is on, and none of the three is</b>. See
/// <see cref="ClearRewardBlankItemSwitches"/> for the switches and what each is off for,
/// <see cref="FallbackRowsFor"/> for which rows belong to which card, and <see cref="Undrawn"/>
/// for why an occasion the tables could not answer is handed nothing even with a switch on.
/// </para>
/// <para>
/// <b>Only equipment is drawn.</b> The file declares a section of four item-type rates -- see the
/// remarks on <see cref="ClearRewardTable.ItemTypeRates"/> -- and it is deliberately not applied
/// here as a second gate, and that is a choice rather than an inability: the A21 reference server
/// reads the four positions as item types one to four and rolls them, so A21's <c>0 9500 0 500</c>
/// is ordinary equipment at 9500 and avatar at 500. What keeps
/// the gate out of here is the owner's decision of 2026-08-31 that this phase hands out equipment
/// and no avatar, which a second roll would only be a slower way of saying. If the avatar branch
/// is ever wanted, this is where it goes.
/// </para>
/// </remarks>
internal sealed class ClearRewardRuleService(
    IClearRewardRateSource rates,
    IGoldDropTableSource goldTables,
    IDropItemPoolSource pools,
    IDungeonDefinitionSource dungeons,
    ClearRewardRateOptions options) : IClearRewardRuleService
{
    // 70Reference: profiles 3/4/5 are pcroom default/event/bonus.
    // Ordinary non-premium cards retain their existing default profile.
    private const string ThresholdProfile = ClearRewardProfiles.Default;

    private static string ProfileFor(ClearRewardOccasion occasion) => occasion.Kind switch
    {
        ClearRewardCardKind.BlackDiamond => ClearRewardProfiles.PcRoomBonus,
        ClearRewardCardKind.Free when occasion.HasBlackDiamond => ClearRewardProfiles.PcRoomDefault,
        ClearRewardCardKind.Paid when occasion.HasBlackDiamond => ClearRewardProfiles.PcRoomEvent,
        _ => ThresholdProfile,
    };

    /// <summary>
    /// The odds one position of this occasion carries.
    /// </summary>
    /// <remarks>
    /// Four lookups of the archive and one composition. The lookups are the file's own tables and
    /// each answers null for an entry it does not declare; the composition, and every decision
    /// inside it, is <see cref="ClearRewardRules.ChanceOf"/>'s.
    /// <para>
    /// A null from any of the four comes back impossible rather than at some neighbour's value --
    /// see <see cref="IClearRewardRuleService.ChanceOf"/> for why, and for why a room count with no
    /// row of its own is not one of them.
    /// </para>
    /// <para>
    /// <b>The band is taken at the character's level, and it is the one lookup on this path that
    /// is.</b> The grade band and the paid flip's price are both the dungeon's -- see
    /// <see cref="ClearRewardOccasion.DungeonBasisLevel"/> -- and this one is deliberately not,
    /// for the reason set out on <see cref="ClearRewardOccasion.CharacterLevel"/>: the six
    /// profiles this band lives inside are named for the player's circumstance and not for the
    /// dungeon. Nothing on this archive can tell the two readings apart, because
    /// <c>default</c> pays 10000 in all four of its bands, so the choice is written down and
    /// pinned against a table whose bands differ rather than left to be rediscovered.
    /// </para>
    /// </remarks>
    public ClearRewardChance ChanceOf(ClearRewardOccasion occasion)
    {
        ArgumentNullException.ThrowIfNull(occasion);

        var table = rates.Load(DropRateTables.ClearReward);
        if (table.ProfileNamed(ProfileFor(occasion))?.BandFor(occasion.CharacterLevel) is not { } band
            || table.DifficultyRate((int)occasion.Difficulty) is not { } difficulty
            || table.PartyMemberRate(occasion.PartyMemberCount) is not { } party
            || ClearRewardRules.MapBaseRate(table.MapCountRates, occasion.MapMaxCount)
                is not { } mapBase)
        {
            return new ClearRewardChance(0, options.RateBasis);
        }

        return ClearRewardRules.ChanceOf(
            mapBase, difficulty, party, band.Rate, options.RateBasis);
    }

    /// <summary>
    /// What the archive prices a paid flip at, for a dungeon of that basis level.
    /// </summary>
    /// <remarks>
    /// One lookup of <c>[gold card cost table]</c>, and deliberately nothing else. The four bonus
    /// tables this file declares are keyed on the occasion a face is drawn for and not on what
    /// turning one over costs, so there is nothing here to compose with the price; a difficulty or
    /// a party size applied to it would be a tariff this server invented.
    /// <para>
    /// The table's null goes back out unchanged. See
    /// <see cref="IClearRewardRuleService.PaidFlipCost"/> and
    /// <see cref="ClearRewardTable.GoldCardCost"/> for why it may not become a zero on the way.
    /// </para>
    /// <para>
    /// <b>The key is the dungeon's <c>[basis level]</c>, and that is a change with a hole behind
    /// it.</b> Priced by the character, a level 86 character clearing dungeon 144 -- which the
    /// archive sets at level 5 -- was quoted 10300 where that dungeon's own row of this table says
    /// 580. The reading is argued on <see cref="ClearRewardOccasion.DungeonBasisLevel"/>; what
    /// it rests on is the archive's own <c>[mob level charac level replace flag]</c> and this
    /// repository already pricing a clear's experience the same way, not the reference server
    /// agreeing -- though it does, its <c>GetPaidCardCost</c> reaching this table by the same key.
    /// </para>
    /// <para>
    /// <b>The null is still this server's own.</b> Where the table has no row the reference server
    /// walks back to the nearest lower level and finally to the lowest, so it never answers "not
    /// priced" at all. That behaviour is not adopted here.
    /// </para>
    /// </remarks>
    public int? PaidFlipCost(int dungeonBasisLevel) =>
        rates.Load(DropRateTables.ClearReward).GoldCardCost(dungeonBasisLevel);

    /// <summary>
    /// Whether the dungeon this clear was fought in offers a paid flip.
    /// </summary>
    /// <remarks>
    /// One lookup of one dungeon, and the only thing read off it is its own switch. The dungeon
    /// file is content and this service is where content is read, which is the whole of why the
    /// question is answered here rather than beside the flip.
    /// <para>
    /// <b>Silence is not a refusal.</b> A dungeon that answers nothing -- one the archive does not
    /// declare, or one it declares without a maze -- leaves the paid flip offered, because the
    /// declaration that switches it off is a declaration and its absence is the ordinary case.
    /// See <see cref="IClearRewardRuleService.OffersPaidFlipAsync"/>.
    /// </para>
    /// </remarks>
    public async ValueTask<bool> OffersPaidFlipAsync(
        int dungeonId, CancellationToken cancellationToken) =>
        await dungeons.GetAsync(dungeonId, cancellationToken).ConfigureAwait(false)
            is not { OffersPaidCard: false };

    /// <summary>
    /// Every position of one face, decided in one call.
    /// </summary>
    /// <remarks>
    /// <b>The grade band is the dungeon's, taken at its <c>[basis level]</c>.</b> What a clear
    /// hands out is a property of what was cleared, so the same dungeon bands the same way for
    /// every character who clears it; see <see cref="ClearRewardOccasion.DungeonBasisLevel"/>. Read
    /// at the character's level instead -- which this server did until this revision -- a level 86
    /// character clearing a dungeon the archive sets at level 5 drew level 86 equipment out of it.
    /// <para>
    /// A level the grade table declares no row for draws nothing rather than throwing, and that is
    /// separate from the odds: the profile's bands span level ranges and the grade table declares
    /// single levels, so an occasion can carry a chance and still have no band to draw from.
    /// </para>
    /// </remarks>
    public ClearRewardCardFace FaceFor(ClearRewardOccasion occasion, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(occasion);

        var table = rates.Load(DropRateTables.ClearReward);

        // The gold first, and from its own generator. A card pays whether or not anything was
        // drawn behind it, so this must not sit downstream of the draw -- and it must not consume
        // values of the draw's generator either, or adding it would move every position of every
        // face this server has ever dealt.
        var gold = this.GoldOf(occasion, table, new DropRandom(seed));

        var guarantee = options.DebugGuaranteePaidEquipment && occasion.Kind is ClearRewardCardKind.Paid;
        var chance = guarantee ? new ClearRewardChance(options.RateBasis, options.RateBasis) : this.ChanceOf(occasion);
        if (chance.IsImpossible
            || table.Rarity is not { } ladder
            || ladder.HighestRoll <= 0
            || table.GradeBandFor(occasion.DungeonBasisLevel) is not { } grades)
        {
            return Undrawn(occasion.Kind, gold);
        }

        var fallback = this.FallbackRowsFor(table, occasion.Kind);
        var pool = pools.Load(DropItemFamily.Equipment);
        var random = new DropRandom(seed);
        var slots = new List<ClearRewardCardSlot>(ClearRewardRules.CardSlotCount);
        for (var index = 0; index < ClearRewardRules.CardSlotCount; index++)
        {
            slots.Add(
                new ClearRewardCardSlot(
                    index,
                    guarantee
                        ? GuaranteedEquipment(grades, pool, random)
                        : Draw(chance, ladder, grades, pool, random) ?? Fallback(fallback, random)));
        }

        return new ClearRewardCardFace(occasion.Kind, slots, gold);
    }

    /// <summary>
    /// Which rows a drawn position of this face falls back on when it comes up holding nothing,
    /// and none at all when the card's switch is off.
    /// </summary>
    /// <remarks>
    /// <b>Two questions, and the archive answers only one of them.</b> Which section a card falls
    /// back on is the file's -- <c>[gold card blank item]</c> is the paid card's and the free card
    /// has no section of that shape at all -- and whether a card falls back is this server's, one
    /// switch per card, all off. See <see cref="ClearRewardBlankItemSwitches"/> for what each is
    /// off for.
    /// <para>
    /// <b>Off means no rows, and no rows means no roll.</b> An empty list is handed on rather than
    /// a flag, so a switched-off card never reaches
    /// <see cref="ClearRewardRules.BlankItemOf"/> with anything to draw and never spends a value
    /// of the generator on one. That is what makes the switched-off face the same face this
    /// service dealt before the switches existed, position for position, and not merely a face
    /// with nothing in the empty positions.
    /// </para>
    /// <para>
    /// The PC-room card is absent from this mapping because it is absent from
    /// <see cref="ClearRewardCardKind"/>: the flip carries two kinds and there is no third to map.
    /// Its rows and its switch are read all the same; see
    /// <see cref="ClearRewardBlankCard.PcRoom"/>.
    /// </para>
    /// </remarks>
    private IReadOnlyList<ClearRewardBlankItem> FallbackRowsFor(
        ClearRewardTable table, ClearRewardCardKind kind)
    {
        var card = kind switch
        {
            ClearRewardCardKind.Paid => ClearRewardBlankCard.GoldCard,
            ClearRewardCardKind.BlackDiamond => ClearRewardBlankCard.PcRoom,
            _ => ClearRewardBlankCard.Free,
        };
        if (!options.BlankItems.Allows(card))
            return [];

        // The free card's rows are the empty list because the archive declares it no section, and
        // it is not lent the paid card's; see ClearRewardBlankCard.Free.
        return card switch
        {
            ClearRewardBlankCard.GoldCard => table.GoldCardBlankItems,
            ClearRewardBlankCard.PcRoom => table.PcRoomCardBlankItems,
            _ => [],
        };
    }

    /// <summary>
    /// <b>The face for an occasion the tables could not answer: four positions, nothing in any of
    /// them, and no draw made.</b>
    /// </summary>
    /// <remarks>
    /// <b>A rule, and named for the one thing every way in has in common: no dice was thrown.</b>
    /// <b>One call site and four ways in</b> -- the four are the conditions of the single
    /// <c>if</c> in <see cref="FaceFor"/>, not four callers: an occasion the rate tables do not
    /// describe, a file that has lost its rarity section, a ladder no roll can land in, or a grade
    /// table with no row for the dungeon's own level. None of them is a position that was drawn
    /// and came up empty.
    /// <para>
    /// <b>The first of the four is wider than "a table did not answer", and only this archive
    /// hides the difference.</b> What it tests is
    /// <see cref="ClearRewardChance.IsImpossible"/>, which is also true where all four tables
    /// answered and the composition still came to nothing: a party multiplier scaling to zero, or
    /// a shortfall the dice cannot make up. Neither is reachable on A21 -- its party multipliers
    /// run 1 to 1.6, and the largest shortfall its tables can produce is 8000 against a basis of
    /// 10000 -- so on this file every arrival here really is a table that said nothing. On a file
    /// where one of them is reachable, a position drawn at odds of zero would arrive here and be
    /// handed no fallback, and that would be a decision rather than the description above.
    /// </para>
    /// <para>
    /// <b>That is why no fallback is handed out here, switched on or not.</b> The archive's
    /// <c>[gold card blank item]</c> answers "the dice was thrown and this position is still
    /// empty", and throwing it presupposes that every table it is thrown against answered. Paying
    /// a fallback for a table that said nothing would hand the player four items exactly where
    /// this server failed to read the archive -- a visible defect turned into a silent payout, and
    /// the opposite of what the rest of this path does with a table that says nothing. Pinned by
    /// <c>ClearRewardRuleServiceTests</c>'s
    /// <c>Blank_items_are_not_handed_out_where_the_tables_could_not_answer</c>.
    /// </para>
    /// <para>
    /// It touches neither the pool nor the generator, so it costs an unanswerable occasion no read
    /// of the whole equipment family. That is a saving, but the saving is not what it is for: with
    /// a fallback switched on, drawing four positions at a chance of zero and answering here are
    /// two different answers, and this is the one an unanswered table gets.
    /// </para>
    /// <para>
    /// <b>On this archive no ordinary clear reaches here.</b> Every occasion its tables describe
    /// carries some chance, the lowest difficulty included: that difficulty adds nothing to the
    /// sum, and under an additive composition adding nothing is not the same as paying nothing.
    /// What reaches here is an occasion outside the tables -- a level in no band, a difficulty or a
    /// party size the file does not list -- and a file with no map-count rows at all.
    /// </para>
    /// </remarks>
    private static ClearRewardCardFace Undrawn(ClearRewardCardKind kind, int gold) =>
        new(
            kind,
            [.. Enumerable.Range(0, ClearRewardRules.CardSlotCount)
                .Select(index => new ClearRewardCardSlot(index, null))],
            gold);

    /// <summary>
    /// What a flip of this face pays, from the three archive numbers that describe it.
    /// </summary>
    /// <remarks>
    /// The level's base and the band it moves inside are the second and third columns of
    /// <c>[gold drop ref table]</c>, taken at the dungeon's basis level for the same reason the
    /// paid flip's price is -- what a clear pays is a fact about the dungeon, not about who walked
    /// into it. The multiplier is <c>[dungeon difficulty gold drop bonusrate]</c>, out of the
    /// clear-reward file itself.
    /// <para>
    /// A difficulty the file does not list pays nothing rather than paying the base unscaled:
    /// there is no figure for it, and inventing the neutral multiplier would be indistinguishable
    /// from the archive having declared one.
    /// </para>
    /// </remarks>
    private int GoldOf(ClearRewardOccasion occasion, ClearRewardTable table, DropRandom random)
    {
        // The free card's floor, and only the free card's. What a paid flip hands over is bought
        // with the price the archive puts on it; paying a floor on top of that would be this
        // server inventing a second reward for a card the player already paid for. The reference
        // server's own gold path is named for the free card too.
        if (occasion.Kind is not ClearRewardCardKind.Free)
            return 0;

        if (table.DifficultyGoldRate((int)occasion.Difficulty) is not { } difficultyRate)
            return 0;

        var gold = goldTables.Load(DropRateTables.ClearReward);
        return ClearRewardRules.CardGoldOf(
            gold.BaseFor(occasion.DungeonBasisLevel),
            difficultyRate,
            gold.VarianceFor(occasion.DungeonBasisLevel),
            random);
    }

    /// <summary>
    /// What one position that was drawn and came up holding nothing is handed: one row of the
    /// card's fallback table, or nothing when the card has no rows to fall back on.
    /// </summary>
    /// <remarks>
    /// <b>The quality and the durability are this server's and the archive declares neither.</b> A
    /// fallback row is three numbers -- a weight, an item and a quantity -- so there is no ladder
    /// roll behind it and nothing to take a quality from; it is carried at the ladder's lowest,
    /// which is the value a slot with no ladder is given everywhere else in this repository (see
    /// <see cref="DropRules.RollRarity"/>). The durability is zero because both items the archive
    /// names are stackables -- <c>.stk</c> files, not <c>.equ</c> ones -- and a stackable carries
    /// none.
    /// </remarks>
    private static ClearRewardCardItem? Fallback(
        IReadOnlyList<ClearRewardBlankItem> fallback, DropRandom random) =>
        ClearRewardRules.BlankItemOf(fallback, random) is { } row
            ? new ClearRewardCardItem(row.ItemId, row.Quantity, DropRarity.Common, 0)
            : null;

    /// <summary>
    /// What one position comes to: nothing, or one piece of equipment.
    /// </summary>
    /// <remarks>
    /// Every position rolls its odds against the same generator in turn, so the four are
    /// independent of each other and all four are reproduced by the seed alone.
    /// <para>
    /// A position whose odds come in and whose bucket is empty holds nothing, and that is an
    /// ordinary answer rather than an error. The ladder's rarest bands are one roll wide apiece,
    /// and a grade band can hold nothing at the quality that was rolled; refusing here would turn a
    /// quiet miss into a clear that fails.
    /// </para>
    /// </remarks>
    private static ClearRewardCardItem GuaranteedEquipment(
        ItemGradeBand grades, DropItemPool pool, DropRandom random)
    {
        foreach (var rarity in Enum.GetValues<DropRarity>())
        {
            if (DropRules.Pick(pool, grades, rarity, random) is { } item)
                return new ClearRewardCardItem(item.ItemId, 1, rarity, item.Durability);
        }
        throw new InvalidOperationException("Guaranteed gold-card equipment has no eligible equipment pool.");
    }

    private static ClearRewardCardItem? Draw(
        ClearRewardChance chance,
        RarityLadder ladder,
        ItemGradeBand grades,
        DropItemPool pool,
        DropRandom random)
    {
        if (!random.Hits(chance.Rate, chance.Basis))
            return null;

        var rarity = (DropRarity)ladder.BandOf(random.Next(ladder.HighestRoll));
        return DropRules.Pick(pool, grades, rarity, random) is { } drawn
            ? new ClearRewardCardItem(drawn.ItemId, 1, rarity, drawn.Durability)
            : null;
    }
}
