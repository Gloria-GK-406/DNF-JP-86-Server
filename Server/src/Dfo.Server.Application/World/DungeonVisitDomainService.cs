using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

/// <summary>
/// Entering a dungeon and walking between its rooms.
/// </summary>
/// <remarks>
/// This used to be assembled inside two protocol handlers, which is what `AGENTS.md` forbids under
/// "handler 不得承载业务语义": the order of the three steps is the business, and a handler that
/// carries it is a handler the domain layer knows nothing about.
/// </remarks>
internal sealed class DungeonVisitDomainService(
    IDungeonLayoutRuleService layouts,
    IDungeonRunDomainService runs,
    IDungeonRoomRuleService rooms,
    IItemPlacementRuleService placementRules,
    IDungeonSelectionRuleService selection) : IDungeonVisitDomainService
{
    public bool DefersSelectionState(byte characterLevel) =>
        selection.DefersSelectionState(characterLevel);

    public async ValueTask<DungeonEntry?> EnterAsync(
        CharacterId characterId,
        int dungeonId,
        byte difficulty,
        DungeonEntryFacts facts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);

        // One seed per run, drawn here and then carried: the rule never invents one, because a
        // layout has to be reproducible from what was recorded about the run.
        var layout = await layouts
            .ResolveAsync(dungeonId, facts, Random.Shared.Next(), cancellationToken)
            .ConfigureAwait(false);
        if (layout is null)
            return null;

        // Remembered from here on. The layout was chosen from quest state as it stood at this
        // moment, so it cannot be recomputed later and still describe the run the client is in.
        var run = runs.Begin(characterId, layout, difficulty);
        return new DungeonEntry(
            layout,
            await this.DescribeAsync(run, firstEntry: true, cancellationToken).ConfigureAwait(false));
    }

    public async ValueTask<DungeonRoomVisit?> MoveAsync(
        CharacterId characterId,
        DungeonRunState current,
        MazeCell target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);

        var firstEntry = !current.VisitedCells.Contains(target);
        var moved = runs.TryMove(characterId, target);
        return moved is null
            ? null
            : await this.DescribeAsync(moved, firstEntry, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// What the room the run is standing in holds, with each piece's kind already read.
    /// </summary>
    /// <remarks>
    /// A room walked into a second time is described as empty. The reference server has a separate
    /// revisit builder that writes no actors and no drops at all, and it is right to: the client
    /// already killed what was in there and still holds those identities. Sending the list again
    /// resurrects them under the very same numbers -- which is what a live session saw on
    /// 2026-08-26 when it walked back out of the boss room, five monsters and four monsters
    /// standing up again with the numbers they had died under.
    /// </remarks>
    private async ValueTask<DungeonRoomVisit> DescribeAsync(
        DungeonRunState run, bool firstEntry, CancellationToken cancellationToken)
    {
        var contents = await rooms.DescribeAsync(run, firstEntry, cancellationToken)
            .ConfigureAwait(false);

        var floor = new List<FloorDropDescription>(contents.GroundDrops.Count);
        foreach (var drop in contents.GroundDrops)
        {
            floor.Add(new FloorDropDescription(
                drop,
                await placementRules.IsEquipmentAsync(drop.Drop.ItemId, cancellationToken)
                    .ConfigureAwait(false)));
        }

        return new DungeonRoomVisit(
            run, firstEntry, contents.Actors, floor, contents.FirstActorSequence);
    }
}
