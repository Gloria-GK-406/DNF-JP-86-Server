namespace Dfo.Server.Abstractions.Content;

/// <summary>
/// One level band of a clear-reward profile, and the single rate it pays.
/// </summary>
/// <remarks>
/// Read from <c>[drop prob]</c> of <c>etc/itemdropinfo_clearreward.etc</c>, whose bands are three
/// numbers: two level bounds and one rate. That is not the shape of the sibling rate tables that
/// declare this section -- three of them do, and there a band is two bounds then five rates, a
/// gold rate and one per item slot; see <see cref="ClearRewardProfile"/> for what this file
/// declares instead.
/// <para>
/// What the rate is a numerator over is <b>not</b> declared by the archive, and is not decided
/// here. This type carries the archive's own integer; the basis, and how the bonus tables combine
/// with it, are named where that combination is performed.
/// </para>
/// </remarks>
public sealed record ClearRewardBand(int LowLevel, int HighLevel, int Rate)
{
    /// <summary>Whether this band covers that level.</summary>
    public bool Covers(int level) => level >= this.LowLevel && level <= this.HighLevel;
}

/// <summary>
/// One named profile of the clear-reward table: a name and the bands it pays.
/// </summary>
/// <remarks>
/// This is where the clear-reward table parts company with the rest of its family. The monster
/// table declares level bands of five parallel rates -- a gold rate and four item rates -- and
/// two of its siblings do the same; the fourth, the abyss table, does not declare this section at
/// all but a difficulty-keyed one. This file declares six
/// named profiles, each holding the number of bands that <c>[drop prob count]</c> announces, each
/// band paying one rate. Which profile applies to an occasion is the caller's fact, not this
/// file's: the archive names them and says nothing more about when each is chosen.
/// </remarks>
/// <param name="Name">
/// The profile's name exactly as the archive spells it, spaces included. Three of the six are two
/// words.
/// </param>
public sealed record ClearRewardProfile(string Name, IReadOnlyList<ClearRewardBand> Bands)
{
    /// <summary>The band covering that level, or null when none does.</summary>
    public ClearRewardBand? BandFor(int level) =>
        this.Bands.FirstOrDefault(band => band.Covers(level));
}

/// <summary>
/// The profile names the A21 archive declares under <c>[drop prob]</c>.
/// </summary>
/// <remarks>
/// Spelled as the archive spells them, because that spelling is the lookup key. The archive gives
/// no rule for choosing between them, so only <see cref="Default"/> has a caller today. The other
/// five are named so that a future one does not have to rediscover what the file holds.
/// <para>
/// What is missing is the archive's rule, not the occasion: three of these names are PC-room
/// profiles and this server already carries PC-room state, which is why the three are declared
/// below rather than left out. Choosing one without a rule to choose it by would be a rate nobody
/// could trace back.
/// </para>
/// </remarks>
public static class ClearRewardProfiles
{
    /// <summary>The profile with no occasion attached to it. Pays the full rate in every band.</summary>
    public const string Default = "default";

    public const string Event = "event";

    /// <summary>The archive spells this one <c>event2</c>; what distinguishes it from
    /// <see cref="Event"/> is not ascertained, and its four rates are identical to it.</summary>
    public const string SecondEvent = "event2";

    public const string PcRoomDefault = "pcroom default";
    public const string PcRoomBonus = "pcroom bonus";
    public const string PcRoomEvent = "pcroom event";
}

/// <summary>
/// One row of a card's fallback table: a weight, an item and a quantity.
/// </summary>
/// <remarks>
/// Read from <c>[gold card blank item]</c> and <c>[pcroom card blank item]</c>, whose rows are
/// three numbers each. The reading is checkable against the archive rather than assumed: all six
/// paid rows name the same item id and differ only in weight and quantity, which no other column
/// order reproduces.
/// <para>
/// Whether these are ever handed out is a rule decision and not this type's business, and the
/// decision is <see cref="Dfo.Server.Abstractions.World.ClearRewardBlankItemSwitches"/>: one
/// switch per card, all off, each saying in its own place what its item is unusable for. The paid
/// table's six rows all point at one 2013 event item.
/// </para>
/// <para>
/// The weight is a share of the weights beside it and of nothing fixed -- A21's six paid rows sum
/// to ten thousand and its one PC-room row to fifty -- which is why the draw is
/// <see cref="Dfo.Server.Abstractions.World.ClearRewardRules.BlankItemOf"/>'s and not a rate read
/// against the basis the rest of this file is counted against.
/// </para>
/// </remarks>
public sealed record ClearRewardBlankItem(int Weight, int ItemId, int Quantity);

/// <summary>
/// The archive's clear-reward rate table, read.
/// </summary>
/// <remarks>
/// One file: <c>etc/itemdropinfo_clearreward.etc</c>, named by
/// <see cref="DropRateTables.ClearReward"/>. Of its sixteen section tags, seven also appear in
/// the sibling rate tables and nine appear in no other file of the family -- so most of what it
/// declares is its own. Of the seven it shares, one it declares differently: its
/// <c>[drop prob]</c> is named profiles rather than bands of parallel rates.
/// <para>
/// Every member below names the section it came from. Three sections of the file are deliberately
/// not read at all; they are named and explained on the reader
/// (<c>PvfClearRewardRateSource</c>) rather than passed silently -- two because their purpose is
/// not ascertained, and one because it is inapplicable rather than unknown. This said four until
/// <c>[gold card create rate]</c> left that list for <see cref="GoldCardCreateRate"/>, and the
/// count stayed behind.
/// </para>
/// <para>
/// Nothing here is scaled, combined or interpreted. The scale of these integers is not something
/// the archive declares, so a table that quietly divided by ten thousand would be handing on an
/// assumption dressed as a reading. Combination happens one layer up, where the assumption can be
/// named.
/// </para>
/// </remarks>
/// <param name="ArchivePath">Which file this was read from, so a log can say.</param>
/// <param name="Profiles">
/// <c>[drop prob]</c>, in declaration order. Each holds as many bands as
/// <c>[drop prob count]</c> announces.
/// </param>
/// <param name="DropKindRates">
/// <c>[drop kind prob]</c>, verbatim: two numbers, <c>10000 0</c> in A21. <b>What either position
/// means is not ascertained</b>, and that is now looked-for-and-not-found rather than merely not
/// read: the section occurs in no other file of the family, so there is nothing to compare against
/// to fix the order, and the A21 reference server does not parse the section at all. It is carried
/// as the archive's own list so that whoever identifies it later changes a meaning rather than a
/// number.
/// </param>
/// <param name="ItemTypeRates">
/// <c>[drop item type prob]</c>, verbatim: four numbers, <c>0 9500 0 500</c> in A21. The archive
/// names no position, but the A21 reference server does: its <c>RollItemType</c> indexes this list
/// by item type less one and its equipment branch takes type 2 for ordinary equipment and type 4
/// for avatar, so A21's numbers are equipment at 9500 and avatar at 500 with types 1 and 3 unused.
/// Carried verbatim all the same: nothing on this path applies it, for the reason given on
/// <c>ClearRewardRuleService</c>.
/// </param>
/// <param name="DifficultyRates">
/// <c>[dungeon difficulty drop bonusrate]</c>, one entry per difficulty, indexed from zero. The
/// first is zero, which where these bonuses are added is the neutral entry rather than an emptying
/// one; and the fifth is smaller than the fourth, so this is not a monotonic ladder.
/// </param>
/// <param name="DifficultyGoldRates">
/// <c>[dungeon difficulty gold drop bonusrate]</c>, one multiplier per difficulty, indexed from
/// zero, the same five difficulties as <see cref="DifficultyRates"/>. A21 declares
/// <c>1.02 1.38 1.6 1.9 2</c>.
/// <para>
/// <b>Multipliers, where its sibling holds addends.</b> The lowest entry here is <c>1.02</c> and
/// not zero, which is what says these are scaled rather than summed -- a zero would empty the
/// figure rather than leave it alone. The two sections are read apart for that reason and never
/// share an accessor.
/// </para>
/// </param>
/// <param name="PartyMemberRates">
/// <c>[party member drop bonusrate]</c>, one multiplier per party size, stored by the archive as
/// singles. Indexed from a party of one.
/// </param>
/// <param name="MapCountRates">
/// <c>[reward item rate per map max count]</c>, read as count/rate pairs, keyed on how many rooms
/// a maze holds in total. A21 declares five rows, for one room through five. <b>What the rate is
/// for</b> -- it stands as the base value for a maze of that size, which the difficulty bonus and
/// the dice are then added to, the dice having been multiplied by the party's bonus first -- is
/// not this type's business and is settled at
/// <see cref="Dfo.Server.Abstractions.World.ClearRewardRules.ChanceOf"/>; neither is what to do
/// about a maze the file lists no row for, which
/// <see cref="Dfo.Server.Abstractions.World.ClearRewardRules.MapBaseRate"/> answers with the
/// largest declared row. The point here is only that such a total has no row of its own, which
/// <see cref="MapCountRate"/> says with null.
/// </param>
/// <param name="Rarity">
/// <c>[basis of rarity dicision]</c>. This file declares a single group of seven, where the three
/// siblings that declare bands of parallel rates each declare four such groups, and the abyss
/// table declares a leading count of two followed by two. So there is one ladder here rather than
/// one per item slot. Modelled with the family's
/// existing <see cref="RarityLadder"/>, whose remarks record why the basis must be read from the
/// group's last number rather than hard-coded.
/// </param>
/// <param name="GoldCardCosts">
/// <c>[gold card cost table]</c>, read as level/price pairs: what a paid flip costs at that level.
/// </param>
/// <param name="GoldCardCreateRate">
/// <c>[gold card create rate]</c>: one number, stored by A21 as a float where every neighbouring
/// rate is an integer, and <c>1</c> in this archive. Null when the file declares the section not
/// at all, which is the same "absent is not neutral" rule the rest of this type keeps.
/// <para>
/// <b>Nothing on this path reads it, and that is a rule decision rather than an omission.</b> The
/// two faces of one clear carry the same odds on this server, so the paid face is composed from
/// the map-count table, the difficulty bonus, the dice and the <c>default</c> threshold exactly as
/// the free face is, and neither of them reads this number. What the file gives the paid card is
/// its own three sections -- <c>[gold card cost table]</c>, this one and
/// <c>[gold card blank item]</c> -- and a paid card is therefore one that costs money and has its
/// own empty-handed fallback, not one drawn at other odds; the reasoning is on
/// <c>ClearRewardRuleService</c>. Carried verbatim all the same, as
/// <see cref="ItemTypeRates"/> is: a declaration dropped from the reading is harder to notice
/// later than one read and left unapplied.
/// </para>
/// <para>
/// <b>What another server does with it is recorded here as another server's reading, not as this
/// file's rule.</b> A single unlabelled number could be a gate, a scale or a count, and the file
/// does not say which; <c>ServerS4A21</c>'s <c>Game/Dungeon/ClearRewardGenerator.cs</c> is where
/// the question was put. There, <c>GeneratePaidItemCard</c> passes this value in the argument
/// position that <c>GenerateFreeItemCard</c> fills with an exploration rate of its own, and that
/// argument multiplies the profile's rate -- so on that server it is the paid face's scale and the
/// paid face never reads the map-count table. That server is another private implementation of the
/// same archive and carries no authority over this one, which is why it is written down as
/// evidence of what the number can be for and not as what this repository does with it.
/// </para>
/// </param>
/// <param name="GoldCardBlankItems">
/// <c>[gold card blank item]</c>, the paid flip's weighted fallback rows. Handed out only where
/// <see cref="Dfo.Server.Abstractions.World.ClearRewardBlankItemSwitches.GoldCard"/> is switched
/// on, which it is not.
/// </param>
/// <param name="PcRoomCardBlankItems">
/// <c>[pcroom card blank item]</c>, the PC-room flip's weighted fallback rows. <b>No flip reaches
/// them</b>: there is no PC-room card in
/// <see cref="Dfo.Server.Abstractions.World.ClearRewardCardKind"/> and the flip request carries no
/// face byte for one. They are read, drawable, and switched by
/// <see cref="Dfo.Server.Abstractions.World.ClearRewardBlankItemSwitches.PcRoomCard"/> all the
/// same -- the section is the same shape as the paid one and reading it costs a line.
/// </param>
/// <param name="GradeBands">
/// <c>[item drop ref table]</c>, whose rows are level, how far below, how far above. This file
/// declares its own and it is not the monster table's copy: at level 86 a card reaches one grade
/// less far up than a monster kill does.
/// </param>
public sealed record ClearRewardTable(
    string ArchivePath,
    IReadOnlyList<ClearRewardProfile> Profiles,
    IReadOnlyList<int> DropKindRates,
    IReadOnlyList<int> ItemTypeRates,
    IReadOnlyList<int> DifficultyRates,
    IReadOnlyList<double> DifficultyGoldRates,
    IReadOnlyList<double> PartyMemberRates,
    IReadOnlyDictionary<int, int> MapCountRates,
    RarityLadder? Rarity,
    IReadOnlyDictionary<int, int> GoldCardCosts,
    double? GoldCardCreateRate,
    IReadOnlyList<ClearRewardBlankItem> GoldCardBlankItems,
    IReadOnlyList<ClearRewardBlankItem> PcRoomCardBlankItems,
    IReadOnlyDictionary<int, ItemGradeBand> GradeBands)
{
    /// <summary>The profile of that name, or null when the file declares none.</summary>
    /// <remarks>
    /// Matched case-insensitively on the archive's own spelling; see
    /// <see cref="ClearRewardProfiles"/> for the six names A21 holds.
    /// </remarks>
    public ClearRewardProfile? ProfileNamed(string name) =>
        this.Profiles.FirstOrDefault(
            profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What this difficulty adds, or null when the table declares no such difficulty.
    /// </summary>
    /// <remarks>
    /// Null rather than a neutral value, and that is deliberate. The table's lowest difficulty
    /// declares zero, and zero is exactly what a neutral entry is worth where these bonuses are
    /// added rather than multiplied, so "absent" and "neutral" are not interchangeable here:
    /// answering an unlisted index with a neutral value would be indistinguishable from the
    /// archive's own first entry having been read one slot over. What an absent difficulty should
    /// mean belongs to the caller, who knows whether it can happen at all.
    /// </remarks>
    public int? DifficultyRate(int difficulty) =>
        difficulty >= 0 && difficulty < this.DifficultyRates.Count
            ? this.DifficultyRates[difficulty]
            : null;

    /// <summary>
    /// What this difficulty multiplies a card's gold by, or null when the table declares no such
    /// difficulty.
    /// </summary>
    /// <remarks>
    /// Null rather than one, for the reason its sibling answers null rather than zero: a caller
    /// handed the neutral value could not tell it apart from the archive's own entry, and this
    /// table's neutral value would be a number the archive never declares.
    /// </remarks>
    public double? DifficultyGoldRate(int difficulty) =>
        difficulty >= 0 && difficulty < this.DifficultyGoldRates.Count
            ? this.DifficultyGoldRates[difficulty]
            : null;

    /// <summary>What a party of this size multiplies by, or null when unlisted.</summary>
    /// <remarks>The table is indexed from a party of one, so a size of zero has no entry.</remarks>
    public double? PartyMemberRate(int partyMemberCount) =>
        partyMemberCount >= 1 && partyMemberCount <= this.PartyMemberRates.Count
            ? this.PartyMemberRates[partyMemberCount - 1]
            : null;

    /// <summary>What this map count adds, or null when the table declares no such count.</summary>
    public int? MapCountRate(int mapMaxCount) =>
        this.MapCountRates.TryGetValue(mapMaxCount, out var rate) ? rate : null;

    /// <summary>What a paid flip costs at that level, or null when the table prices no such level.</summary>
    /// <remarks>
    /// Null rather than zero. Zero is a price -- a free flip -- and a level the archive does not
    /// price is not a free one; the caller has to decide what to do about a level outside the
    /// table's one to two hundred rather than be handed a giveaway.
    /// </remarks>
    public int? GoldCardCost(int level) =>
        this.GoldCardCosts.TryGetValue(level, out var cost) ? cost : null;

    /// <summary>The grades this level draws from, or null when the table declares no such row.</summary>
    public ItemGradeBand? GradeBandFor(int level) =>
        this.GradeBands.TryGetValue(level, out var band) ? band : null;
}

/// <summary>Reads the archive's clear-reward rate table.</summary>
/// <remarks>
/// A content source, so by this repository's convention it is injected into rule services and
/// nowhere else. It is separate from <see cref="IDropRateTableSource"/> rather than another path
/// passed to it because the two files disagree about the shape of <c>[drop prob]</c>, and a reader
/// that accepted both would have to guess which one it was holding.
/// </remarks>
public interface IClearRewardRateSource
{
    /// <summary>
    /// The clear-reward table that file declares. Read once and held.
    /// </summary>
    /// <remarks>
    /// The path is a parameter for the same reason the sibling family's is: the file to read is
    /// the caller's choice, and <see cref="DropRateTables.ClearReward"/> names the one A21 holds.
    /// A file whose <c>[drop prob]</c> is not named profiles is refused by name rather than read
    /// as this shape.
    /// </remarks>
    ClearRewardTable Load(string archivePath);
}
