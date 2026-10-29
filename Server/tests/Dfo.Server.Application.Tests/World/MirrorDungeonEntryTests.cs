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
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class MirrorDungeonEntryTests
{
    [SkippableTheory]
    [InlineData(4)]
    [InlineData(17)]
    [InlineData(23)]
    [InlineData(60)]
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
    [InlineData("late-session")]
    public async Task Refused_mirror_entry_does_not_create_a_run_or_reserve_daily_entry(string boundary)
    {
        await TestGameplay.RunAsync(async () =>
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
            Assert.Equal(0, quota.Reservations);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Client_pair_and_frozen_mirror_identity_reach_shared_entry_and_only_success_spends_quota(bool visitRefused)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = new LearningFixture();
            await fixture.SeedAsync();
            var (service, visits, quota) = Create(fixture);
            visits.Refused = visitRefused;
            await using var transaction = fixture.Scope.Begin();
            var result = await service.EnterAsync(Context(), default);
            Assert.Equal(1, visits.Calls);
            Assert.Equal(32, visits.Layout!.DungeonId);
            Assert.Equal(new MirrorDungeonRunIdentity(8000, 31, 14299), Assert.IsType<DungeonRunMode.Mirror>(visits.Layout.Mode).Plan);
            Assert.Equal((byte)85, visits.Layout.FixedMonsterLevel);
            Assert.Equal((byte)85, visits.Layout.BasisLevel);
            Assert.Equal(DungeonDifficulty.Tier3, visits.Facts!.Difficulty);
            Assert.Equal(DungeonEntryIntentKind.Interactive, visits.Facts.Intent.Kind);
            Assert.Equal(visitRefused, result.Entry is null);
            await transaction.CommitAsync(default);
            Assert.Equal(visitRefused ? 0 : 1, quota.Reservations);
        });
    }

    [Fact]
    public async Task Quota_write_inconsistency_is_an_exception_so_entry_transaction_cannot_commit_partial_payment()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = new LearningFixture();
            await fixture.SeedAsync();
            var (service, _, quota) = Create(fixture);
            quota.FailReservation = true;
            await using var transaction = fixture.Scope.Begin();
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(async () => await service.EnterAsync(Context(), default));
        });
    }

    [Theory]
    [InlineData(new byte[] { 86 }, 86)]
    [InlineData(new byte[] { 55, 86 }, 86)]
    [InlineData(new byte[] { 86, 55 }, 86)]
    [InlineData(new byte[] { 55, 60, 70, 80 }, 80)]
    public void Run_level_uses_the_highest_validated_participant(byte[] levels, byte expected)
    {
        var rules = new MirrorDungeonRuleService(null!, null!, null!);
        Assert.Equal(expected, rules.ResolveRunLevel(levels));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0, 86 })]
    [InlineData(new byte[] { 55, 60, 70, 80, 86 })]
    public void Invalid_participant_levels_cannot_be_frozen(byte[] levels) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MirrorDungeonRuleService(null!, null!, null!).ResolveRunLevel(levels));

    [Fact]
    public async Task Entry_freezes_party_level_before_visit_and_leaves_content_layout_unchanged()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = new LearningFixture();
            await fixture.SeedAsync();
            var scale = fixture.Services.GetRequiredService<IExperienceRuleService>().CharacterScale();
            fixture.Sql($"UPDATE characters SET level=55,exp={scale.ExperienceForLevel(55)} WHERE character_id=71; UPDATE characters SET level=86,exp={scale.ExperienceForLevel(86)} WHERE character_id=72;");
            var (service, visits, _) = Create(fixture);
            await using (var transaction = fixture.Scope.Begin())
            {
                Assert.NotNull((await service.EnterAsync(Context(), default)).Entry);
                await transaction.CommitAsync(default);
            }
            fixture.Sql($"UPDATE characters SET level=85,exp={scale.ExperienceForLevel(85)} WHERE character_id=72;");
            Assert.Equal((byte)85, (await fixture.Services.GetRequiredService<ICharacterDomainService>()
                .Get(new(72)).ProgressAsync(default))!.Level);
            Assert.Equal((byte)86, visits.Layout!.FixedMonsterLevel);
            Assert.Equal((byte)86, visits.Layout.BasisLevel);
            Assert.Null(Layout.FixedMonsterLevel);
            Assert.Equal((byte)1, Layout.BasisLevel);
        });
    }

    private static readonly DungeonRunParticipant[] Participants = [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)];
    private static readonly MirrorDungeonCatalog Catalog = new(8000, 31, 21, 2, 55, 10, 5,
        new(5, 3037, 10), [], [new(32, 14299)]) { RequiredQuestIds = [14395] };
    private static readonly DungeonLayout Layout = new(32, 0, 1, 1, new(0, 0), new(0, 0), 4403, 42,
        new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(4403) });
    private static DungeonEntryContext Context() => new(32, DungeonEntryFacts.None, 0, 0, Participants, 14299);

    private static (MirrorDungeonEntryDomainService Service, Visits Visits, Quota Quota) Create(LearningFixture fixture, string? boundary = null)
    {
        fixture.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(71,14395,1);");
        if (boundary != "member-quest") fixture.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(72,14395,1);");
        var sessions = fixture.Services.GetRequiredService<ISelectedSessionDomainService>();
        var other = fixture.EnterWorld(2, 72, "mirror-member");
        var positions = new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService());
        positions.Begin(fixture.Session, new(21, 2, 865, 208, 0), 100);
        positions.Begin(other, new(21, boundary == "position" ? (byte)1 : (byte)2, 865, 208, 0), 100);
        var visits = new Visits();
        var quota = new Quota(fixture);
        if (boundary == "quota")
            fixture.Sql($"INSERT INTO character_mirror_daily(character_id,day,entries,clears,daily_reward_claimed) VALUES(72,'{DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime):yyyy-MM-dd}',10,0,0);");
        var structures = new Structures(boundary == "late-session" ? () => sessions.Select(other with { ConnectionGeneration = other.ConnectionGeneration + 1 }) : null);
        var admission = new DungeonAdmissionDomainService(structures,
            fixture.Services.GetRequiredService<ICharacterDungeonDifficultyDomainService>(), new DungeonDifficultyRuleService(),
            fixture.Services.GetRequiredService<ICharacterFatigueDomainService>(), characters: fixture.Services.GetRequiredService<ICharacterDomainService>(),
            quests: fixture.Services.GetRequiredService<ICharacterQuestLogDomainService>(),
            quotas: fixture.Services.GetRequiredService<Dfo.Server.Abstractions.UsageQuotas.IUsageQuotaDomainService>(),
            events: fixture.Services.GetRequiredService<Dfo.Server.Abstractions.DomainEvents.IEventService>(), transactions: fixture.Scope,
            sessions: sessions, onlineCharacters: positions, runs: TestParties.Runs(fixture.Scope));
        visits.Admission = admission;
        return (new(new Rules(), structures, visits, fixture.Services.GetRequiredService<ICharacterDomainService>(), admission, TimeProvider.System,
            fixture.Services.GetRequiredService<Dfo.Server.Abstractions.Configuration.IDailyResetRuleService>()), visits, quota);
    }

    private sealed class Rules : IMirrorDungeonRuleService
    {
        public MirrorDungeonCatalog Catalog => MirrorDungeonEntryTests.Catalog;
        public byte ResolveRunLevel(IReadOnlyCollection<byte> levels) =>
            new MirrorDungeonRuleService(null!, null!, null!).ResolveRunLevel(levels);
        public ValueTask<MirrorDungeonEntryRule?> ResolveAsync(int dungeonId, int questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<MirrorDungeonEntryRule?>(dungeonId == 32 && questId == 14299 ? new(Catalog, DungeonDifficulty.Tier3) : null);
    }
    private sealed class Quota(LearningFixture fixture)
    {
        public int Reservations => fixture.Scalar("SELECT COALESCE(MAX(entries),0) FROM character_mirror_daily WHERE character_id=71");
        public bool FailReservation { set { if (value) fixture.Sql("CREATE TRIGGER fail_mirror_entry BEFORE INSERT ON character_mirror_daily WHEN NEW.character_id=72 BEGIN SELECT RAISE(ABORT,'second member quota failure'); END;"); } }
    }
    private sealed class Structures(Action? onResolve = null) : IDungeonStructureRuleService
    {
        public ValueTask<DungeonDefinition?> GetDefinitionAsync(int id, CancellationToken token) => ValueTask.FromResult<DungeonDefinition?>(new(id, "mirror-test", 55, true, []));
        public ValueTask<DungeonLayout?> ResolveAsync(int id, DungeonEntryFacts facts, int seed, CancellationToken token)
        {
            onResolve?.Invoke();
            return ValueTask.FromResult<DungeonLayout?>(Layout with { Seed = seed });
        }
        public ValueTask<DungeonMapSelection> SelectMapAsync(DungeonLayout layout, MazeCell? from, MazeCell target, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Visits : IDungeonVisitDomainService
    {
        public IDungeonAdmissionDomainService Admission { get; set; } = null!;
        public ValueTask<DungeonEntryAttempt> EnterGrantedPartyAsync(IReadOnlyList<DungeonRunParticipant> members, DungeonLayout layout,
            DungeonEntryFacts facts, IDungeonAdmissionReceipt receipt, CancellationToken ct) => Admission.BeginEntry(receipt, members, layout.DungeonId) is { } refusal
                ? ValueTask.FromResult(refusal) : TryEnterPreparedPartyAsync(members, layout, facts, ct);
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
