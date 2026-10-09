using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.World.IllusionTower;

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
    ICharacterDungeonDifficultyDomainService difficulties,
    IDungeonDifficultyRuleService difficultyRules, ICharacterFatigueDomainService fatigue,
    IIllusionTowerDomainService? tower = null, IDungeonEntryMaterialsDomainService? materials = null,
    ICharacterDomainService? characters = null) : IDungeonVisitDomainService
{
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
        if (await ValidateAdmissionAsync(participants, dungeonId, facts, cancellationToken).ConfigureAwait(false) is { } refusal)
            return refusal;
        DungeonLayout? layout;
        try
        {
            layout = await layouts.ResolveAsync(dungeonId, facts, Random.Shared.Next(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException error)
        {
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message);
        }
        return layout is null ? DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound)
            : await EnterPreparedCoreAsync(participants, layout, facts, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<DungeonEntryAttempt> TryEnterPreparedPartyAsync(
        IReadOnlyList<DungeonRunParticipant> participants, DungeonLayout layout,
        DungeonEntryFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(facts);
        if (await ValidateAdmissionAsync(participants, layout.DungeonId, facts, cancellationToken).ConfigureAwait(false) is { } refusal)
            return refusal;
        return await EnterPreparedCoreAsync(participants, layout, facts, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<DungeonEntryAttempt?> ValidateAdmissionAsync(
        IReadOnlyList<DungeonRunParticipant> participants, int dungeonId,
        DungeonEntryFacts facts, CancellationToken cancellationToken)
    {
        if (participants.Any(x => tower?.IsInTower(x.CharacterId) == true))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.AlreadyInDungeon);

        var difficulty = facts.Difficulty;
        if (!Enum.IsDefined(difficulty)) return DungeonEntryAttempt.Refused(DungeonEntryFailure.DifficultyNotUnlocked);
        DungeonDefinition? definition;
        try
        {
            definition = await layouts.GetDefinitionAsync(dungeonId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException error)
        {
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message);
        }
        foreach (var member in participants)
        {
            if (definition is { HasStoryMode: false } && characters is not null)
            {
                var progress = await characters.Get(member.CharacterId).ProgressAsync(cancellationToken).ConfigureAwait(false);
                if (progress is null || progress.Level < definition.MinimumRequiredLevel)
                    return new(null, DungeonEntryFailure.LevelTooLow, member.Slot);
            }
            var maximum = await difficulties.GetMaximumAsync(member.CharacterId, dungeonId, cancellationToken)
                .ConfigureAwait(false);
            var cycle = facts.Intent.Kind == DungeonEntryIntentKind.Cycle;
            if (cycle && facts.Intent.DirectedDungeonId != dungeonId)
                return new(null, DungeonEntryFailure.DifficultyNotUnlocked, member.Slot);
            bool allowed;
            try
            {
                allowed = cycle
                    ? await difficultyRules.IsUnlockedAsync(dungeonId, difficulty, maximum, cancellationToken).ConfigureAwait(false)
                    : await difficultyRules.AllowsAsync(dungeonId, difficulty, maximum, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException error)
            {
                return new(null, DungeonEntryFailure.ConfigurationInvalid, member.Slot, error.Message);
            }
            if (!allowed)
                return new(null, DungeonEntryFailure.DifficultyNotUnlocked, member.Slot);
        }
        return null;
    }

    private async ValueTask<DungeonEntryAttempt> EnterPreparedCoreAsync(
        IReadOnlyList<DungeonRunParticipant> participants, DungeonLayout layout,
        DungeonEntryFacts facts, CancellationToken cancellationToken)
    {
        var difficulty = facts.Difficulty;
        if (layout.ConsumesFatigue)
            foreach (var participant in participants)
                if ((await fatigue.BalanceOfAsync(participant.CharacterId, cancellationToken).ConfigureAwait(false)).Remaining == 0)
                    return new(null, DungeonEntryFailure.InsufficientFatigue, participant.Slot);

        DungeonDefinition? definition;
        try
        {
            definition = await layouts.GetDefinitionAsync(layout.DungeonId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException error)
        {
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message);
        }
        IDungeonEntryMaterialPlan? materialPlan = null;
        if (definition?.RequiredItems is { Count: > 0 } requirements)
        {
            var preparation = await (materials ?? throw new InvalidOperationException("Dungeon entry material service is unavailable."))
                .PrepareAsync(participants, requirements, cancellationToken).ConfigureAwait(false);
            if (preparation.Plan is null) return new(null, DungeonEntryFailure.MissingRequiredItem, preparation.BlockedSlot);
            materialPlan = preparation.Plan;
        }
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
                await fatigue.SpendAsync(participant.CharacterId, 1, cancellationToken).ConfigureAwait(false);
        var change = runs.PreparePartyEntry(
            participants, layout, difficulty, initialCell, runId, startedAt);
        var createdVisits = participants.ToDictionary(
            participant => participant.CharacterId,
            participant => this.DescribeCreatedCell(
                change.Run with { Participant = participant },
                firstEntry: true));
        var inventoryChanges = materialPlan is null ? [] : await materialPlan.ConsumeAsync(cancellationToken).ConfigureAwait(false);
        return DungeonEntryAttempt.Accepted(new DungeonPartyEntry(layout, createdVisits, change) { InventoryChanges = inventoryChanges });
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

        // The caller's view can predate a leader departure. Cell preparation reads the current
        // party-backed run view; the existing room version still guards the final move commit.
        var latest = runs.Find(characterId);
        if (latest is null || latest.RunId != current.RunId
            || latest.RoomVersion != current.RoomVersion)
            return new(null, DungeonMoveFailure.StaleRun);

        var firstEntry = !latest.VisitedCells.Contains(target);
        if (!latest.Layout.Cells.ContainsKey(target))
            return new(null, DungeonMoveFailure.CellNotFound);
        if (!latest.Layout.CanTraverse(latest.CurrentCell, target))
            return new(null, DungeonMoveFailure.Disconnected);

        DungeonCellSeed? preparedCell = null;
        if (firstEntry)
        {
            var selected = await layouts.SelectMapAsync(latest.Layout, latest.CurrentCell, target, cancellationToken)
                .ConfigureAwait(false);
            if (selected.MapId is not { } mapId) return new(null, DungeonMoveFailure.NoCompatibleMap);
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
        if (entered is null) return new(null, DungeonMoveFailure.StateRejected);
        if (firstEntry && entered.Layout.ConsumesFatigue)
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
