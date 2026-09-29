using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
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
    IDungeonSelectionRuleService selection,
    IDungeonCellFactory cells,
    ICharacterDungeonDifficultyDomainService difficulties,
    IDungeonDifficultyRuleService difficultyRules, ICharacterFatigueDomainService fatigue) : IDungeonVisitDomainService
{
    public bool DefersSelectionState(byte characterLevel) =>
        selection.DefersSelectionState(characterLevel);

    public async ValueTask<DungeonEntry?> EnterAsync(
        CharacterId characterId,
        int dungeonId,
        DungeonDifficulty difficulty,
        DungeonEntryFacts facts,
        CancellationToken cancellationToken)
    {
        var entered = await this.EnterPartyAsync(
            [new DungeonRunParticipant(
                new AccountId(0), characterId, Slot: 0, IsLeader: true)],
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
        DungeonDifficulty difficulty,
        DungeonEntryFacts facts,
        CancellationToken cancellationToken)
    {
        var attempt = await TryEnterPartyAsync(participants, dungeonId, facts with { Difficulty = difficulty }, cancellationToken)
            .ConfigureAwait(false);
        return attempt.Entry;
    }

    public async ValueTask<DungeonEntryAttempt> TryEnterPartyAsync(
        IReadOnlyList<DungeonRunParticipant> participants, int dungeonId,
        DungeonEntryFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(facts);

        var difficulty = facts.Difficulty;
        if (!Enum.IsDefined(difficulty)) throw new ArgumentOutOfRangeException(nameof(facts));
        // Party-wide eligibility policy is intentionally unchanged in this task.
        if (participants.Count == 1 && !difficultyRules.Allows(difficulty, await difficulties.GetMaximumAsync(
                participants[0].CharacterId, dungeonId, cancellationToken).ConfigureAwait(false)))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.DifficultyNotUnlocked);

        // One seed and one layout per shared run. Every participant is attached only after the
        // layout exists, and BeginParty publishes all character indexes under one aggregate lock.
        var layout = await layouts
            .ResolveAsync(dungeonId, facts, Random.Shared.Next(), cancellationToken)
            .ConfigureAwait(false);
        if (layout is null)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound);

        if (layout.ConsumesFatigue)
            foreach (var participant in participants)
                if ((await fatigue.BalanceOfAsync(participant.CharacterId, cancellationToken).ConfigureAwait(false)).Remaining == 0)
                    return new(null, DungeonEntryFailure.InsufficientFatigue, participant.Slot);

        var runId = DungeonRunId.New();
        var startedAt = DateTimeOffset.UtcNow;
        var initialCell = await cells.CreateAsync(
            new DungeonCellCreationContext(
                runId,
                layout,
                layout.StartCell,
                layout.StartRoomMapId,
                difficulty,
                startedAt,
                participants),
            cancellationToken).ConfigureAwait(false);
        if (layout.ConsumesFatigue)
            foreach (var participant in participants)
                await fatigue.SpendAsync(participant.CharacterId, 1, cancellationToken).ConfigureAwait(false);
        var change = runs.PreparePartyEntry(
            participants, layout, difficulty, initialCell, runId, startedAt);
        var createdVisits = participants.ToDictionary(
            participant => participant.CharacterId,
            participant => this.DescribeCreatedCell(
                change.Run with { Participant = participant },
                firstEntry: true));
        return DungeonEntryAttempt.Accepted(new DungeonPartyEntry(layout, createdVisits, change));
    }

    public async ValueTask<DungeonRoomVisit?> MoveAsync(
        CharacterId characterId,
        DungeonRunState current,
        MazeCell target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);

        // The caller's view can predate a leader departure. Cell preparation reads the current
        // party-backed run view; the existing room version still guards the final move commit.
        var latest = runs.Find(characterId);
        if (latest is null || latest.RunId != current.RunId
            || latest.RoomVersion != current.RoomVersion)
            return null;

        var firstEntry = !latest.VisitedCells.Contains(target);
        if (!latest.Layout.Rooms.TryGetValue(target, out var mapId))
            return null;

        DungeonCellSeed? preparedCell = null;
        if (firstEntry)
        {
            preparedCell = await cells.CreateAsync(
                new DungeonCellCreationContext(
                    latest.RunId,
                    latest.Layout,
                    target,
                    mapId,
                    latest.Difficulty,
                    latest.StartedAt,
                    latest.Participants),
                cancellationToken).ConfigureAwait(false);
        }

        var entered = runs.TryMove(
            characterId,
            latest.RunId,
            latest.RoomVersion,
            target,
            preparedCell);
        if (entered is null) return null;
        if (firstEntry && entered.Layout.ConsumesFatigue)
            foreach (var participant in entered.Participants)
                await fatigue.SpendAsync(participant.CharacterId, 1, cancellationToken).ConfigureAwait(false);
        return this.DescribeCreatedCell(entered, firstEntry);
    }

    private DungeonRoomVisit DescribeCreatedCell(DungeonRunState run, bool firstEntry)
    {
        var cell = run.CurrentCellState;
        if (!firstEntry)
        {
            // A21's revisit builder does not recreate actors or floor objects client-side. The
            // domain mirror remains the complete, reused cell above; only this wire projection is
            // empty.
            return new DungeonRoomVisit(run, firstEntry, [], [], cell.FirstActorSequence);
        }

        var actors = cell.Actors
            .Select(actor => new RoomActorDescription(actor.Actor, actor.IsAiCharacter))
            .ToArray();
        var floor = cell.GroundItems
            .Where(item => item.Source.Kind == DungeonGroundItemSourceKind.DeclaredObject)
            .Select(item => new FloorDropDescription(
                new GroundDrop(
                    item.Number,
                    new DungeonRoomDrop(
                        item.Source.ObjectIndex!.Value,
                        item.ItemId,
                        item.Durability,
                        item.Count)),
                item.IsEquipment))
            .ToArray();
        return new DungeonRoomVisit(run, firstEntry, actors, floor, cell.FirstActorSequence);
    }
}
