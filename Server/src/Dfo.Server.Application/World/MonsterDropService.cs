using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.World;

/// <summary>
/// What a monster's death puts on the floor.
/// </summary>
/// <remarks>
/// The order is fixed and each step is somebody else's: the rules say what the rates are and what
/// they draw, the compensation seam gets to raise them in between, the room says how many pieces
/// hold the low numbers, and the run hands out the numbers above them.
/// <para>
/// Nothing here decides a number. What is left is the flow: ask, adjust, ask again, number, and
/// write the line that makes a live session usable as evidence.
/// </para>
/// </remarks>
internal sealed class MonsterDropService(
    IDropRuleService dropRules,
    IDropRateAdjuster adjuster,
    IDungeonRunDomainService runs,
    IDungeonRoomRuleService rooms,
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
        var declared = await rooms
            .DeclaredDropsAsync(
                kill.Run.Layout.DungeonId, kill.Run.CurrentRoomMapId, cancellationToken)
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
    /// The rates are asked for, raised on the character's behalf if anything is owed, and handed
    /// back to be drawn at. The raising happens here rather than inside the rules because what a
    /// character is owed is state that crosses runs, and the rules are not allowed to go and find
    /// it.
    /// </remarks>
    private IReadOnlyList<RolledDrop> Roll(MonsterKill kill)
    {
        if (dropRules.RatesFor(kill) is not { } rates)
        {
            logger.LogWarning(
                "Monster drop character={CharacterId}: the rate table covers no level "
                    + "{Level}, so nothing was dropped",
                kill.CharacterId.Value,
                kill.MonsterLevel);
            return [];
        }

        return dropRules.DrawFor(kill, adjuster.Adjust(kill.CharacterId, rates));
    }
}
