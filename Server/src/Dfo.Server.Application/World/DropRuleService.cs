using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

/// <summary>
/// The one reader of the drop tables, and the only caller of the drop rules.
/// </summary>
/// <remarks>
/// The order is fixed and each step is somebody else's: the archive says what the rates are, the
/// rules decide which of them hit and what quality each hit is, and the pools say what may be
/// drawn. Nothing here decides a number. The one place a constant could hide is the basis, and that
/// is configuration with its own name and its own explanation, passed into the rules rather than
/// defaulted inside them.
/// </remarks>
internal sealed class DropRuleService(
    IDropRateTableSource rateTables,
    IGoldDropTableSource goldTables,
    IDropItemPoolSource pools,
    DropRateOptions options) : IDropRuleService
{
    public DropRates? RatesFor(MonsterDropRequest kill)
    {
        ArgumentNullException.ThrowIfNull(kill);

        var table = rateTables.Load(DropRateTables.Monster);
        return table.BandFor(kill.MonsterLevel) is not { } band
            ? null
            : DropRules.RatesFor(
                table,
                band,
                kill.MonsterRank,
                kill.Context.Difficulty,
                kill.PartyMemberCount,
                options.RateBasis);
    }

    public IReadOnlyList<RolledDrop> DrawFor(MonsterDropRequest kill, DropRates rates, bool includeItems = true, int attempt = 0)
    {
        ArgumentNullException.ThrowIfNull(kill);
        ArgumentNullException.ThrowIfNull(rates);

        var table = rateTables.Load(DropRateTables.Monster);
        var random = new DropRandom(DropRandom.SeedFor(
            kill.Context.Seed,
            kill.Context.MapId,
            kill.Context.Cell.X,
            kill.Context.Cell.Y,
            kill.ActorSequence) ^ ((ulong)(uint)attempt * 0x9E3779B97F4A7C15UL));

        var rolled = new List<RolledDrop>();
        foreach (var slot in DropRules.Gate(rates, random))
        {
            if ((includeItems || slot == DropRateSlot.Gold)
                && this.Draw(table, kill, slot, random) is { } drop)
                rolled.Add(drop);
        }

        return rolled;
    }

    /// <summary>
    /// What one slot that hit yields, or nothing when its pool has nothing to give.
    /// </summary>
    /// <remarks>
    /// A slot that hits and draws nothing is an ordinary outcome, not a failure. The archive fills
    /// five of the ladder's six qualities, so a kill can pass a gate and roll a quality nothing is
    /// declared at.
    /// <para>
    /// <b>The avatar slot always draws nothing</b>, and that is a decision rather than a
    /// consequence. Avatars live in the same list as ordinary gear, so sending this slot to the
    /// equipment pool would have it pay fashion's rate in weapons and armour -- one gear drop in
    /// twenty-eight at the endgame band, arriving through a rate the archive assigns to something
    /// else. No avatar in the archive declares itself creatable, so the correct pool for this slot
    /// is empty, and drawing nothing is what an empty pool yields. Phase one does not build that
    /// pool; it declines the slot and says so.
    /// </para>
    /// <para>
    /// The rarity is rolled here and handed to the draw rather than being rolled inside it. That
    /// is what leaves the constrained entry -- draw me a piece of this quality -- available for
    /// nothing extra, which the card flip and the compensation service will both want.
    /// </para>
    /// </remarks>
    private RolledDrop? Draw(
        DropRateTable table, MonsterDropRequest kill, DropRateSlot slot, DropRandom random)
    {
        var kind = DropRules.KindOf(slot);
        return kind == DropItemKind.Gold
            ? this.DrawGold(kill, random)
            : this.DrawItem(table, kill, slot, kind, random);
    }

    private RolledDrop? DrawGold(MonsterDropRequest kill, DropRandom random)
    {
        var amount = DropRules.GoldAmount(
            goldTables.Load(DropRateTables.Monster),
            kill.MonsterLevel, kill.MonsterRank, random);
        return amount > 0
            ? new RolledDrop(RolledDrop.GoldItemId, amount, DropRarity.Common, DropItemKind.Gold)
            : null;
    }

    private RolledDrop? DrawItem(
        DropRateTable table, MonsterDropRequest kill, DropRateSlot slot,
        DropItemKind kind, DropRandom random)
    {
        if (kind == DropItemKind.Avatar)
            return null;

        if (table.GradeBandFor(kill.MonsterLevel) is not { } grades)
            return null;

        var rarity = DropRules.RollRarity(table, slot, random);
        var family = kind == DropItemKind.Equipment
            ? DropItemFamily.Equipment
            : DropItemFamily.Stackable;

        return DropRules.Pick(pools.Load(family), grades, rarity, random) is { } drawn
            ? new RolledDrop(drawn.ItemId, 1, rarity, kind, drawn.Durability)
            : null;
    }
}
