using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
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
        var wireUserId = characterId.Value is > 0 and <= ushort.MaxValue
            ? checked((ushort)characterId.Value)
            : (ushort)0;
        var entered = await this.EnterPartyAsync(
            [new DungeonRunParticipant(
                new AccountId(0), characterId, wireUserId, Slot: 0, IsLeader: true)],
            dungeonId,
            difficulty,
            facts,
            cancellationToken).ConfigureAwait(false);
        return entered is null
            ? null
            : new DungeonEntry(entered.Layout, entered.Visits[characterId]);
    }

    public async ValueTask<DungeonPartyEntry?> EnterPartyAsync(
        IReadOnlyList<DungeonRunParticipant> participants,
        int dungeonId,
        byte difficulty,
        DungeonEntryFacts facts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(facts);

        // One seed and one layout per shared run. Every participant is attached only after the
        // layout exists, and BeginParty publishes all character indexes under one aggregate lock.
        var layout = await layouts
            .ResolveAsync(dungeonId, facts, Random.Shared.Next(), cancellationToken)
            .ConfigureAwait(false);
        if (layout is null)
            return null;

        var leaderView = runs.BeginParty(participants, layout, difficulty);
        try
        {
            var visits = new Dictionary<CharacterId, DungeonRoomVisit>(participants.Count);
            foreach (var participant in participants)
            {
                var run = participant.CharacterId == leaderView.Participant?.CharacterId
                    ? leaderView
                    : runs.Find(participant.CharacterId)
                        ?? throw new InvalidOperationException(
                            "A shared dungeon run was published without every participant index.");
                visits.Add(
                    participant.CharacterId,
                    await this.DescribeAsync(run, firstEntry: true, cancellationToken)
                        .ConfigureAwait(false));
            }

            return new DungeonPartyEntry(layout, visits);
        }
        catch
        {
            // Projection is part of entry preparation. If any member's room description cannot
            // be built, nobody owns a usable entry. End only the run we just published: a member
            // may already have re-entered on a replacement connection while an awaited rule was
            // completing, and that newer run must survive this rollback.
            foreach (var participant in participants)
                runs.End(participant.CharacterId, leaderView.RunId);
            throw;
        }
    }

    public async ValueTask<DungeonRoomVisit?> MoveAsync(
        CharacterId characterId,
        DungeonRunState current,
        MazeCell target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);

        var firstEntry = !current.VisitedCells.Contains(target);
        var moved = runs.TryMove(
            characterId, current.RunId, current.RoomVersion, target);
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
