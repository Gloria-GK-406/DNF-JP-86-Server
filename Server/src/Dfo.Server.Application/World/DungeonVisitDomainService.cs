using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.World.IllusionTower;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    IDungeonStructureRuleService layouts,
    IDungeonRunDomainService runs,
    IDungeonSelectionRuleService selection,
    IDungeonCellFactory cells,
    ICharacterFatigueDomainService fatigue, IDungeonAdmissionDomainService admission,
    IIllusionTowerDomainService? tower = null,
    ILogger<DungeonVisitDomainService>? logger = null) : IDungeonVisitDomainService
{
    private readonly ILogger<DungeonVisitDomainService> logger = logger ?? NullLogger<DungeonVisitDomainService>.Instance;
    public bool DefersSelectionState(byte characterLevel, bool tutorialCompleted = false) =>
        selection.DefersSelectionState(characterLevel, tutorialCompleted);

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
        return await admission.ExecuteAsync(new(dungeonId, facts, 0, 0, participants), new(), async (receipt, ct) =>
        {
            DungeonLayout? layout;
            try { layout = await layouts.ResolveAsync(dungeonId, facts, Random.Shared.Next(), ct).ConfigureAwait(false); }
            catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
            return layout is null ? DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound)
                : await EnterGrantedPartyAsync(participants, layout, facts, receipt, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<DungeonEntryAttempt> TryEnterPreparedPartyAsync(IReadOnlyList<DungeonRunParticipant> participants,
        DungeonLayout layout, DungeonEntryFacts facts, CancellationToken cancellationToken) =>
        admission.ExecuteAsync(new(layout.DungeonId, facts, 0, 0, participants),
            new() { Mirror = layout.Mirror is not null, ConsumesFatigue = layout.ConsumesFatigue },
            (receipt, ct) => EnterGrantedPartyAsync(participants, layout, facts, receipt, ct), cancellationToken);

    public ValueTask<DungeonEntryAttempt> EnterGrantedPartyAsync(IReadOnlyList<DungeonRunParticipant> participants,
        DungeonLayout layout, DungeonEntryFacts facts, IDungeonAdmissionReceipt receipt, CancellationToken cancellationToken)
    {
        if (admission.BeginEntry(receipt, participants, layout.DungeonId) is { } refusal)
            return ValueTask.FromResult(refusal);
        return EnterPreparedCoreAsync(participants, layout, facts, cancellationToken);
    }

    private async ValueTask<DungeonEntryAttempt> EnterPreparedCoreAsync(
        IReadOnlyList<DungeonRunParticipant> participants, DungeonLayout layout,
        DungeonEntryFacts facts, CancellationToken cancellationToken)
    {
        var difficulty = facts.Difficulty;
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
        if (tower is not null && !tower.TryReserveOrdinaryEntry(participants.Select(x => x.CharacterId).ToArray()))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.AlreadyInDungeon);
        if (layout.ConsumesFatigue)
            foreach (var participant in participants)
                await fatigue.SpendAsync(participant.CharacterId, layout.EntryOnlyFatigue ?? 1, cancellationToken).ConfigureAwait(false);
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
        => (await TryMoveAsync(characterId, current, target, cancellationToken).ConfigureAwait(false)).Visit;

    public async ValueTask<DungeonMoveAttempt> TryMoveAsync(
        CharacterId characterId, DungeonRunState current, MazeCell target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);

        DungeonMoveAttempt Refused(DungeonMoveFailure reason)
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Dungeon move refused character={CharacterId} run={RunId} dungeon={DungeonId} room={RoomId} version={RoomVersion} from=({FromX},{FromY}) target=({X},{Y}) reason={Reason}",
                    characterId, current.RunId, current.Layout.DungeonId, current.CurrentRoomMapId, current.RoomVersion,
                    current.CurrentCell.X, current.CurrentCell.Y, target.X, target.Y, reason);
            return new(null, reason);
        }

        // The caller's view can predate a leader departure. Cell preparation reads the current
        // party-backed run view; the existing room version still guards the final move commit.
        var latest = runs.Find(characterId);
        if (latest is null || latest.RunId != current.RunId
            || latest.RoomVersion != current.RoomVersion)
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Dungeon move stale character={CharacterId} expectedRun={ExpectedRunId} actualRun={ActualRunId} expectedVersion={ExpectedVersion} actualVersion={ActualVersion}",
                    characterId, current.RunId, latest?.RunId, current.RoomVersion, latest?.RoomVersion);
            return Refused(DungeonMoveFailure.StaleRun);
        }

        var firstEntry = !latest.VisitedCells.Contains(target);
        if (!latest.Layout.Cells.ContainsKey(target))
            return Refused(DungeonMoveFailure.CellNotFound);
        if (!latest.Layout.CanTraverse(latest.CurrentCell, target))
            return Refused(DungeonMoveFailure.Disconnected);

        DungeonCellSeed? preparedCell = null;
        if (firstEntry)
        {
            var selected = await layouts.SelectMapAsync(latest.Layout, latest.CurrentCell, target, cancellationToken)
                .ConfigureAwait(false);
            if (selected.MapId is not { } mapId) return Refused(DungeonMoveFailure.NoCompatibleMap);
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Dungeon move preparing character={CharacterId} run={RunId} version={RoomVersion} target=({X},{Y}) map={MapId} firstEntry={FirstEntry}",
                    characterId, latest.RunId, latest.RoomVersion, target.X, target.Y, mapId, firstEntry);
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
        if (entered is null) return Refused(DungeonMoveFailure.StateRejected);
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Dungeon move accepted character={CharacterId} run={RunId} dungeon={DungeonId} room={RoomId} version={RoomVersion} firstEntry={FirstEntry} actors={ActorCount} objects={ObjectCount}",
                characterId, entered.RunId, entered.Layout.DungeonId, entered.CurrentRoomMapId, entered.RoomVersion,
                firstEntry, entered.CurrentCellState.Actors.Count, entered.CurrentCellState.Objects.Count);
        if (firstEntry && entered.Layout.ConsumesFatigue && entered.Layout.EntryOnlyFatigue is null)
            foreach (var participant in entered.Participants)
                await fatigue.SpendAsync(participant.CharacterId, 1, cancellationToken).ConfigureAwait(false);
        return new(this.DescribeCreatedCell(entered, firstEntry), null);
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
            .Select(actor => new RoomActorDescription(actor.Actor, actor.IsAiCharacter)
                { IsHellTemplate = actor.IsHellTemplate, HellOrder = actor.HellOrder,
                    ObjectTemplateOwner = actor.ObjectTemplateOwner, ObjectTemplateIndex = actor.ObjectTemplateIndex })
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
