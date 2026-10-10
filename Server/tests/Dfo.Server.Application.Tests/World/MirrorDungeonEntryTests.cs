using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class MirrorDungeonEntryTests
{
    [SkippableFact]
    public async Task Test_rotation_maps_have_valid_pvf_missions_and_prepare_with_their_declared_difficulty()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();
        using var archive = new PvfArchiveProvider(new PvfContentOptions { ArchivePath = ContentArchive.Path });
        var rules = new MirrorDungeonRuleService(new PvfMirrorDungeonDefinitionSource(archive),
            content.GetRequiredService<IDungeonDefinitionSource>(), content.GetRequiredService<IQuestDefinitionSource>());
        var structures = new DungeonStructureRuleService(content.GetRequiredService<IDungeonDefinitionSource>(),
            content.GetRequiredService<IDungeonMapDefinitionSource>());
        foreach (var id in new[] { 17, 23, 60, 4 })
        {
            var candidate = rules.Catalog.Candidates.First(x => x.DungeonId == id);
            var rule = await rules.ResolveAsync(id, candidate.QuestId, default);
            Assert.NotNull(rule);
            var layout = await structures.ResolveAsync(id, DungeonEntryFacts.None with { Difficulty = rule.Difficulty }, 42, default);
            Assert.NotNull(layout);
            Assert.Equal(id, layout.DungeonId);
            Assert.True(layout.StartRoomMapId > 0);
        }
    }

    [SkippableTheory]
    [InlineData(4)]
    [InlineData(17)]
    [InlineData(23)]
    public async Task Historical_mirror_maps_prepare_every_ordinary_start_and_boss_combination(int dungeonId)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();
        var definitions = content.GetRequiredService<IDungeonDefinitionSource>();
        var structures = new DungeonStructureRuleService(definitions,
            content.GetRequiredService<IDungeonMapDefinitionSource>());
        var definition = await definitions.GetAsync(dungeonId, default);
        Assert.NotNull(definition);
        var mazes = definition.Mazes.Where(maze => maze.QuestConnection is null).ToArray();
        Assert.NotEmpty(mazes);
        foreach (var maze in mazes)
        foreach (var start in maze.StartCandidates)
        foreach (var boss in maze.BossCandidates)
        {
            var layout = new DungeonLayout(dungeonId, maze.Index, maze.Width, maze.Height,
                start, boss, 0, 0, maze.Cells);
            Assert.True((await structures.PrepareLayoutAsync(layout, default)).StartRoomMapId > 0);
        }
    }

    [SkippableFact]
    public async Task Observed_A21_pair_resolves_declared_difficulty_and_actual_ordinary_maze_without_wrapper_generation()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();
        using var archive = new PvfArchiveProvider(new PvfContentOptions { ArchivePath = ContentArchive.Path });
        var rules = new MirrorDungeonRuleService(new PvfMirrorDungeonDefinitionSource(archive),
            content.GetRequiredService<IDungeonDefinitionSource>(), content.GetRequiredService<IQuestDefinitionSource>());
        var rule = await rules.ResolveAsync(32, 14299, default);
        Assert.NotNull(rule);
        Assert.Null(await rules.ResolveAsync(32, 13801, default));
        Assert.Null(await rules.ResolveAsync(8000, 14299, default));
        var structures = new DungeonStructureRuleService(content.GetRequiredService<IDungeonDefinitionSource>(),
            content.GetRequiredService<IDungeonMapDefinitionSource>());
        var layout = await structures.ResolveAsync(32, DungeonEntryFacts.None with { Difficulty = rule.Difficulty }, 42, default);
        Assert.NotNull(layout);
        Assert.Equal(32, layout.DungeonId);
        Assert.Equal(DungeonRunPurposeKind.Ordinary, layout.Purpose.Kind);
        Assert.NotEmpty(layout.Cells);
        Assert.True(layout.StartRoomMapId > 0);
        Assert.NotNull((await structures.SelectMapAsync(layout, new(0, 1), new(1, 1), default)).MapId);
        // This MAP supports the first arrival from the start, not a newly generated
        // arrival from the opposite side. Returning to its retained Cell needs neither.
        Assert.Equal(DungeonMapSelectionFailure.NoCompatibleMap,
            (await structures.SelectMapAsync(layout, new(2, 1), new(1, 1), default)).Failure);
        var definition = await structures.GetDefinitionAsync(32, default);
        Assert.Equal(definition!.SupportedDifficulties!.Max(), (byte)rule.Difficulty);
    }

    [Theory]
    [InlineData("pair")]
    [InlineData("intent")]
    [InlineData("position")]
    [InlineData("member-quest")]
    [InlineData("quota")]
    public async Task Refused_mirror_entry_does_not_create_a_run_or_reserve_daily_entry(string boundary)
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        var (service, visits, quota) = Create(fixture, boundary);
        var context = Context();
        if (boundary == "pair") context = context with { MirrorQuestId = 13801 };
        if (boundary == "intent") context = context with { Facts = context.Facts with { Intent = DungeonEntryIntent.Directed(32) } };
        await using var transaction = fixture.Scope.Begin();
        var result = await service.EnterAsync(context, default);
        Assert.Equal(boundary == "quota" ? DungeonEntryFailure.DailyEntryLimitReached
            : DungeonEntryFailure.MissingPrerequisite, result.Failure);
        Assert.Null(result.Entry);
        Assert.Equal(0, visits.Calls);
        Assert.Equal(0, quota.Reservations);
        await transaction.CommitAsync(default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Client_pair_and_frozen_mirror_identity_reach_shared_entry_and_only_success_spends_quota(bool visitRefused)
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        var (service, visits, quota) = Create(fixture);
        visits.Refused = visitRefused;
        await using var transaction = fixture.Scope.Begin();
        var result = await service.EnterAsync(Context(), default);
        Assert.Equal(1, visits.Calls);
        Assert.Equal(32, visits.Layout!.DungeonId);
        Assert.Equal(new MirrorDungeonRunIdentity(8000, 31, 14299), visits.Layout.Mirror);
        Assert.Equal(DungeonDifficulty.Tier3, visits.Facts!.Difficulty);
        Assert.Equal(DungeonEntryIntentKind.Interactive, visits.Facts.Intent.Kind);
        Assert.Equal(visitRefused ? 0 : 1, quota.Reservations);
        Assert.Equal(visitRefused, result.Entry is null);
        await transaction.CommitAsync(default);
    }

    [Fact]
    public async Task Quota_write_inconsistency_is_an_exception_so_entry_transaction_cannot_commit_partial_payment()
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        var (service, _, quota) = Create(fixture);
        quota.FailReservation = true;
        await using var transaction = fixture.Scope.Begin();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.EnterAsync(Context(), default));
    }

    [Fact]
    public async Task Test_rotation_wraps_only_after_committed_success_and_bypasses_entry_quota()
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        var (service, visits, quota) = Create(fixture, "quota", new TestRules(skipQuota: true));
        // A successful preparation whose transaction is rolled back retains the first map.
        await using (var transaction = fixture.Scope.Begin())
        {
            Assert.NotNull((await service.EnterAsync(Context(), default)).Entry);
            Assert.Equal(17, visits.Layout!.DungeonId);
        }
        // A rejected visit also retains the same map, even if its transaction commits.
        visits.Refused = true;
        await using (var transaction = fixture.Scope.Begin())
        {
            Assert.Null((await service.EnterAsync(Context(), default)).Entry);
            Assert.Equal(17, visits.Layout!.DungeonId);
            await transaction.CommitAsync(default);
        }
        visits.Refused = false;
        foreach (var id in new[] { 17, 23, 60, 4, 17 })
        {
            await using var transaction = fixture.Scope.Begin();
            Assert.NotNull((await service.EnterAsync(Context(), default)).Entry);
            Assert.Equal(id, visits.Layout!.DungeonId);
            Assert.Equal(15000 + id, visits.Layout.Mirror!.QuestId);
            await transaction.CommitAsync(default);
        }
        Assert.Equal(0, quota.Checks);
        Assert.Equal(0, quota.Reservations);
    }

    [Fact]
    public async Task Test_rotation_does_not_accept_invalid_client_pairs_and_can_keep_normal_quota()
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        var (service, visits, quota) = Create(fixture, rules: new TestRules(skipQuota: false));
        await using var transaction = fixture.Scope.Begin();
        Assert.Null((await service.EnterAsync(Context() with { MirrorQuestId = 1 }, default)).Entry);
        Assert.Equal(0, visits.Calls);
        Assert.NotNull((await service.EnterAsync(Context(), default)).Entry);
        Assert.Equal(17, visits.Layout!.DungeonId);
        Assert.Equal(1, quota.Checks);
        Assert.Equal(1, quota.Reservations);
        await transaction.CommitAsync(default);
    }

    [Fact]
    public async Task Test_rotation_is_independent_for_each_party_leader()
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        var (service, visits, _) = Create(fixture, rules: new TestRules(skipQuota: true));
        await using (var transaction = fixture.Scope.Begin())
        {
            Assert.NotNull((await service.EnterAsync(Context(), default)).Entry);
            await transaction.CommitAsync(default);
        }
        await using (var transaction = fixture.Scope.Begin())
        {
            var otherLeader = Participants.Select(x => x with { IsLeader = !x.IsLeader }).ToArray();
            Assert.NotNull((await service.EnterAsync(Context() with { Participants = otherLeader }, default)).Entry);
            Assert.Equal(17, visits.Layout!.DungeonId);
            await transaction.CommitAsync(default);
        }
        await using (var transaction = fixture.Scope.Begin())
        {
            Assert.NotNull((await service.EnterAsync(Context(), default)).Entry);
            Assert.Equal(23, visits.Layout!.DungeonId);
            await transaction.CommitAsync(default);
        }
    }

    private static readonly DungeonRunParticipant[] Participants = [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)];
    private static readonly MirrorDungeonCatalog Catalog = new(8000, 31, 21, 2, 55, 10, 5,
        new(5, 3037, 10), [], [new(32, 14299)]) { RequiredQuestIds = [14395] };
    private static readonly DungeonLayout Layout = new(32, 0, 1, 1, new(0, 0), new(0, 0), 4403, 42,
        new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(4403) });
    private static DungeonEntryContext Context() => new(32, DungeonEntryFacts.None, 0, 0, Participants, 14299);

    private static (MirrorDungeonEntryDomainService Service, Visits Visits, Quota Quota) Create(LearningFixture fixture, string? boundary = null,
        IMirrorDungeonRuleService? rules = null)
    {
        fixture.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(71,14395,1);");
        if (boundary != "member-quest") fixture.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(72,14395,1);");
        var sessions = fixture.Services.GetRequiredService<ISelectedSessionDomainService>();
        var other = new SelectedSessionIdentity(new(2), new(72), new("mirror-member"), 1, 72, 10011);
        sessions.Select(other);
        var positions = new CharacterPositionDomainService();
        positions.Place(fixture.Session, new(21, 2, 865, 208, 0));
        positions.Place(other, new(21, boundary == "position" ? (byte)1 : (byte)2, 865, 208, 0));
        var visits = new Visits();
        var quota = new Quota { Blocked = boundary == "quota" };
        return (new(rules ?? new Rules(), new Structures(), visits, sessions,
            fixture.Services.GetRequiredService<ICharacterDomainService>(), positions,
            fixture.Services.GetRequiredService<ICharacterQuestLogDomainService>(), new DungeonRunDomainService(fixture.Scope),
            quota, fixture.Scope), visits, quota);
    }

    private sealed class Rules : IMirrorDungeonRuleService
    {
        public MirrorDungeonCatalog Catalog => MirrorDungeonEntryTests.Catalog;
        public ValueTask<MirrorDungeonEntryRule?> ResolveAsync(int dungeonId, int questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<MirrorDungeonEntryRule?>(dungeonId == 32 && questId == 14299 ? new(Catalog, DungeonDifficulty.Tier3) : null);
    }
    private sealed class TestRules(bool skipQuota) : IMirrorDungeonRuleService
    {
        public MirrorDungeonEntryTestSettings EntryTestSettings { get; } = new(true, [17, 23, 60, 4], skipQuota);
        public MirrorDungeonCatalog Catalog { get; } = MirrorDungeonEntryTests.Catalog with
        { Candidates = [new(32, 14299), new(17, 15017), new(23, 15023), new(60, 15060), new(4, 15004)] };
        public ValueTask<MirrorDungeonEntryRule?> ResolveAsync(int dungeonId, int questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<MirrorDungeonEntryRule?>(Catalog.Candidates.Contains(new(dungeonId, questId))
                ? new(Catalog, DungeonDifficulty.Tier3) : null);
    }

    private sealed class Quota : IMirrorDungeonQuotaDomainService
    {
        public bool Blocked { get; init; }
        public bool FailReservation { get; set; }
        public int Reservations { get; private set; }
        public int Checks { get; private set; }
        public ValueTask<byte?> FindBlockedSlotAsync(IReadOnlyList<DungeonRunParticipant> participants, int limit, CancellationToken token) =>
            Check();
        private ValueTask<byte?> Check()
        { Checks++; return ValueTask.FromResult<byte?>(Blocked ? (byte)1 : null); }
        public ValueTask<byte?> TryReserveEntryAsync(IReadOnlyList<DungeonRunParticipant> participants, int limit, CancellationToken token)
        { Reservations++; return ValueTask.FromResult<byte?>(FailReservation ? (byte)1 : null); }
    }
    private sealed class Structures : IDungeonStructureRuleService
    {
        public ValueTask<DungeonDefinition?> GetDefinitionAsync(int id, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<DungeonLayout?> ResolveAsync(int id, DungeonEntryFacts facts, int seed, CancellationToken token) =>
            ValueTask.FromResult<DungeonLayout?>(Layout with { DungeonId = id, Seed = seed });
        public ValueTask<DungeonMapSelection> SelectMapAsync(DungeonLayout layout, MazeCell? from, MazeCell target, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Visits : IDungeonVisitDomainService
    {
        public bool Refused { get; set; }
        public int Calls { get; private set; }
        public DungeonLayout? Layout { get; private set; }
        public DungeonEntryFacts? Facts { get; private set; }
        public ValueTask<DungeonEntryAttempt> TryEnterPreparedPartyAsync(IReadOnlyList<DungeonRunParticipant> members,
            DungeonLayout layout, DungeonEntryFacts facts, CancellationToken token)
        {
            Calls++; Layout = layout; Facts = facts;
            return ValueTask.FromResult(Refused ? DungeonEntryAttempt.Refused(DungeonEntryFailure.InsufficientFatigue)
                : DungeonEntryAttempt.Accepted(new(layout, new Dictionary<CharacterId, DungeonRoomVisit>())));
        }
        public bool DefersSelectionState(byte level, bool tutorialCompleted = false) => false;
        public ValueTask<DungeonEntry?> EnterAsync(CharacterId id, int dungeonId, DungeonDifficulty difficulty, DungeonEntryFacts facts, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<DungeonRoomVisit?> MoveAsync(CharacterId id, DungeonRunState run, MazeCell target, CancellationToken token) => throw new NotSupportedException();
    }
}
