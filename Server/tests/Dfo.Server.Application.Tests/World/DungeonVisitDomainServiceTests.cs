using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Commands.Pipeline;
using Microsoft.Extensions.DependencyInjection;
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
    [Fact]
    public async Task Prepared_mirror_layout_uses_validated_difficulty_without_reading_historical_unlock_ledger()
    {
        var runs = new DungeonRunDomainService();
        var cells = new RecordingCellFactory();
        var fatigue = new TestFatigue();
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(new RecordingLayouts(), runs, new ImmediateSelection(), cells,
            new MaximumDifficulty(storageFailure: true), new DungeonDifficultyRuleService(), fatigue);
        var layout = Layout with { Mirror = new(8000, 31, 14299) };
        var result = await visits.TryEnterPreparedPartyAsync([new(new(1), new(71), 0, true)], layout,
            DungeonEntryFacts.None with { Difficulty = DungeonDifficulty.Tier3 }, default);
        Assert.NotNull(result.Entry);
        Assert.Same(layout.Mirror, result.Entry.Layout.Mirror);
        Assert.Equal(DungeonDifficulty.Tier3, result.Entry.Visits[new(71)].Run.Difficulty);
        Assert.Equal(1, fatigue.Spent[new(71)]);
        Assert.Equal(1, cells.CallCount);
    }

    [Fact]
    public async Task Cycle_cannot_bypass_unlock_checks_or_pay_before_a_valid_difficulty_is_selected()
    {
        var runs = new DungeonRunDomainService();
        var cells = new RecordingCellFactory();
        var fatigue = new TestFatigue();
        var materials = new UnexpectedMaterials();
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(new RecordingLayouts(), runs, new ImmediateSelection(), cells,
            new MaximumDifficulty(DungeonDifficulty.Normal), new DungeonDifficultyRuleService(), fatigue, materials: materials);
        var facts = new DungeonEntryFacts(DungeonDifficulty.Tier3, [1776], [], DungeonEntryIntent.Cycle(Layout.DungeonId, 1776));
        var result = await visits.TryEnterPartyAsync([new(new(1), new(71), 0, true)], Layout.DungeonId, facts, default);
        Assert.Equal(DungeonEntryFailure.DifficultyNotUnlocked, result.Failure);
        Assert.Equal(0, cells.CallCount);
        Assert.Empty(fatigue.Spent);
        Assert.Equal(0, materials.Calls);
        Assert.Null(runs.Find(new CharacterId(71)));
    }

    [Theory]
    [InlineData("definition")]
    [InlineData("resolve")]
    [InlineData("prepared-definition")]
    [InlineData("difficulty-rule")]
    public async Task Invalid_entry_configuration_refuses_before_cell_run_materials_or_fatigue(string boundary)
    {
        var runs = new DungeonRunDomainService();
        var cells = new RecordingCellFactory();
        var fatigue = new TestFatigue();
        var materials = new UnexpectedMaterials();
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(new FaultingEntryStructures(boundary), runs,
            new ImmediateSelection(), cells, new MaximumDifficulty(), new DungeonDifficultyRuleService(
                boundary == "difficulty-rule" ? new InvalidDefinitions() : null),
            fatigue, materials: materials);
        var result = boundary == "prepared-definition"
            ? await visits.TryEnterPreparedPartyAsync([new(new(1), new(71), 0, true)], Layout, DungeonEntryFacts.None, default)
            : await visits.TryEnterPartyAsync([new(new(1), new(71), 0, true)], Layout.DungeonId, DungeonEntryFacts.None, default);
        Assert.Equal(DungeonEntryFailure.ConfigurationInvalid, result.Failure);
        Assert.Equal("invalid test maze", result.Detail);
        Assert.Null(result.Entry);
        Assert.Equal(0, cells.CallCount);
        Assert.Empty(fatigue.Spent);
        Assert.Null(runs.Find(new CharacterId(71)));
        Assert.Equal(0, materials.Calls);
    }

    [Fact]
    public async Task InvalidData_from_cell_creation_is_not_misclassified_as_configuration_refusal()
    {
        var runs = new DungeonRunDomainService();
        var fatigue = new TestFatigue();
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(new RecordingLayouts(), runs, new ImmediateSelection(),
            new InvalidDataCellFactory(), new MaximumDifficulty(), new DungeonDifficultyRuleService(), fatigue);
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await visits.TryEnterPartyAsync([new(new(1), new(71), 0, true)], Layout.DungeonId, DungeonEntryFacts.None, default));
        Assert.Equal("unexpected cell failure", error.Message);
        Assert.Empty(fatigue.Spent);
        Assert.Null(runs.Find(new CharacterId(71)));
    }

    private sealed class FaultingEntryStructures(string boundary) : IDungeonStructureRuleService
    {
        public ValueTask<DungeonDefinition?> GetDefinitionAsync(int id, CancellationToken token)
        {
            if (boundary is "definition" or "prepared-definition")
                throw new InvalidDataException("invalid test maze");
            return ValueTask.FromResult<DungeonDefinition?>(new(id, "test", 1, true, [])
                { RequiredItems = [] });
        }
        public ValueTask<DungeonLayout?> ResolveAsync(int id, DungeonEntryFacts facts, int seed, CancellationToken token) =>
            boundary == "resolve" ? throw new InvalidDataException("invalid test maze") : ValueTask.FromResult<DungeonLayout?>(Layout);
        public ValueTask<DungeonMapSelection> SelectMapAsync(DungeonLayout layout, MazeCell? from, MazeCell target, CancellationToken token) =>
            throw new NotSupportedException();
    }
    [Fact]
    public async Task InvalidData_from_difficulty_storage_is_not_misclassified_as_configuration_refusal()
    {
        var runs = new DungeonRunDomainService();
        var cells = new RecordingCellFactory();
        var fatigue = new TestFatigue();
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(new RecordingLayouts(), runs, new ImmediateSelection(), cells,
            new MaximumDifficulty(storageFailure: true), new DungeonDifficultyRuleService(), fatigue);
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await visits.TryEnterPartyAsync([new(new(1), new(71), 0, true)], Layout.DungeonId, DungeonEntryFacts.None, default));
        Assert.Equal("unexpected difficulty storage failure", error.Message);
        Assert.Equal(0, cells.CallCount);
        Assert.Empty(fatigue.Spent);
        Assert.Null(runs.Find(new CharacterId(71)));
    }
    private sealed class InvalidDefinitions : IDungeonDefinitionSource
    {
        public ValueTask<DungeonDefinition?> GetAsync(int id, CancellationToken token) =>
            throw new InvalidDataException("invalid test maze");
    }
    private sealed class UnexpectedMaterials : IDungeonEntryMaterialsDomainService
    {
        public int Calls { get; private set; }
        public ValueTask<DungeonEntryMaterialPreparation> PrepareAsync(IReadOnlyList<DungeonRunParticipant> participants,
            IReadOnlyList<DungeonRequiredItem> requirements, CancellationToken token)
        {
            Calls++;
            throw new InvalidOperationException("configuration should be rejected before material preparation");
        }
    }
    private sealed class InvalidDataCellFactory : IDungeonCellFactory
    {
        public ValueTask<DungeonCellSeed> CreateAsync(DungeonCellCreationContext context, CancellationToken token) =>
            throw new InvalidDataException("unexpected cell failure");
    }

    [SkippableFact]
    public async Task Real_gent_south_gate_ordinary_entry_reuses_its_start_room_when_returning()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();
        var rules = new DungeonStructureRuleService(content.GetRequiredService<IDungeonDefinitionSource>(),
            content.GetRequiredService<IDungeonMapDefinitionSource>());
        foreach (var seed in new[] { 0, 42, -777, int.MaxValue })
        {
            var layout = await rules.ResolveAsync(82, DungeonEntryFacts.None, seed, default);
            Assert.NotNull(layout);
            Assert.Equal(0, layout.MazeIndex);
            Assert.Equal(DungeonRunPurpose.Ordinary, layout.Purpose);
            Assert.Equal(new MazeCell(3, 2), layout.StartCell);
            Assert.Equal(22419, layout.StartRoomMapId);
        }
        var runs = new DungeonRunDomainService();
        var cells = new RecordingCellFactory();
        var fatigue = new TestFatigue();
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(rules, runs, new ImmediateSelection(), cells,
            new MaximumDifficulty(), new DungeonDifficultyRuleService(), fatigue);
        var character = new CharacterId(71);
        var attempt = await visits.TryEnterPartyAsync([new(new(1), character, 0, true)], 82,
            DungeonEntryFacts.None with { Difficulty = DungeonDifficulty.Adventure }, default);
        Assert.Null(attempt.Failure);
        Assert.NotNull(attempt.Entry);
        Assert.Equal(1, cells.CallCount);
        var next = await visits.MoveAsync(character, runs.Find(character)!, new(2, 2), default);
        Assert.NotNull(next);
        Assert.True(next.FirstEntry);
        Assert.Equal(22417, next.Run.CurrentRoomMapId);
        var spent = fatigue.Spent.GetValueOrDefault(character);
        var back = await visits.MoveAsync(character, next.Run, new(3, 2), default);
        Assert.NotNull(back);
        Assert.False(back.FirstEntry);
        Assert.Equal(22419, back.Run.CurrentRoomMapId);
        Assert.Equal(2, cells.CallCount);
        Assert.Equal(spent, fatigue.Spent.GetValueOrDefault(character));
    }

    [SkippableFact]
    public async Task Real_consciousness_route_reaches_implicit_cells_and_revisits_the_same_shared_map()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();
        var rules = new DungeonStructureRuleService(content.GetRequiredService<IDungeonDefinitionSource>(),
            content.GetRequiredService<IDungeonMapDefinitionSource>());
        var layout = (await rules.ResolveAsync(77, DungeonEntryFacts.None, 42, default))!;
        var maze = (await rules.GetDefinitionAsync(77, default))!.Mazes[0];
        // Freeze one of the declared starts so this reproduces the user's actual route.
        layout = layout with { StartCell = new(0, 1), BossCell = new(4, 0), StartRoomMapId = 15303 };
        Assert.Equal(16, layout.Cells.Count);
        Assert.Equal([15268, 15316], maze.Cells[new(1, 0)].MapCandidates);
        var runs = new DungeonRunDomainService();
        var cells = new RecordingCellFactory();
        var fatigue = new TestFatigue();
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(rules, runs, new ImmediateSelection(), cells,
            new MaximumDifficulty(), new DungeonDifficultyRuleService(), fatigue);
        DungeonRunParticipant[] party = [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)];
        runs.PreparePartyEntry(party, layout, 0, DungeonCellSeed.CompleteEmpty(layout.StartCell, layout.StartRoomMapId),
            DungeonRunId.New(), DateTimeOffset.UtcNow);
        DungeonRoomVisit? entered = null;
        foreach (var target in new MazeCell[] { new(0, 0), new(1, 0), new(1, 1), new(2, 1), new(3, 1), new(3, 2), new(2, 2) })
        {
            entered = await visits.MoveAsync(new(71), runs.Find(new CharacterId(71))!, target, default);
            Assert.NotNull(entered);
            Assert.Equal(entered.Run.CurrentRoomMapId, runs.Find(new CharacterId(72))!.CurrentRoomMapId);
        }
        var retainedMap = entered!.Run.CurrentRoomMapId;
        var count = cells.CallCount;
        var back = await visits.MoveAsync(new(71), entered.Run, new(3, 2), default);
        var revisit = await visits.MoveAsync(new(71), back!.Run, new(2, 2), default);
        Assert.NotNull(revisit);
        Assert.False(revisit.FirstEntry);
        Assert.Equal(retainedMap, revisit.Run.CurrentRoomMapId);
        Assert.Equal(count, cells.CallCount);
        var snapshot = runs.Find(new CharacterId(71))!;
        var refused = await visits.TryMoveAsync(new(71), snapshot, new(2, 3), default);
        Assert.Equal(DungeonMoveFailure.CellNotFound, refused.Failure);
        Assert.Equal(snapshot.RoomVersion, runs.Find(new CharacterId(71))!.RoomVersion);
        Assert.Equal(snapshot.CurrentRoomMapId, runs.Find(new CharacterId(71))!.CurrentRoomMapId);
        Assert.Equal(count, cells.CallCount);
        Assert.Equal(7, fatigue.Spent[new(71)]);
        Assert.Equal(7, fatigue.Spent[new(72)]);
    }

    [SkippableFact]
    public async Task No_compatible_map_keeps_the_current_room_and_spends_no_fatigue()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();
        var rules = new DungeonStructureRuleService(content.GetRequiredService<IDungeonDefinitionSource>(),
            content.GetRequiredService<IDungeonMapDefinitionSource>());
        var layout = TwoRoomLayout with { Cells = new Dictionary<MazeCell, DungeonCellDefinition>
            { [new(0, 0)] = DungeonCellDefinition.Fixed(61000),
                [new(1, 0)] = DungeonCellDefinition.Fixed(999999) with { BossMapCandidates = [999999] } } };
        var runs = new DungeonRunDomainService();
        var cells = new RecordingCellFactory();
        var fatigue = new TestFatigue();
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(rules, runs, new ImmediateSelection(), cells,
            new MaximumDifficulty(), new DungeonDifficultyRuleService(), fatigue);
        var character = new CharacterId(71);
        runs.PreparePartyEntry([new(new(1), character, 0, true)], layout, 0,
            DungeonCellSeed.CompleteEmpty(layout.StartCell, layout.StartRoomMapId), DungeonRunId.New(), DateTimeOffset.UtcNow);
        var current = runs.Find(character)!;
        var attempt = await visits.TryMoveAsync(character, current, new(1, 0), default);
        Assert.Equal(DungeonMoveFailure.NoCompatibleMap, attempt.Failure);
        Assert.Null(attempt.Visit);
        Assert.Equal(current.CurrentCell, runs.Find(character)!.CurrentCell);
        Assert.Equal(current.RoomVersion, runs.Find(character)!.RoomVersion);
        Assert.Equal(0, cells.CallCount);
        Assert.Empty(fatigue.Spent);
    }

    [Theory]
    [InlineData(186)]
    [InlineData(187)]
    public async Task Persisted_balance_charges_each_new_room_once_and_zero_can_finish(int alreadyUsed)
    {
        await using var f = new LearningFixture();
        await f.SeedAsync();
        var fatigue = f.Services.GetRequiredService<ICharacterFatigueDomainService>();
        await using (var tx = f.Scope.Begin())
        {
            await fatigue.SpendAsync(f.Session.CharacterId, (ushort)alreadyUsed, default);
            await tx.CommitAsync(default);
        }
        var runs = new DungeonRunDomainService(f.Scope);
        var visits = Bound(f, runs, TwoRoomLayout);
        var entry = await visits.EnterAsync(f.Session.CharacterId, 10001, DungeonDifficulty.Normal, new(DungeonDifficulty.Normal, [], []), default);
        Assert.NotNull(entry);
        var first = runs.Find(f.Session.CharacterId)!;
        Assert.Equal(alreadyUsed + 1, (await fatigue.BalanceOfAsync(f.Session.CharacterId, default)).Used);
        var next = await visits.MoveAsync(f.Session.CharacterId, first, new(1, 0), default);
        Assert.NotNull(next);
        Assert.Null(await visits.MoveAsync(f.Session.CharacterId, first, new(1, 0), default));
        Assert.NotNull(await visits.MoveAsync(f.Session.CharacterId, next.Run, new(0, 0), default));
        var balance = await fatigue.BalanceOfAsync(f.Session.CharacterId, default);
        Assert.Equal(188, balance.Used);
        Assert.Equal(188, balance.ConsumedToday);
        Assert.Null(await visits.EnterAsync(f.Session.CharacterId, 10001, DungeonDifficulty.Normal, new(DungeonDifficulty.Normal, [], []), default));
        Assert.Equal(first.RunId, runs.Find(f.Session.CharacterId)!.RunId);
    }

    [Fact]
    public async Task Party_admission_is_all_or_none_and_exempt_dungeon_accepts_zero()
    {
        await using var f = new LearningFixture();
        await f.SeedAsync();
        var fatigue = f.Services.GetRequiredService<ICharacterFatigueDomainService>();
        await using (var tx = f.Scope.Begin())
        {
            await fatigue.SpendAsync(new(72), 188, default);
            await tx.CommitAsync(default);
        }
        var runs = new DungeonRunDomainService(f.Scope);
        DungeonRunParticipant[] members = [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)];
        var result = await Bound(f, runs, TwoRoomLayout).TryEnterPartyAsync(members, 10001, new(DungeonDifficulty.Normal, [], []), default);
        Assert.Equal(DungeonEntryFailure.InsufficientFatigue, result.Failure);
        Assert.Equal((byte)1, result.BlockedSlot);
        Assert.Null(runs.Find(new CharacterId(71)));
        Assert.Equal(0, (await fatigue.BalanceOfAsync(new(71), default)).Used);
        var allowed = await Bound(f, runs, TwoRoomLayout with { ConsumesFatigue = false }).TryEnterPartyAsync(members, 10001, new(DungeonDifficulty.Normal, [], []), default);
        Assert.NotNull(allowed.Entry);
        var moved = await Bound(f, runs, TwoRoomLayout).MoveAsync(new(71), runs.Find(new CharacterId(71))!, new(1, 0), default);
        Assert.NotNull(moved);
        Assert.Equal(0, (await fatigue.BalanceOfAsync(new(71), default)).Used);
        Assert.Equal(188, (await fatigue.BalanceOfAsync(new(72), default)).Used);
    }

    [Fact]
    public async Task Aborted_transaction_publishes_neither_entry_nor_move_nor_spending()
    {
        await using var f = new LearningFixture();
        await f.SeedAsync();
        var runs = new DungeonRunDomainService(f.Scope);
        var visits = Bound(f, runs, TwoRoomLayout);
        var fatigue = f.Services.GetRequiredService<ICharacterFatigueDomainService>();
        await using (var tx = f.Scope.Begin())
        {
            Assert.NotNull(await visits.EnterAsync(f.Session.CharacterId, 10001, DungeonDifficulty.Normal, new(DungeonDifficulty.Normal, [], []), default));
            Assert.Null(runs.Find(f.Session.CharacterId));
        }
        Assert.Null(runs.Find(f.Session.CharacterId));
        Assert.Equal(0, (await fatigue.BalanceOfAsync(f.Session.CharacterId, default)).Used);
        await visits.EnterAsync(f.Session.CharacterId, 10001, DungeonDifficulty.Normal, new(DungeonDifficulty.Normal, [], []), default);
        var first = runs.Find(f.Session.CharacterId)!;
        await using (var tx = f.Scope.Begin())
        {
            Assert.NotNull(await visits.MoveAsync(f.Session.CharacterId, first, new(1, 0), default));
            Assert.Equal(first.CurrentCell, runs.Find(f.Session.CharacterId)!.CurrentCell);
        }
        Assert.Equal(first.RoomVersion, runs.Find(f.Session.CharacterId)!.RoomVersion);
        Assert.Equal(1, (await fatigue.BalanceOfAsync(f.Session.CharacterId, default)).Used);
        // A real SQLite write failure must also leave the room untouched.
        f.Sql("CREATE TRIGGER fail_fatigue BEFORE UPDATE ON character_fatigue BEGIN SELECT RAISE(ABORT,'test failure'); END;");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(async () => await visits.MoveAsync(f.Session.CharacterId, first, new(1, 0), default));
        Assert.Equal(first.RoomVersion, runs.Find(f.Session.CharacterId)!.RoomVersion);
        Assert.Equal(1, (await fatigue.BalanceOfAsync(f.Session.CharacterId, default)).Used);
    }

    private static IDungeonVisitDomainService Bound(LearningFixture f, DungeonRunDomainService runs, DungeonLayout layout) =>
        new TransactionalDungeonVisitDomainService(Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(new RecordingLayouts(layout), runs,
            new ImmediateSelection(), new RecordingCellFactory(), new MaximumDifficulty(), new DungeonDifficultyRuleService(),
            f.Services.GetRequiredService<ICharacterFatigueDomainService>()), f.Scope,
            f.Services.GetRequiredService<TransactionalExecutionGate>());

    private sealed class TestFatigue : Dfo.Server.Abstractions.Characters.ICharacterFatigueDomainService
    {
        public Dictionary<CharacterId, int> Spent { get; } = [];
        public ValueTask<Dfo.Server.Abstractions.Characters.CharacterFatigueBalance> BalanceOfAsync(CharacterId id, CancellationToken token) => ValueTask.FromResult(new Dfo.Server.Abstractions.Characters.CharacterFatigueBalance(188, 0));
        public ValueTask SpendAsync(CharacterId id, ushort points, CancellationToken token)
        {
            Spent[id] = Spent.GetValueOrDefault(id) + points;
            return ValueTask.CompletedTask;
        }
        public ValueTask RefundAsync(CharacterId id, ushort points, CancellationToken token) => ValueTask.CompletedTask;
    }
    private static readonly DungeonLayout Layout = new(
        DungeonId: 10000,
        MazeIndex: 0,
        Width: 1,
        Height: 1,
        StartCell: new MazeCell(0, 0),
        BossCell: new MazeCell(0, 0),
        StartRoomMapId: 61000,
        Seed: 123,
        Cells: new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(61000)});

    private static readonly DungeonLayout TwoRoomLayout = new(
        DungeonId: 10001,
        MazeIndex: 0,
        Width: 2,
        Height: 1,
        StartCell: new MazeCell(0, 0),
        BossCell: new MazeCell(1, 0),
        StartRoomMapId: 61000,
        Seed: 456,
        Cells: new Dictionary<MazeCell, DungeonCellDefinition> {
            [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(61000),
            [new MazeCell(1, 0)] = DungeonCellDefinition.Fixed(61001),
        });

    [Fact]
    public async Task Locked_single_player_difficulty_does_not_create_a_run_or_room()
    {
        var runs = new DungeonRunDomainService();
        var cells = new RecordingCellFactory();
        var layouts = new RecordingLayouts();
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(layouts, runs, new ImmediateSelection(), cells,
            new MaximumDifficulty(DungeonDifficulty.Normal), new DungeonDifficultyRuleService(), new TestFatigue());
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
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(layouts, runs, new ImmediateSelection(), cells,
            new MaximumDifficulty(DungeonDifficulty.Adventure), new DungeonDifficultyRuleService(), new TestFatigue());
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
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(
            layouts,
            new DungeonRunDomainService(),
            new ImmediateSelection(),
            new RecordingCellFactory(), new MaximumDifficulty(), new DungeonDifficultyRuleService(), new TestFatigue());

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
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(
            new RecordingLayouts(),
            runs,
            new ImmediateSelection(),
            new ThrowingCellFactory(), new MaximumDifficulty(), new DungeonDifficultyRuleService(), new TestFatigue());

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
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(
            new RecordingLayouts(),
            runs,
            new ImmediateSelection(),
            new ThrowingCellFactory(), new MaximumDifficulty(), new DungeonDifficultyRuleService(), new TestFatigue());

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
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(
            new RecordingLayouts(),
            runs,
            new ImmediateSelection(),
            factory, new MaximumDifficulty(), new DungeonDifficultyRuleService(), new TestFatigue());

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
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(
            new RecordingLayouts(), runs, new ImmediateSelection(), factory, new MaximumDifficulty(), new DungeonDifficultyRuleService(), new TestFatigue());

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
        var visits = Dfo.Server.Application.Tests.Support.DungeonVisitTestFactory.Create(
            new RecordingLayouts(),
            runs,
            new ImmediateSelection(),
            factory, new MaximumDifficulty(), new DungeonDifficultyRuleService(), new TestFatigue());
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

    private sealed class RecordingLayouts(DungeonLayout? layout = null) : IDungeonStructureRuleService
    {
        public ValueTask<DungeonDefinition?> GetDefinitionAsync(int dungeonId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<DungeonDefinition?>(new(dungeonId, "test", 1, (layout ?? Layout).ConsumesFatigue, []));
        public ValueTask<DungeonMapSelection> SelectMapAsync(DungeonLayout value, MazeCell? from, MazeCell target, CancellationToken cancellationToken) =>
            ValueTask.FromResult(DungeonMapSelection.Selected(value.Cells[target].MapCandidates[0]));
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
        public bool DefersSelectionState(byte characterLevel, bool tutorialCompleted = false) => false;
    }
    private sealed class MaximumDifficulty(DungeonDifficulty maximum = DungeonDifficulty.Tier4, bool storageFailure = false) : Dfo.Server.Abstractions.Characters.ICharacterDungeonDifficultyDomainService
    {
        public ValueTask<DungeonDifficulty> GetMaximumAsync(CharacterId characterId, int dungeonId, CancellationToken token) => storageFailure
            ? throw new InvalidDataException("unexpected difficulty storage failure") : ValueTask.FromResult(maximum);
        public ValueTask<IReadOnlyList<Dfo.Server.Abstractions.Characters.CharacterDungeonDifficulty>> GetRecordedAsync(CharacterId characterId, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<DungeonDifficulty> UnlockAsync(CharacterId characterId, int dungeonId, DungeonDifficulty difficulty, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<Dfo.Server.Abstractions.Characters.CharacterDungeonDifficulty?> UnlockAfterClearAsync(CharacterId characterId, int dungeonId, DungeonDifficulty cleared, CancellationToken token) => throw new NotSupportedException();
    }
}
