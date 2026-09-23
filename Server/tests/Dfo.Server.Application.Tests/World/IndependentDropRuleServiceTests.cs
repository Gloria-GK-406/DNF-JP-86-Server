using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.World;

public sealed class IndependentDropRuleServiceTests
{
    [Fact]
    public void Production_registration_prewarms_without_resolving_character_runtime()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IIndependentDropTableSource>(new Source(Row()));
        new ApplicationModule().ConfigureServices(services,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        Assert.Equal(1, provider.GetRequiredService<IIndependentDropRuleService>().Initialize());
    }

    private static IndependentDropRow Row(int source = 0, int item = 100, int ordinal = 0,
        int min = 0, int max = 0, int mode = 0, ImmutableArray<IndependentDropChoice> choices = default) =>
        new(ordinal, [source, 17, item, 0, 1000000, 3500000, 1000000, 1000000,
            1, 3, 5, 8, 4, min, max, 1, mode], choices.IsDefault ? [] : choices);
    private static MonsterDropRequest Request(int party = 1, IndependentDropSourceType type = IndependentDropSourceType.Monster) =>
        new(17, new(DungeonRunId.New(), 144, 123, new(1, 2), 555, DungeonDifficulty.Adventure,
            101, 99, 0, new CharacterId(1), Enumerable.Range(1, party)
                .Select(i => new MonsterDropParticipant(new AccountId(i), new CharacterId(i))).ToImmutableArray())
            { SourceType = type });
    private sealed class Source(params IndependentDropRow[] rows) : IIndependentDropTableSource
    {
        public int Calls { get; private set; }
        public IndependentDropTable Load()
        {
            Calls++;
            return new([..rows], new[] {100, 101, 102}.ToImmutableDictionary(i => i,
                i => (RolledDrop?)new RolledDrop(i, 1, DropRarity.Common, DropItemKind.Stackable)));
        }
    }
    private static IndependentDropRuleService Service(params IndependentDropRow[] rows) =>
        new(new Source(rows), () => throw new InvalidOperationException("No mutable character facts in pure rule test"), NullLogger<IndependentDropRuleService>.Instance);

    [Theory]
    [InlineData(1, 1)] [InlineData(2, 3)] [InlineData(3, 4)] [InlineData(4, 4)]
    public void Whole_party_trials_run_once_and_are_capped(int party, int expected) =>
        Assert.Equal(expected, Service(Row()).Draw(Request(party), 20).Count);

    [Fact]
    public void Five_difficulty_columns_and_saturation_are_respected()
    {
        var service = Service(Row());
        var request = Request();
        for (var difficulty = 0; difficulty < 5; difficulty++)
            Assert.Equal(difficulty == 0 ? 0 : 1, service.Draw(request with { Context = request.Context with {
                Difficulty = (DungeonDifficulty)difficulty } }, 20).Count);
    }
    [Theory]
    [InlineData(9, 0)] [InlineData(10, 1)] [InlineData(20, 1)] [InlineData(21, 0)]
    public void Player_level_bounds_are_inclusive_and_not_monster_level(int level, int count) =>
        Assert.Equal(count, Service(Row(min:10, max:20)).Draw(Request(), level).Count);

    [Fact]
    public void Monster_and_APC_ids_have_separate_namespaces()
    {
        var service = Service(Row(), Row(source:1, item:101, ordinal:1));
        Assert.Equal(100, Assert.Single(service.Draw(Request(), 20)).ItemId);
        Assert.Equal(101, Assert.Single(service.Draw(Request(type:IndependentDropSourceType.APC), 20)).ItemId);
        Assert.Empty(service.Draw(Request() with { MonsterCode = 999 }, 20));
    }
    [Fact]
    public void List_uses_relative_weights_and_never_selects_zero_weight()
    {
        var service = Service(Row(item:0, mode:1, choices:[new(100, 0), new(101, 1), new(102, 3)]));
        var request = Request(4);
        var drops = Enumerable.Range(1, 1000).SelectMany(seed => service.Draw(request with {
            Context = request.Context with { Seed = seed } }, 20)).ToArray();
        Assert.DoesNotContain(drops, d => d.ItemId == 100);
        Assert.InRange(drops.Count(d => d.ItemId == 101), 900, 1100);
        Assert.Equal(service.Draw(request, 20), service.Draw(request, 20));
    }
    [Fact]
    public void Cache_is_built_once_and_rows_can_independently_both_drop()
    {
        var source = new Source(Row(), Row(item:101, ordinal:1));
        var service = new IndependentDropRuleService(source, () => throw new InvalidOperationException(), NullLogger<IndependentDropRuleService>.Instance);
        Assert.Equal(2, service.Initialize());
        Assert.Equal(new[] {100, 101}, service.Draw(Request(), 20).Select(d => d.ItemId));
        service.Initialize();
        Assert.Equal(1, source.Calls);
    }
    [Fact]
    public void Missing_item_and_zero_weight_pool_fail_at_initialization()
    {
        Assert.Throws<InvalidDataException>(() => Service(Row(item:999)).Initialize());
        Assert.Throws<InvalidDataException>(() => Service(Row(mode:1, choices:[new(100, 0)])).Initialize());
    }
    [Fact]
    public async Task Unmatched_source_needs_no_character_lookup_and_cancellation_is_observed()
    {
        var service = Service(Row());
        Assert.Empty(await service.DrawAsync(Request() with {MonsterCode = 999}, CancellationToken.None));
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.DrawAsync(Request(), new CancellationToken(true)));
    }
    [Fact]
    public async Task Character_level_is_read_once_per_generation_and_not_retained_in_static_cache()
    {
        var reads = 0;
        byte level = 20;
        var character = new Dfo.Server.Application.Tests.Support.CurrentCharacterStub
        {
            ProgressReader = () =>
            {
                reads++;
                return ValueTask.FromResult<Dfo.Server.Data.Characters.CharacterProgressionState?>(new(level, 0));
            },
        };
        var service = new IndependentDropRuleService(new Source(Row(min:10, max:20), Row(ordinal:1, min:10, max:20)),
            () => Dfo.Server.Application.Tests.Support.CurrentCharacterStub.Domain(character), NullLogger<IndependentDropRuleService>.Instance);
        Assert.Equal(2, (await service.DrawAsync(Request(), CancellationToken.None)).Count);
        Assert.Equal(1, reads);
        level = 21;
        Assert.Empty(await service.DrawAsync(Request(), CancellationToken.None));
        Assert.Equal(2, reads);
    }
    [Fact]
    public void Missing_metadata_keeps_rule_weights_and_logs_without_rerolling()
    {
        var source = new MissingMetadataSource();
        var logger = new CaptureLogger();
        var service = new IndependentDropRuleService(source, () => throw new InvalidOperationException(), logger);
        Assert.Equal(2, service.Initialize());
        Assert.Equal(2, logger.Warnings.Count);
        var request = Request();
        var drops = Enumerable.Range(1, 1000).SelectMany(seed => service.Draw(request with {
            Context = request.Context with { Seed = seed } }, 20)).ToArray();
        // First row is a missing direct item; second row still splits 50/50, no renormalization.
        Assert.InRange(drops.Length, 430, 570);
        Assert.All(drops, item => Assert.Equal(100, item.ItemId));
        Assert.True(logger.Warnings.Count > 1000);
        Assert.Equal(1, source.Calls);
    }
    private sealed class MissingMetadataSource : IIndependentDropTableSource
    {
        public int Calls { get; private set; }
        public IndependentDropTable Load()
        {
            Calls++;
            return new([Row(item:999), Row(item:0, ordinal:1, mode:1, choices:[new(999, 1), new(100, 1)])],
                new Dictionary<int, RolledDrop?> { [999] = null,
                    [100] = new(100, 1, DropRarity.Common, DropItemKind.Stackable) }.ToImmutableDictionary());
        }
    }
    private sealed class CaptureLogger : ILogger<IndependentDropRuleService>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (level == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }
    [SkippableFact]
    public void A21_entire_cache_prewarms_with_one_warning_and_preserves_all_rows()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var scope = ContentArchive.CreateContentProvider();
        var logger = new CaptureLogger();
        var service = new IndependentDropRuleService(scope.GetRequiredService<IIndependentDropTableSource>(),
            () => throw new InvalidOperationException(), logger);
        Assert.Equal(3046, service.Initialize());
        Assert.Contains("10150672", Assert.Single(logger.Warnings));
        service.Initialize();
        Assert.Single(logger.Warnings);
        // Real row 2801 can still be sampled without failing the generation occasion.
        for (var seed = 0; seed < 20; seed++)
        {
            var request = Request() with { MonsterCode = 65696 };
            service.Draw(request with { Context = request.Context with { Seed = seed } }, 20);
        }
        Assert.True(logger.Warnings.Count > 1);
    }
}
