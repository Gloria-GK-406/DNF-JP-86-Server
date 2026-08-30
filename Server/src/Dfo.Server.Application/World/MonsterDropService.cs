using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.World;

/// <summary>
/// What a monster's death puts on the floor.
/// </summary>
/// <remarks>
/// The order is fixed and each step is somebody else's: the archive says what the rates are, the
/// compensation seam gets to raise them, the rules decide which of them hit and what quality each
/// hit is, the pools say what may be drawn, and the run hands out the numbers.
/// <para>
/// Nothing here decides a number. That is the whole point of the arrangement -- the one place a
/// constant could hide is the basis, and that is configuration with its own name and its own
/// explanation.
/// </para>
/// </remarks>
internal sealed class MonsterDropService(
    IDropRateTableSource rateTables,
    IGoldDropTableSource goldTables,
    IDropItemPoolSource pools,
    IDropRateAdjuster adjuster,
    IDungeonRunDomainService runs,
    IDungeonRoomDropSource roomDrops,
    DropRateOptions options,
    ILogger<MonsterDropService> logger) : IMonsterDropService
{
    public async ValueTask<IReadOnlyList<MonsterDrop>> ForKillAsync(
        MonsterKill kill, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kill);

        var rolled = this.Roll(kill);
        if (rolled.Count == 0)
            return [];

        // The room's own pieces hold the low numbers, so what it declares has to be known before
        // anything can be numbered above them.
        var declared = await roomDrops
            .GetAsync(kill.Run.Layout.DungeonId, kill.Run.CurrentRoomMapId, cancellationToken)
            .ConfigureAwait(false);

        var allocated = runs.AllocateMonsterDrops(
            kill.CharacterId, declared.Count, kill.ActorSequence, rolled);

        // What dropped and why, because a rate is not a thing anybody can check by looking. This
        // line is what makes a live session usable as evidence for whether the tables are being
        // read right, which is how the reference server's arithmetic was recovered in the first
        // place.
        foreach (var drop in allocated)
        {
            logger.LogInformation(
                "Monster drop character={CharacterId}: actor={Actor} monster level={Level} "
                    + "rank={Rank} gave piece {Number} = item {ItemId} x{Count} ({Rarity}, {Kind})",
                kill.CharacterId.Value,
                kill.ActorSequence,
                kill.MonsterLevel,
                kill.MonsterRank,
                drop.Number,
                drop.ItemId,
                drop.Count,
                drop.Rolled.Rarity,
                drop.Rolled.Kind);
        }

        return allocated;
    }

    /// <summary>
    /// What this kill produced, before anything is numbered.
    /// </summary>
    /// <remarks>
    /// Deterministic in every input: the same kill of the same monster in the same room of the
    /// same run produces the same list, because the only randomness is derived from exactly those
    /// things. Nothing is read from the clock and nothing is drawn from a shared generator.
    /// </remarks>
    private IReadOnlyList<RolledDrop> Roll(MonsterKill kill)
    {
        var table = rateTables.Load(DropRateTables.Monster);
        if (table.BandFor(kill.MonsterLevel) is not { } band)
        {
            logger.LogWarning(
                "Monster drop character={CharacterId}: the rate table covers no level "
                    + "{Level}, so nothing was dropped",
                kill.CharacterId.Value,
                kill.MonsterLevel);
            return [];
        }

        var rates = adjuster.Adjust(
            kill.CharacterId,
            DropRules.RatesFor(
                table,
                band,
                kill.MonsterRank,
                kill.Run.Difficulty,
                kill.PartyMemberCount,
                options.RateBasis));

        var random = new DropRandom(DropRandom.SeedFor(
            kill.Run.Layout.Seed,
            kill.Run.CurrentRoomMapId,
            kill.Run.CurrentCell.X,
            kill.Run.CurrentCell.Y,
            kill.ActorSequence));

        var rolled = new List<RolledDrop>();
        foreach (var slot in DropRules.Gate(rates, random))
        {
            if (this.Draw(table, kill, slot, random) is { } drop)
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
        DropRateTable table, MonsterKill kill, DropRateSlot slot, DropRandom random)
    {
        var kind = DropRules.KindOf(slot);
        if (kind == DropItemKind.Gold)
        {
            var amount = DropRules.GoldAmount(
                goldTables.Load(DropRateTables.Monster),
                kill.MonsterLevel,
                kill.MonsterRank,
                random);
            return amount > 0
                ? new RolledDrop(RolledDrop.GoldItemId, amount, DropRarity.Common, kind)
                : null;
        }

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
