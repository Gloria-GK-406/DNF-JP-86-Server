using Dfo.Server.Data.World;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

/// <summary>Reward categories. Only Free and Paid are selectable wire request values.</summary>
/// <remarks>BlackDiamond is an extra reward delivered with the free card.</remarks>
public enum ClearRewardCardKind
{
    Free = 0,
    Paid = 1,
    // Internal reward category, not a third SELECT_CARD request value.
    BlackDiamond = 2,
}

/// <summary>One thing sitting behind one card position.</summary>
/// <param name="ItemId">The archive's item id.</param>
/// <param name="Quantity">How many. One, for a piece of equipment.</param>
/// <param name="Rarity">The quality the ladder rolled, kept so nothing has to re-derive it.</param>
/// <param name="Durability">
/// What the piece is created carrying, from its own declaration. Travels with it for the same
/// reason a floor drop's does: a piece created at zero durability is a broken one.
/// </param>
public readonly record struct ClearRewardCardItem(
    int ItemId, int Quantity, DropRarity Rarity, ushort Durability);

/// <summary>
/// One position of a card face, and what is behind it.
/// </summary>
/// <remarks>
/// <see cref="Item"/> is null for a position holding nothing, which is an ordinary outcome rather
/// than a failure: the odds a position carries are a fraction, and even a position whose odds come
/// in can roll a quality whose bucket in the pool holds nothing.
/// <para>
/// It stays null unless the card's fallback is switched on, and no card's is; see
/// <see cref="ClearRewardBlankItemSwitches"/>.
/// </para>
/// </remarks>
/// <param name="Index">Which position of the face this is, counted from zero.</param>
public sealed record ClearRewardCardSlot(int Index, ClearRewardCardItem? Item)
{
    /// <summary>Whether this position holds nothing.</summary>
    public bool IsEmpty => this.Item is null;
}

/// <summary>
/// A whole card face: every position of it, decided together.
/// </summary>
/// <remarks>
/// The whole face rather than one position at a time, and that is the point of the shape. A face
/// asked for once cannot disagree with itself, so "the same position of the same clear holds the
/// same thing" is a consequence of asking once rather than a rule every caller has to keep by
/// passing the same seed each time.
/// </remarks>
/// <param name="Gold">
/// What a flip of this face pays, whatever the positions hold. Decided here with the positions and
/// from the same seed, so a clear that is asked twice -- or reconnected into -- answers the same
/// figure both times. See <see cref="ClearRewardRules.CardGoldOf"/> for where it comes from.
/// <para>
/// The figure only. Which of the player's two golds it is paid into is the flip's business and not
/// this face's. Today it is paid into the account cargo. The reference server pays it into the
/// gold the character carries instead, which is where it belongs.
/// </para>
/// </param>
public sealed record ClearRewardCardFace(
    ClearRewardCardKind Kind, IReadOnlyList<ClearRewardCardSlot> Slots, int Gold);

/// <summary>
/// What a card face is being produced for: everything about the occasion that the archive's tables
/// are keyed on.
/// </summary>
/// <remarks>
/// Facts, all of them handed in. A rule service may not go and look any of these up -- the moment
/// it did it would become a second owner of the world's state -- so the caller that knows the run
/// and the character is the one that fills this in.
/// <para>
/// <b>Two levels rather than one, and each names whose it is.</b> The character's level chooses the
/// threshold profile's band; the dungeon's own <c>[basis level]</c> chooses the grade band a piece
/// is drawn from and the row a paid flip is priced at. Which table takes which, and why the two
/// differ, is argued on the two parameters below.
/// </para>
/// <para>
/// <b>How far the run got is not one of these facts.</b> Nothing on this path reads it; the run
/// itself still knows -- <see cref="DungeonRunState.VisitedCells"/>.
/// </para>
/// </remarks>
/// <param name="Kind">Free or paid. See <see cref="ClearRewardCardKind"/>.</param>
/// <param name="CharacterLevel">
/// Which of the threshold profile's level bands applies, and the only thing on this path the
/// character's own level decides.
/// <para>
/// <b>Deliberately still the character's, where the grade band and the price are the dungeon's.</b>
/// The split is a reading of the file rather than an oversight. <c>[drop prob]</c> is six profiles
/// named for the <b>player's</b> circumstance -- <c>default</c>, <c>event</c>, <c>event2</c> and
/// three <c>pcroom</c> ones -- so the axis of that whole section is who is playing and under what
/// promotion; the level bands inside a profile are that same axis one step down. What the archive
/// keys on a dungeon is the other two: what a clear is worth
/// (<see cref="ClearRewardTable.GradeBands"/>) and what turning a card over costs
/// (<see cref="ClearRewardTable.GoldCardCosts"/>), which is what
/// <see cref="DungeonBasisLevel"/> exists for.
/// </para>
/// <para>
/// The evidence that moved those two does not reach this one. It is
/// <c>[mob level charac level replace flag]</c>, which 134 of the archive's 1047 dungeons declare
/// and whose name says what it does: it replaces a <b>monster's</b> level with the character's.
/// What that shows is that a dungeon's contents are scaled off the dungeon unless a dungeon says
/// otherwise. Whether a position carries anything at all is neither a monster nor a grade nor a
/// price, so the same switch says nothing about it.
/// </para>
/// <para>
/// <b>It makes no difference on this archive, which is exactly why it is written down.</b> The
/// <c>default</c> profile pays 10000 in all four of its bands, so today the two readings cannot be
/// told apart by any clear -- and a choice nothing can distinguish is one a server keeps making by
/// accident. It is pinned instead, against a table whose bands differ, by
/// <c>ClearRewardRuleServiceTests.The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons</c>.
/// </para>
/// </param>
/// <param name="DungeonBasisLevel">
/// The dungeon's own <c>[basis level]</c>: which grade band a position is drawn from, and which
/// row of <c>[gold card cost table]</c> a paid flip is priced at.
/// <para>
/// <b>The dungeon's level and not the character's, and that closes a gear tap.</b> Keyed on the
/// character, a level 86 character clearing a dungeon the archive sets at level 5 drew level 86
/// equipment out of a level 5 clear -- and was charged the level 86 price for it. What a clear is
/// worth is a property of the clear.
/// </para>
/// <para>
/// The reading rests on the archive rather than on another server. The dungeon file carries
/// <c>[mob level charac level replace flag]</c>, which 134 of the archive's 1047 dungeons declare
/// and every one of them declares non-zero: a switch whose name is "replace the monster's level
/// with the character's" is only worth declaring where not replacing it is the default. This
/// repository already prices a clear's experience the same way -- see
/// <see cref="DungeonClearExperienceRules"/> -- so paying experience by the dungeon and equipment
/// by the character would have been two standards inside one settlement.
/// </para>
/// <para>
/// Handed in like every other fact here. The run carries it (<c>DungeonLayout.BasisLevel</c>),
/// which is where the caller takes it from; a rule service that fetched it would be reading the
/// dungeon file a second time.
/// </para>
/// </param>
/// <param name="Difficulty">The run's difficulty, indexed as the archive's bonus table is.</param>
/// <param name="MapMaxCount">
/// How many rooms the run's maze holds in total, which is what
/// <c>[reward item rate per map max count]</c> is keyed on and the only thing about the maze the
/// composition reads. Real mazes are routinely larger than the five rows that table declares --
/// dungeon 144's are ten, six, four, six and four -- so a count with no row is the ordinary case;
/// see <see cref="ClearRewardRules.MapBaseRate"/> for what is answered then.
/// </param>
/// <param name="PartyMemberCount">
/// How many were in the party, counted from one. Handed in like every other fact here rather than
/// worked out: what a clear was fought by is the caller's to know, not these rules'. What this
/// server's callers pass today is visible at their call sites.
/// </param>
public sealed record ClearRewardOccasion(
    ClearRewardCardKind Kind,
    int CharacterLevel,
    int DungeonBasisLevel,
    DungeonDifficulty Difficulty,
    int MapMaxCount,
    int PartyMemberCount)
{
    /// <summary>The run's committed monster deaths when this face is fixed.</summary>
    public bool HasBlackDiamond { get; init; }
    public ImmutableArray<DungeonKillRecord> Kills { get; init; } = [];

    /// <summary>Counts derived from <see cref="Kills"/> for reward rules that need aggregates.</summary>
    public DungeonKillSummary KillSummary => DungeonKillSummary.From(this.Kills);
}

/// <summary>
/// What a clear's card face holds: the odds each position carries, and what those odds produce.
/// </summary>
/// <remarks>
/// The one reader of the clear-reward rate table and of the equipment pool on this path, and the
/// only caller of <see cref="ClearRewardRules"/>. It holds no world state and is handed none: an
/// occasion and a seed go in, a face comes out, and the same pair going in twice produces the same
/// four positions.
/// <para>
/// The seed is explicit and is not mixed with anything here. <b>With every fallback switch off --
/// and all three are -- it is also the only thing that separates the two faces of one clear.</b>
/// They carry the same odds -- see <see cref="ClearRewardCardKind"/> -- and share a ladder, a grade
/// band and a pool, so one seed asked for both kinds answers the same four positions twice. A
/// switched-on paid card parts them; the caller may not lean on either state, and hands them
/// different seeds regardless. Saying so is cheaper than hiding a derivation inside a service whose
/// seed is meant to be the caller's.
/// </para>
/// </remarks>
public interface IClearRewardRuleService
{
    /// <summary>
    /// The odds one position of this occasion carries.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="FaceFor"/> so the composition can be read without drawing
    /// anything. Every position of one occasion carries the same odds: nothing in the table read
    /// from the archive is keyed on which position a card is.
    /// <para>
    /// An occasion the tables do not describe -- a level outside every band, a difficulty the
    /// archive does not list, a party size it does not price -- comes back impossible rather than
    /// at some neighbouring entry's odds. The table hands back nothing for those on purpose, and
    /// inventing a neutral value here would be indistinguishable from having read the archive's
    /// own zero one slot over.
    /// </para>
    /// <para>
    /// <b>A room count the archive does not list is not one of those.</b> It is the ordinary case
    /// rather than a gap -- the map-count table stops at five rooms and real mazes hold more -- and
    /// it is priced at the largest row the table does declare. Reading it as a gap is what emptied
    /// both faces of most real clears until it was corrected; see
    /// <see cref="ClearRewardRules.MapBaseRate"/>.
    /// </para>
    /// </remarks>
    ClearRewardChance ChanceOf(ClearRewardOccasion occasion);

    /// <summary>
    /// Every position of one face, decided in one call.
    /// </summary>
    /// <remarks>
    /// <b>A position that comes up holding nothing holds nothing.</b> The archive declares a
    /// fallback for two of its cards -- <c>[gold card blank item]</c> and
    /// <c>[pcroom card blank item]</c> -- and this server hands neither of them out, because each
    /// names an item that is unusable here; the mechanism is built and every switch is off. See
    /// <see cref="ClearRewardBlankItemSwitches"/> for the three switches and for what each is off
    /// for, and <see cref="ClearRewardRules.BlankItemOf"/> for how a switched-on card draws.
    /// <para>
    /// A switched-on card's fallback answers for a position that was <b>drawn</b> and came up
    /// empty, and for nothing else. An occasion these tables could not answer at all -- a level in
    /// no band, a file missing its rarity section, a dungeon level with no grade row -- comes back
    /// four empty positions whatever the switches say, because a dice that was never thrown left
    /// nothing empty.
    /// </para>
    /// </remarks>
    /// <param name="seed">
    /// The occasion's own seed, chosen by the caller. Two calls with the same occasion and the same
    /// seed produce the same face position by position.
    /// </param>
    ClearRewardCardFace FaceFor(ClearRewardOccasion occasion, ulong seed);

    /// <summary>
    /// What a paid flip of a dungeon at that basis level costs, or null when the archive prices no
    /// such level.
    /// </summary>
    /// <remarks>
    /// From <c>[gold card cost table]</c> of <c>etc/itemdropinfo_clearreward.etc</c>, whose rows
    /// are a level and a price; the reading is <c>ClearRewardTable.GoldCardCost</c>'s and nothing
    /// is composed on top of it. It is asked for here because the archive is this service's to
    /// read and nobody else's -- a caller that wanted the price and went to the file for it would
    /// be a second reader of the table this service owns.
    /// <para>
    /// <b>The dungeon's level, not the character's.</b> A clear is priced by what was cleared: a
    /// level 86 character turning a card in a dungeon the archive sets at level 5 pays the level 5
    /// row, 580, and not the level 86 row's 10300. See
    /// <see cref="ClearRewardOccasion.DungeonBasisLevel"/> for the reading behind that and for the
    /// gear tap the other way round opened. The A21 reference server reaches the same row by the
    /// same key.
    /// </para>
    /// <para>
    /// A level and nothing more. That table is keyed on the level alone: the difficulty, the maze
    /// and the party size that decide what a face holds have no bearing on what turning one over
    /// costs, so an occasion is not what is asked about here.
    /// </para>
    /// <para>
    /// <b>Null is handed on rather than smoothed over.</b> Zero is a price -- a free flip -- so a
    /// level the table does not price cannot be answered with one without turning "not priced"
    /// into a giveaway. The table answers null for exactly that reason and says the decision is
    /// the caller's; this signature repeats it rather than making it here. That is also where this
    /// server parts company with the reference one, which walks back to the nearest lower row and
    /// finally to the lowest, and so can never answer "not priced" at all.
    /// </para>
    /// </remarks>
    /// <param name="dungeonBasisLevel">
    /// The dungeon's own <c>[basis level]</c>, which the run carries as
    /// <c>DungeonLayout.BasisLevel</c>.
    /// </param>
    int? PaidFlipCost(int dungeonBasisLevel);

    /// <summary>
    /// Whether this dungeon offers a paid flip at all, from its own declaration.
    /// </summary>
    /// <remarks>
    /// A dungeon may switch the paid card off for itself. Five of the archive's dungeons do -- see
    /// <see cref="DungeonDefinition.OffersPaidCard"/> for the tag, for which five, and for why
    /// zero is read as "off" -- and every other dungeon is left as the cost table alone decides.
    /// <para>
    /// <b>Separate from <see cref="PaidFlipCost"/> rather than folded into it, and deliberately
    /// so.</b> Both do vary by dungeon, but not by the same fact about one and not out of the same
    /// file. A price is a row of <c>[gold card cost table]</c> reached by the dungeon's
    /// <c>[basis level]</c>, so every dungeon set at one level is priced alike; whether there is a
    /// flip to price at all is the dungeon's own <c>[gold card use]</c>, which five dungeons and
    /// no others declare. One is a level's price and the other is a dungeon's refusal, and a
    /// single answer would have to say which of the two a null came from.
    /// </para>
    /// <para>
    /// <b>A dungeon this server cannot read answers true.</b> That is the unchanged case rather
    /// than a guess: a dungeon the archive does not declare, and a dungeon it declares without a
    /// maze, both come back as nothing from
    /// <see cref="IDungeonDefinitionSource"/> -- and nothing is not a declaration that the paid
    /// card is off. Answering false for them would switch the card off for every dungeon this
    /// server has no definition for, which is the opposite of what silence means.
    /// </para>
    /// <para>
    /// Asynchronous because reading a dungeon is, and for no other reason. The archive read is
    /// this service's to make: a caller that went to the dungeon file for this would be a second
    /// reader of content, which is what <see cref="PaidFlipCost"/>'s remarks say about the cost
    /// table and is equally true here.
    /// </para>
    /// </remarks>
    /// <param name="dungeonId">Which dungeon the clear was fought in.</param>
    ValueTask<bool> OffersPaidFlipAsync(int dungeonId, CancellationToken cancellationToken);
}
