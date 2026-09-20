using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonVisitDomainServiceTests
{
    private static readonly DungeonLayout Layout = new(
        DungeonId: 10000,
        MazeIndex: 0,
        Width: 1,
        Height: 1,
        StartCell: new MazeCell(0, 0),
        BossCell: new MazeCell(0, 0),
        StartRoomMapId: 61000,
        Seed: 123,
        Rooms: new Dictionary<MazeCell, int> { [new MazeCell(0, 0)] = 61000 });

    private static readonly DungeonLayout TwoRoomLayout = new(
        DungeonId: 10001,
        MazeIndex: 0,
        Width: 2,
        Height: 1,
        StartCell: new MazeCell(0, 0),
        BossCell: new MazeCell(1, 0),
        StartRoomMapId: 61000,
        Seed: 456,
        Rooms: new Dictionary<MazeCell, int>
        {
            [new MazeCell(0, 0)] = 61000,
            [new MazeCell(1, 0)] = 61001,
        });

    [Fact]
    public async Task Locked_single_player_difficulty_does_not_create_a_run_or_room()
    {
        var runs = new DungeonRunDomainService();
        var cells = new RecordingCellFactory();
        var layouts = new RecordingLayouts();
        var visits = new DungeonVisitDomainService(layouts, runs, new ImmediateSelection(), cells,
            new MaximumDifficulty(DungeonDifficulty.Normal), new DungeonDifficultyRuleService());
        var result = await visits.TryEnterPartyAsync(
            [new(new AccountId(1), new CharacterId(1), 0, true)], 150,
            new DungeonEntryFacts(DungeonDifficulty.Adventure, [], []), default);
        Assert.Equal(DungeonEntryFailure.DifficultyNotUnlocked, result.Failure);
        Assert.Null(result.Entry);
        Assert.Null(runs.Find(new CharacterId(1)));
        Assert.Equal(0, cells.CallCount);
        Assert.Equal(0, layouts.CallCount);
    }

    [Fact]
    public async Task Adventure_is_fixed_for_layout_first_room_next_room_and_revisit()
    {
        var runs = new DungeonRunDomainService();
        var cells = new RecordingCellFactory();
        var layouts = new RecordingLayouts(TwoRoomLayout);
        var visits = new DungeonVisitDomainService(layouts, runs, new ImmediateSelection(), cells,
            new MaximumDifficulty(DungeonDifficulty.Adventure), new DungeonDifficultyRuleService());
        var character = new CharacterId(1);
        var result = await visits.TryEnterPartyAsync(
            [new(new AccountId(1), character, 0, true)], TwoRoomLayout.DungeonId,
            new DungeonEntryFacts(DungeonDifficulty.Adventure, [], []), default);
        Assert.Null(result.Failure);
        var first = Assert.IsType<DungeonPartyEntry>(result.Entry).Visits[character];
        Assert.Equal(DungeonDifficulty.Adventure, layouts.Facts!.Difficulty);
        Assert.Equal(DungeonDifficulty.Adventure, first.Run.Difficulty);
        var next = await visits.MoveAsync(character, first.Run, new MazeCell(1, 0), default);
        Assert.NotNull(next);
        var revisit = await visits.MoveAsync(character, next.Run, first.Run.CurrentCell, default);
        Assert.NotNull(revisit);
        Assert.False(revisit.FirstEntry);
        Assert.Equal(DungeonDifficulty.Adventure, revisit.Run.Difficulty);
        Assert.Equal(2, cells.Contexts.Count);
        Assert.All(cells.Contexts, context => Assert.Equal(DungeonDifficulty.Adventure, context.Difficulty));
    }

    [Fact]
    public async Task Party_entry_resolves_one_layout_and_describes_one_shared_start_room()
    {
        DungeonRunParticipant[] party =
        [
            new(new AccountId(11), new CharacterId(1), 0, IsLeader: true),
            new(new AccountId(22), new CharacterId(2), 1, IsLeader: false),
        ];
        var layouts = new RecordingLayouts();
        var visits = new DungeonVisitDomainService(
            layouts,
            new DungeonRunDomainService(),
            new ImmediateSelection(),
            new RecordingCellFactory(), new MaximumDifficulty(), new DungeonDifficultyRuleService());

        var entered = await visits.EnterPartyAsync(
            party, Layout.DungeonId, difficulty: (DungeonDifficulty)2, DungeonEntryFacts.None, CancellationToken.None);

        Assert.NotNull(entered);
        Assert.Equal(1, layouts.CallCount);
        Assert.Equal(party.Select(participant => participant.CharacterId), entered.Visits.Keys);
        Assert.Single(entered.Visits.Values.Select(visit => visit.Run.RunId).Distinct());
        Assert.Single(entered.Visits.Values.Select(visit => visit.Run.CurrentCell).Distinct());
        Assert.All(entered.Visits.Values, visit => Assert.True(visit.FirstEntry));
        Assert.NotNull(entered.Change);
        Assert.Equal(DungeonEntryChangeKind.Prepared, entered.Change.Kind);
        Assert.Equal(
            party.Select(participant => participant.CharacterId),
            entered.Change.Recipients);
    }

    [Fact]
    public async Task Failed_initial_cell_creation_does_not_publish_any_run()
    {
        DungeonRunParticipant[] party =
        [
            new(new AccountId(11), new CharacterId(1), 0, IsLeader: true),
            new(new AccountId(22), new CharacterId(2), 1, IsLeader: false),
        ];
        var runs = new DungeonRunDomainService();
        var visits = new DungeonVisitDomainService(
            new RecordingLayouts(),
            runs,
            new ImmediateSelection(),
            new ThrowingCellFactory(), new MaximumDifficulty(), new DungeonDifficultyRuleService());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await visits.EnterPartyAsync(
                party,
                Layout.DungeonId,
                difficulty: (DungeonDifficulty)2,
                DungeonEntryFacts.None,
                CancellationToken.None));

        Assert.All(party, participant => Assert.Null(runs.Find(participant.CharacterId)));
    }

    [Fact]
    public async Task Failed_first_visit_does_not_move_the_run_before_the_cell_is_ready()
    {
        var character = new CharacterId(1);
        var runs = new DungeonRunDomainService();
        var current = runs.Begin(character, TwoRoomLayout, difficulty: 0);
        var visits = new DungeonVisitDomainService(
            new RecordingLayouts(),
            runs,
            new ImmediateSelection(),
            new ThrowingCellFactory(), new MaximumDifficulty(), new DungeonDifficultyRuleService());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await visits.MoveAsync(
                character,
                current,
                new MazeCell(1, 0),
                CancellationToken.None));

        var after = runs.Find(character);
        Assert.NotNull(after);
        Assert.Equal(new MazeCell(0, 0), after.CurrentCell);
        Assert.Equal(0, after.RoomVersion);
    }

    [Fact]
    public async Task Revisit_reuses_the_published_cell_without_calling_the_factory_again()
    {
        var character = new CharacterId(1);
        var runs = new DungeonRunDomainService();
        var current = runs.Begin(character, TwoRoomLayout, difficulty: 0);
        var factory = new RecordingCellFactory();
        var visits = new DungeonVisitDomainService(
            new RecordingLayouts(),
            runs,
            new ImmediateSelection(),
            factory, new MaximumDifficulty(), new DungeonDifficultyRuleService());

        var second = await visits.MoveAsync(
            character, current, new MazeCell(1, 0), CancellationToken.None);
        Assert.NotNull(second);
        var returned = await visits.MoveAsync(
            character, second.Run, new MazeCell(0, 0), CancellationToken.None);

        Assert.NotNull(returned);
        Assert.False(returned.FirstEntry);
        Assert.Empty(returned.Actors);
        Assert.Empty(returned.FloorDrops);
        Assert.Equal(1, factory.CallCount);
        Assert.Equal(new MazeCell(0, 0), returned.Run.CurrentCellState.Cell);
    }

    [Fact]
    public async Task First_cell_after_leader_leaves_uses_current_party_even_with_stale_caller_view()
    {
        var leader = new DungeonRunParticipant(new AccountId(11), new CharacterId(1), 0, true);
        var successor = new DungeonRunParticipant(new AccountId(22), new CharacterId(2), 1, false);
        var parties = new PartyService(
            new StubPartyMemberStatusDomainService(), new PartyModeRuleService(),
            TimeProvider.System, EmptyPartyMemberSessionEndpointSource.Instance);
        var scope = new PartyDirectoryScope(11, 1, 1);
        var first = new PartyCharacterSnapshot(leader.AccountId, leader.CharacterId, 1, scope);
        var second = new PartyCharacterSnapshot(successor.AccountId, successor.CharacterId, 2, scope);
        Assert.True(parties.RequestInvite(first, second,
            new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
        Assert.True(parties.AcceptInvite(second, first).Succeeded);
        var runs = new DungeonRunDomainService(parties);
        var start = runs.PreparePartyEntry(
            [leader, successor], TwoRoomLayout, 0,
            DungeonCellSeed.CompleteEmpty(TwoRoomLayout.StartCell, TwoRoomLayout.StartRoomMapId),
            DungeonRunId.New(), DateTimeOffset.UnixEpoch).Run;
        var factory = new RecordingCellFactory();
        var visits = new DungeonVisitDomainService(
            new RecordingLayouts(), runs, new ImmediateSelection(), factory, new MaximumDifficulty(), new DungeonDifficultyRuleService());

        Assert.True(parties.LeaveParty(leader.CharacterId).Succeeded);
        var moved = await visits.MoveAsync(
            successor.CharacterId, start, new MazeCell(1, 0), CancellationToken.None);

        Assert.NotNull(moved);
        Assert.Equal(start.RunId, moved.Run.RunId);
        Assert.Equal(start.PartyKey, moved.Run.PartyKey);
        var generatedFor = Assert.Single(Assert.Single(factory.Contexts).Participants);
        Assert.Equal(successor.CharacterId, generatedFor.CharacterId);
        Assert.True(generatedFor.IsLeader);
        var returned = Assert.IsType<DungeonRoomVisit>(await visits.MoveAsync(
            successor.CharacterId, moved.Run, TwoRoomLayout.StartCell, CancellationToken.None));
        var revisited = Assert.IsType<DungeonRoomVisit>(await visits.MoveAsync(
            successor.CharacterId, returned.Run, new MazeCell(1, 0), CancellationToken.None));
        Assert.False(revisited.FirstEntry);
        Assert.Single(factory.Contexts);
    }

    [Fact]
    public async Task Concurrent_first_visits_publish_only_one_complete_cell_and_reuse_it()
    {
        var character = new CharacterId(1);
        var runs = new DungeonRunDomainService();
        var current = runs.Begin(character, TwoRoomLayout, difficulty: 0);
        var factory = new RacingCellFactory();
        var visits = new DungeonVisitDomainService(
            new RecordingLayouts(),
            runs,
            new ImmediateSelection(),
            factory, new MaximumDifficulty(), new DungeonDifficultyRuleService());
        var target = new MazeCell(1, 0);

        var attempts = await Task.WhenAll(
            visits.MoveAsync(character, current, target, CancellationToken.None).AsTask(),
            visits.MoveAsync(character, current, target, CancellationToken.None).AsTask());

        var adopted = Assert.Single(attempts, visit => visit is not null)!;
        Assert.Single(adopted.Run.CurrentCellState.Actors);
        Assert.Contains(
            adopted.Run.CurrentCellState.Actors[0].Actor.Code,
            factory.CreatedActorCodes);
        Assert.Equal(2, factory.CallCount);

        var back = Assert.IsType<DungeonRoomVisit>(await visits.MoveAsync(
            character, adopted.Run, TwoRoomLayout.StartCell, CancellationToken.None));
        var revisited = Assert.IsType<DungeonRoomVisit>(await visits.MoveAsync(
            character, back.Run, target, CancellationToken.None));

        Assert.Equal(2, factory.CallCount);
        Assert.Equal(
            adopted.Run.CurrentCellState.Actors[0].Actor.Code,
            revisited.Run.CurrentCellState.Actors[0].Actor.Code);
        Assert.Empty(revisited.Actors);
    }

    private sealed class RecordingLayouts(DungeonLayout? layout = null) : IDungeonLayoutRuleService
    {
        public int CallCount { get; private set; }
        public DungeonEntryFacts? Facts { get; private set; }

        public ValueTask<DungeonLayout?> ResolveAsync(
            int dungeonId,
            DungeonEntryFacts facts,
            int seed,
            CancellationToken cancellationToken)
        {
            this.CallCount++;
            this.Facts = facts;
            return ValueTask.FromResult<DungeonLayout?>(layout ?? Layout);
        }
    }

    private sealed class ThrowingCellFactory : IDungeonCellFactory
    {
        public ValueTask<DungeonCellSeed> CreateAsync(
            DungeonCellCreationContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("cell construction failed");
    }

    private sealed class RecordingCellFactory : IDungeonCellFactory
    {
        public int CallCount { get; private set; }
        public List<DungeonCellCreationContext> Contexts { get; } = [];

        public ValueTask<DungeonCellSeed> CreateAsync(
            DungeonCellCreationContext context, CancellationToken cancellationToken)
        {
            this.CallCount++;
            this.Contexts.Add(context);
            return ValueTask.FromResult(DungeonCellSeed.CompleteEmpty(context.Cell, context.MapId));
        }
    }

    private sealed class RacingCellFactory : IDungeonCellFactory
    {
        private readonly TaskCompletionSource bothStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;

        public int CallCount => Volatile.Read(ref this.calls);
        public List<int> CreatedActorCodes { get; } = [];

        public async ValueTask<DungeonCellSeed> CreateAsync(
            DungeonCellCreationContext context,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref this.calls);
            lock (this.CreatedActorCodes)
                this.CreatedActorCodes.Add(60000 + call);
            if (call == 2)
                this.bothStarted.TrySetResult();
            await this.bothStarted.Task.WaitAsync(cancellationToken);
            return new DungeonCellSeed(
                context.Cell,
                context.MapId,
                FirstActorSequence: 100,
                Actors:
                [
                    new DungeonCellActorState(
                        Sequence: 100,
                        new DungeonRoomActor(60000 + call, 1, 0),
                        IsAiCharacter: false,
                        IsDead: false,
                        PendingLoot: []),
                ],
                GroundItems: []);
        }
    }

    private sealed class ImmediateSelection : IDungeonSelectionRuleService
    {
        public bool DefersSelectionState(byte characterLevel) => false;
    }
    private sealed class MaximumDifficulty(DungeonDifficulty maximum = DungeonDifficulty.Tier4) : Dfo.Server.Abstractions.Characters.ICharacterDungeonDifficultyDomainService
    {
        public ValueTask<DungeonDifficulty> GetMaximumAsync(CharacterId characterId, int dungeonId, CancellationToken token) => ValueTask.FromResult(maximum);
        public ValueTask<IReadOnlyList<Dfo.Server.Abstractions.Characters.CharacterDungeonDifficulty>> GetRecordedAsync(CharacterId characterId, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<DungeonDifficulty> UnlockAsync(CharacterId characterId, int dungeonId, DungeonDifficulty difficulty, CancellationToken token) => throw new NotSupportedException();
    }
}
