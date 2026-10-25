using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Settlement;
using Dfo.Server.Abstractions.Settlement;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game.QuestAcceptance;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Quests;

public sealed class MirrorAradQuestTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 7, 0, 0, TimeSpan.Zero);
    [Fact]
    public async Task Delayed_map_clear_updates_only_the_originating_mirror_run_and_never_recreates_removed_state()
    {
        var runtime = new Runtime { Current = Run() };
        await using var f = new LearningFixture(configure: services =>
        {
            services.AddSingleton<IDungeonRunDomainService>(runtime);
            services.AddSingleton<IQuestContentRuleService>(new Definitions { Value = Quest("clear map", null, 4403) });
            services.AddSingleton<IMirrorDungeonRuleService>(new MirrorRules());
            services.AddSingleton<IItemPlacementRuleService>(new Placements());
            services.AddSingleton<TimeProvider>(new Clock { Now = Now });
        });
        await f.SeedAsync();
        var service = f.Services.GetRequiredService<IMirrorAradQuestDomainService>();
        var store = f.Services.GetRequiredService<IMirrorAradQuestDomainStore>();
        var events = f.Services.GetRequiredService<IEventService>();
        var old = runtime.Current;
        await Write(f, async () => Assert.True((await service.AcceptAsync(new(1), new(71), 14299, default)).Accepted));
        var clear = new DungeonMapCleared(old.RunId, 32, 0, old.CurrentCell, 4403, old.RoomVersion, Now, [.. old.Participants]);
        await Write(f, async () => { await events.EnqueueAsync(clear); await events.EnqueueAsync(clear); });
        runtime.Current = Run();
        await Write(f, async () => Assert.True((await service.AcceptAsync(new(1), new(71), 14299, default)).Accepted));
        var dispatcher = f.Services.GetRequiredService<DomainEventDispatcher>();
        await dispatcher.DrainOnceAsync("quest.mirror-map-clear");
        var previous = await store.ReadAsync(new(71), old.RunId, default);
        Assert.Equal(0u, previous!.TriggerValue);
        Assert.Single(previous.ClearedCells);
        Assert.Equal(1u, (await store.ReadAsync(new(71), runtime.Current.RunId, default))!.TriggerValue);
        await Write(f, async () => await service.ClearRunAsync(new(71), old.RunId, default));
        await Write(f, async () => { await events.EnqueueAsync(clear); });
        await dispatcher.DrainOnceAsync("quest.mirror-map-clear");
        Assert.Null(await store.ReadAsync(new(71), old.RunId, default));
        Assert.Equal(1u, (await store.ReadAsync(new(71), runtime.Current.RunId, default))!.TriggerValue);
    }

    private static QuestDefinition Quest(string type = "condition under clear", int? subtype = 6, params int[] args) =>
        new(14299, "mirror", QuestGrade.Crack, null, null, null, null, null,
            [new(QuestRewardKind.CrackOfDimension, [])], type, "", null, null, null, null, null, null, null, [], [], [], [])
        { ObjectiveSubType = subtype, ObjectiveArguments = args.Length == 0 ? [32, -1] : args };

    private static DungeonRunState Run(bool cleared = false, DungeonRunId? id = null) => new(
        new DungeonLayout(32, 0, 1, 1, new(0, 0), new(0, 0), 4403, 42,
            new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(4403) })
            { Mirror = new(8000, 31, 14299) },
        new(0, 0), 4403, new HashSet<MazeCell> { new(0, 0) }, Now.AddMinutes(-20),
        RunId: id ?? DungeonRunId.New(), Participants: [new(new(1), new(71), 0, true)], ClearOpen: cleared,
        CurrentCellState: new(new(0, 0), 4403, 0, 1, [new(1, new(100, 85, 3), false, true, [])], []))
        { ClearedAt = cleared ? Now.AddSeconds(10) : null };

    [Fact]
    public async Task Five_objective_kinds_use_run_facts_and_distinct_map_identity()
    {
        var definitions = new Definitions();
        var rules = new MirrorAradQuestRuleService(definitions, new MirrorRules(), new Placements());
        var run = Run(true);
        var state = new MirrorQuestState(new(71), run.RunId, 14299, Now, 1);
        definitions.Value = Quest("hunt enemy", null, 32, -1, -3, 1, 2);
        var hunt = (await rules.ResolveAsync(32, 14299, default))!;
        Assert.Equal(MirrorQuestKind.Hunt, hunt.Kind);
        state = state with { TriggerValue = hunt.InitialTrigger };
        var death = new QuestEnemyDefeated(32, 0, 100, 1, RunId: run.RunId, Actor: new(new(0, 0), 1));
        state = rules.Apply(hunt, state, run, death);
        Assert.Equal(1u, state.TriggerValue);
        Assert.Equal(state, rules.Apply(hunt, state, run, death));
        Assert.Equal(state, rules.Apply(hunt, state, run, death with { RunId = DungeonRunId.New() }));

        definitions.Value = Quest(subtype: 4);
        var clear = (await rules.ResolveAsync(32, 14299, default))!;
        Assert.True(rules.CanFinish(clear, state, run));
        Assert.False(rules.CanFinish(clear, rules.Apply(clear, state, run, new QuestCharacterRevived(run.RunId, new(71))), run));

        definitions.Value = Quest("clear map", null, 32);
        var map = (await rules.ResolveAsync(32, 14299, default))!;
        Assert.False(rules.CanFinish(map, state, run)); // DGN 32 is not MAP 32.
        Assert.True(rules.CanFinish(map, rules.ApplyMapClear(map, state,
            new(run.RunId, 32, 0, new(0, 0), 32, run.RoomVersion, Now, [.. run.Participants])), run));

        definitions.Value = Quest(subtype: 0, args: [32, -1, 30]);
        var timed = (await rules.ResolveAsync(32, 14299, default))!;
        Assert.True(rules.CanFinish(timed, state, run)); // Accepted 10 seconds ago, entered 20 minutes ago.
        Assert.False(rules.CanFinish(timed, state with { AcceptedAt = Now.AddMinutes(-1) }, run));

        definitions.Value = Quest(subtype: 11, args: [32, -1, 2]);
        var locations = (await rules.ResolveAsync(32, 14299, default))!;
        Assert.False(rules.CanFinish(locations, state, run with { VisitedCells = new HashSet<MazeCell> { new(0, 0), new(1, 0) } }));
        var room = new DungeonMapCleared(run.RunId, 32, 0, new(0, 0), 4403, run.RoomVersion, Now, [.. run.Participants]);
        state = rules.ApplyMapClear(locations, state, room);
        Assert.Equal(state, rules.ApplyMapClear(locations, state, room));
        Assert.True(rules.CanFinish(locations, state, run with { ClearedCells = [new(0, 0), new(1, 0)] }));
    }

    [Fact]
    public async Task Acceptance_replay_is_refused_and_completion_remains_run_local_idempotent()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = new LearningFixture();
            await fixture.SeedAsync();
            var runtime = new Runtime { Current = Run() };
            var (service, store, _, effects, settlement) = Create(fixture, runtime);
            await Write(fixture, async () => Assert.True((await service.AcceptAsync(new(1), new(71), 14299, default)).Accepted));
            var first = await store.ReadAsync(new(71), runtime.Current.RunId, default);
            var handler = new QuestAcceptOperationDriver(new DefinitionDomain(), service,
                fixture.Services.GetRequiredService<ICharacterQuestLogDomainService>(),
                fixture.Services.GetRequiredService<ICharacterContainerDomainService>(),
                fixture.Services.GetRequiredService<IQuestProgressDomainService>(),
                fixture.Services.GetRequiredService<IItemDeliveryDomainService>());
            await Write(fixture, async () =>
            {
                var replay = await handler.HandleAsync(new(Guid.NewGuid(),typeof(object),typeof(object),CommandExecutionKind.Transactional),new(new(1), new(71), 14299), default);
                Assert.False(replay.Accepted);
                Assert.Equal(QuestAcceptRefusal.AlreadyActive, replay.Refusal);
                Assert.Null(replay.QuestProgress);
                Assert.Empty(replay.Items);
            });
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first),
                System.Text.Json.JsonSerializer.Serialize(await store.ReadAsync(new(71), runtime.Current.RunId, default)));
            await Write(fixture, async () => Assert.False((await Finish(settlement)).Finished));
            runtime.Current = runtime.Current with { ClearOpen = true, ClearedAt = Now.AddSeconds(10) };
            // No QuestDungeonCleared report is needed before finishing.
            await Write(fixture, async () => Assert.True((await Finish(settlement)).Finished));
            await Write(fixture, async () => Assert.True((await Finish(settlement)).Finished));
            await Write(fixture, async () => Assert.False(await service.GiveUpAsync(new(1), new(71), 14299, default)));
            await Write(fixture, async () => Assert.False((await service.AcceptAsync(new(1), new(71), 14299, default)).Accepted));
            Assert.Equal(1, effects.Calls);
            Assert.Equal(1, (await store.ReadQuotaAsync(new(71), new(2026, 10, 10), default)).Clears);
            Assert.Empty(await fixture.Services.GetRequiredService<ICharacterQuestLogDomainService>().LogOf(new(1), new(71)).CarriedAsync(default));
            Assert.Empty((await service.SnapshotAsync(new(1), new(71), default)).Quests);
        });
    }

    [Fact]
    public async Task Prepare_is_readonly_for_rewards_run_claim_quota_and_events()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = new LearningFixture();
            await fixture.SeedAsync();
            var runtime = new Runtime { Current = Run(true) };
            var (service, store, _, effects, settlement) = Create(fixture, runtime);
            await Write(fixture, async () => Assert.True((await service.AcceptAsync(new(1), new(71), 14299, default)).Accepted));
            var before = System.Text.Json.JsonSerializer.Serialize(await store.ReadAsync(new(71), runtime.Current.RunId, default));
            var events = fixture.Scalar("SELECT COUNT(*) FROM domain_events");
            await Write(fixture, async () =>
            {
                var plan = await TestEffectFootprints.Run(new Dfo.Server.Abstractions.Effects.EffectFootprintOwner(new(1), new(71), null), cap => service.PrepareCompletionAsync(new(1), new(71), 14299, ushort.MaxValue, 1, Now, null, cap, default));
                Assert.True(plan.Prepared);
                Assert.False(plan.AlreadyClaimed);
                Assert.NotEmpty(plan.Reward!.Items);
                Assert.Empty(plan.Reward.ConsumedItems ?? []);
                Assert.NotNull(plan.QuotaChange);
                Assert.True(plan.Completed!.RewardClaimed);
                Assert.False(plan.Expected!.RewardClaimed);
            });
            Assert.Equal(0, effects.Calls);
            Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM mirror_test_rewards"));
            Assert.Equal(0, (await store.ReadQuotaAsync(new(71), new(2026, 10, 10), default)).Clears);
            Assert.Equal(events, fixture.Scalar("SELECT COUNT(*) FROM domain_events"));
            Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(await store.ReadAsync(new(71), runtime.Current.RunId, default)));
            await Write(fixture, async () => Assert.True((await Finish(settlement)).MirrorCompletion));
            Assert.Equal(1, effects.Calls);
            Assert.Equal(1, (await store.ReadQuotaAsync(new(71), new(2026, 10, 10), default)).Clears);
        });
    }

    [Fact]
    public async Task Reward_refusal_and_rollback_leave_claim_and_daily_ledger_unchanged()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = new LearningFixture();
            await fixture.SeedAsync();
            var runtime = new Runtime { Current = Run(true) };
            var (service, store, preview, effects, settlement) = Create(fixture, runtime);
            await Write(fixture, async () => { await service.AcceptAsync(new(1), new(71), 14299, default); });
            preview.Rejection = InventoryEffectRejection.NoRoom;
            await Write(fixture, async () => Assert.False((await Finish(settlement)).Finished));
            Assert.False((await store.ReadAsync(new(71), runtime.Current.RunId, default))!.RewardClaimed);
            Assert.Equal(0, (await store.ReadQuotaAsync(new(71), new(2026, 10, 10), default)).Clears);
            preview.Rejection = InventoryEffectRejection.None;
            // Roll back after the reward, quest marker, quota and event have all been staged.
            await using (fixture.Scope.Begin()) Assert.True((await Finish(settlement)).Finished);
            Assert.False((await store.ReadAsync(new(71), runtime.Current.RunId, default))!.RewardClaimed);
            Assert.Equal(0, (await store.ReadQuotaAsync(new(71), new(2026, 10, 10), default)).Clears);
            Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM mirror_test_rewards"));
            effects.Fail = true;
            await using (fixture.Scope.Begin()) await Assert.ThrowsAsync<InvalidOperationException>(async () => await Finish(settlement));
            Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM mirror_test_rewards"));
        });
    }

    [Fact]
    public async Task Old_leave_cleanup_cannot_delete_new_run_and_stale_run_is_not_an_authority()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = new LearningFixture();
            await fixture.SeedAsync();
            var runtime = new Runtime { Current = Run() };
            var (service, store, _, _, _) = Create(fixture, runtime);
            var old = runtime.Current.RunId;
            await Write(fixture, async () => { await service.AcceptAsync(new(1), new(71), 14299, default); });
            runtime.Current = null;
            Assert.Empty((await service.SnapshotAsync(new(1), new(71), default)).Quests);
            await Write(fixture, async () => Assert.False((await service.AcceptAsync(new(1), new(71), 14299, default)).Accepted));
            runtime.Current = Run();
            await Write(fixture, async () => { await service.AcceptAsync(new(1), new(71), 14299, default); });
            await Write(fixture, async () => { await service.ClearRunAsync(new(71), old, default); await service.ClearRunAsync(new(71), old, default); });
            Assert.Null(await store.ReadAsync(new(71), old, default));
            Assert.NotNull(await store.ReadAsync(new(71), runtime.Current.RunId, default));
            var full = new QuestProgressSnapshot(Enumerable.Range(1, 30).Select(i => new CarriedQuest((short)i, i, 1)).ToArray());
            Assert.Equal(31, (await service.MergeAsync(new(71), full, default)).Quests.Count);
        });
    }

    [Fact]
    public async Task Committed_departure_is_consumed_by_the_registered_reliable_subscriber_without_touching_new_run()
    {
        var runtime = new Runtime { Current = Run() };
        await using var fixture = new LearningFixture(configure: services =>
        {
            services.AddSingleton<IDungeonRunDomainService>(runtime);
            services.AddSingleton<IQuestContentRuleService>(new Definitions());
            services.AddSingleton<IMirrorDungeonRuleService>(new MirrorRules());
            services.AddSingleton<IItemPlacementRuleService>(new Placements());
            services.AddSingleton<IInventoryEffectPreflightDomainService>(new Preview());
            services.AddSingleton<IApplyEffectsDomainService>(provider => new Effects(provider.GetRequiredService<SqliteDatabase>()));
            services.AddSingleton<TimeProvider>(new Clock { Now = Now });
        });
        await fixture.SeedAsync();
        var service = fixture.Services.GetRequiredService<IMirrorAradQuestDomainService>();
        var store = fixture.Services.GetRequiredService<IMirrorAradQuestDomainStore>();
        var lifecycle = fixture.Services.GetRequiredService<IDungeonLifecycleDomainService>();
        var dispatcher = fixture.Services.GetRequiredService<DomainEventDispatcher>();
        var old = runtime.Current.RunId;
        await Write(fixture, async () => Assert.True((await service.AcceptAsync(new(1), new(71), 14299, default)).Accepted));
        runtime.Current = null;
        // Disconnects publish outside a CMD transaction; duplicate facts remain safe.
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await lifecycle.RecordDepartureAsync(new(new(71), old, 32));
            await lifecycle.RecordDepartureAsync(new(new(71), old, 32));
        });
        runtime.Current = Run();
        await Write(fixture, async () => Assert.True((await service.AcceptAsync(new(1), new(71), 14299, default)).Accepted));
        Assert.NotNull(await store.ReadAsync(new(71), old, default));
        await dispatcher.DrainOnceAsync();
        await dispatcher.DrainOnceAsync();
        Assert.Null(await store.ReadAsync(new(71), old, default));
        Assert.NotNull(await store.ReadAsync(new(71), runtime.Current.RunId, default));
        Assert.Equal(2, fixture.Scalar("SELECT COUNT(*) FROM domain_event_deliveries WHERE subscriber_id='quest.mirror-leave' AND state=1"));
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM domain_event_deliveries WHERE subscriber_id='quest.mirror-leave' AND state<>1"));
    }

    [Fact]
    public async Task Read_only_query_uses_run_local_lifecycle_and_never_writes_virtual_slot_to_ordinary_log()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = new LearningFixture();
            await fixture.SeedAsync();
            var runtime = new Runtime { Current = Run(true) };
            var (service, store, _, _, _) = Create(fixture, runtime);
            await Write(fixture, async () => Assert.True((await service.AcceptAsync(new(1), new(71), 14299, default)).Accepted));
            var before = await store.ReadAsync(new(71), runtime.Current.RunId, default);
            var definitions = new Definitions();
            var query = new QuestQueryDomainService(definitions,
                fixture.Services.GetRequiredService<ICharacterQuestLogDomainService>(), null!,
                new QuestStateRuleService(definitions), null!,
                fixture.Services.GetRequiredService<Dfo.Server.Abstractions.Persistence.ICharacterTable>(),
                mirror: store, runs: runtime,
                mirrorRules: new MirrorAradQuestRuleService(definitions, new MirrorRules(), new Placements()));
            var status = await query.QueryAsync(new(1), new(71), 14299, default);
            Assert.Equal(QuestLifecycle.Accepted, status.Lifecycle);
            Assert.True(status.Accepted!.CanComplete);
            definitions.Value = Quest("hunt enemy", null, 32, -1, -3, 1, 1);
            await Write(fixture, async () => await store.WriteAsync(new(1), before! with { TriggerValue = 0 }, default));
            runtime.Current = runtime.Current with { ClearOpen = false, ClearedAt = null };
            var incompleteRun = await query.QueryAsync(new(1), new(71), 14299, default);
            Assert.Equal(0, incompleteRun.Accepted!.Quest.TriggerValue);
            Assert.False(incompleteRun.Accepted.CanComplete);
            Assert.False(Assert.Single(await query.ReadAcceptedAsync(new(1), new(71), default)).CanComplete);
            runtime.Current = runtime.Current with { ClearOpen = true, ClearedAt = Now.AddSeconds(10) };
            Assert.True((await query.QueryAsync(new(1), new(71), 14299, default)).Accepted!.CanComplete);
            await Write(fixture, async () => await store.WriteAsync(new(1), before!, default));
            Assert.Equal(short.MaxValue, Assert.Single(await query.ReadAcceptedAsync(new(1), new(71), default)).Quest.Slot);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before),
                System.Text.Json.JsonSerializer.Serialize(await store.ReadAsync(new(71), runtime.Current.RunId, default)));
            Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=71"));
            await Write(fixture, async () => await store.WriteAsync(new(1), before! with { RewardClaimed = true }, default));
            Assert.Equal(QuestLifecycle.Completed, (await query.QueryAsync(new(1), new(71), 14299, default)).Lifecycle);
            Assert.Empty(await query.ReadAcceptedAsync(new(1), new(71), default));
            await Write(fixture, async () => await store.WriteAsync(new(1), before! with { GivenUp = true }, default));
            var givenUp = await query.QueryAsync(new(1), new(71), 14299, default);
            Assert.Equal(QuestLifecycle.NotAccepted, givenUp.Lifecycle);
            Assert.False(givenUp.CanAccept);
            runtime.Current = Run();
            var newRun = await query.QueryAsync(new(1), new(71), 14299, default);
            Assert.Equal(QuestLifecycle.NotAccepted, newRun.Lifecycle);
            Assert.True(newRun.CanAccept);
        });
    }

    [Fact]
    public async Task Party_daily_entry_reservation_is_atomic_and_uses_server_day()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = new LearningFixture();
            await fixture.SeedAsync();
            var store = fixture.Services.GetRequiredService<IMirrorAradQuestDomainStore>();
            var clock = new Clock { Now = new(2026, 10, 10, 15, 59, 59, TimeSpan.Zero) };
            var quota = new MirrorDungeonQuotaDomainService(fixture.Services.GetRequiredService<IUsageQuotaDomainService>(), clock, fixture.Services.GetRequiredService<IEventService>());
            DungeonRunParticipant[] participants = [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)];
            await Write(fixture, async () => await store.WriteQuotaAsync(new(2), new(72), new(2026, 10, 10), new(Entries: 10), default));
            await Write(fixture, async () => Assert.Equal((byte)1, await quota.TryReserveEntryAsync(participants, 10, default)));
            Assert.Equal(0, (await store.ReadQuotaAsync(new(71), new(2026, 10, 10), default)).Entries);
            clock.Now = clock.Now.AddSeconds(1);
            await Write(fixture, async () => Assert.Null(await quota.TryReserveEntryAsync(participants, 10, default)));
            Assert.Equal(1, (await store.ReadQuotaAsync(new(71), new(2026, 10, 11), default)).Entries);
            Assert.Equal(1, (await store.ReadQuotaAsync(new(72), new(2026, 10, 11), default)).Entries);
        });
    }

    [Fact]
    public async Task Catalog_reward_band_and_daily_bonus_are_both_in_effect_plan()
    {
        var rules = new MirrorAradQuestRuleService(new Definitions(), new MirrorRules(), new Placements());
        var normal = await rules.RewardEffectsAsync(85, new(Clears: 3), default);
        Assert.Single(normal!.Value);
        var threshold = await rules.RewardEffectsAsync(85, new(Clears: 4), default);
        Assert.Equal([10100301, 3037], threshold!.Value.Cast<GrantBasicCharacterItemEffect>().Select(x => x.ItemId));
        Assert.Null(await rules.RewardEffectsAsync(85, new(Clears: 5), default));
    }

    [SkippableFact]
    public async Task Every_declared_A21_mirror_pair_has_a_supported_run_objective()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();
        var catalog = content.GetRequiredService<IMirrorDungeonDefinitionSource>().Load();
        var definitions = new QuestContentRuleService(content.GetRequiredService<IQuestDefinitionSource>(),
            content.GetRequiredService<IQuestParameterSource>());
        var rules = new MirrorAradQuestRuleService(definitions, new CatalogRules(catalog), new Placements());
        foreach (var candidate in catalog.Candidates)
            Assert.True(await rules.ResolveAsync(candidate.DungeonId, candidate.QuestId, default) is not null,
                $"Unsupported mirror pair DGN={candidate.DungeonId}, QST={candidate.QuestId}");
    }

    private sealed class CatalogRules(MirrorDungeonCatalog catalog) : IMirrorDungeonRuleService
    {
        public MirrorDungeonCatalog Catalog => catalog;
        public byte ResolveRunLevel(IReadOnlyCollection<byte> levels) => throw new NotSupportedException();
        public ValueTask<MirrorDungeonEntryRule?> ResolveAsync(int dungeonId, int questId, CancellationToken ct) => throw new NotSupportedException();
    }

    private static (MirrorAradQuestDomainService, IMirrorAradQuestDomainStore, Preview, Effects, IQuestCompletionSettlementDomainService) Create(LearningFixture fixture, Runtime runs)
    {
        fixture.Sql("CREATE TABLE mirror_test_rewards(value INTEGER NOT NULL)");
        var store = fixture.Services.GetRequiredService<IMirrorAradQuestDomainStore>();
        var preview = new Preview();
        var effects = new Effects(fixture.Services.GetRequiredService<SqliteDatabase>());
        var service = new MirrorAradQuestDomainService(store, new MirrorAradQuestRuleService(new Definitions(), new MirrorRules(), new Placements()), runs,
            fixture.Services.GetRequiredService<ICharacterQuestLogDomainService>(), fixture.Services.GetRequiredService<ICharacterDomainService>(),
            preview, new Clock { Now = Now }, fixture.Services.GetRequiredService<IEventService>(), fixture.Services.GetRequiredService<IUsageQuotaDomainService>());
        var settlement = new QuestCompletionSettlementService(null!, new DefinitionDomain(), effects,
            fixture.Services.GetRequiredService<IEventService>(), service);
        return (service, store, preview, effects, settlement);
    }
    private static ValueTask<QuestCompletionReceipt> Finish(IQuestCompletionSettlementDomainService service) =>
        TestEffectFootprints.Run(new Dfo.Server.Abstractions.Effects.EffectFootprintOwner(new(1), new(71), null), cap => service.FinishAsync(new(1), new(71), 14299, ushort.MaxValue, 1, Now, cap, default));
    private static async Task Write(LearningFixture fixture, Func<Task> action)
    { await TestGameplay.RunAsync(fixture.Services, async () => { await using var transaction = fixture.Scope.Begin(); await action(); await transaction.CommitAsync(default); }); }
    private sealed class DefinitionDomain : IQuestDefinitionDomainService
    {
        public QuestParameters RewardParameters() => throw new NotSupportedException();
        public QuestRewardResolution ResolveRewards(QuestDefinition definition, QuestRewardCandidate candidate, ushort selection, ushort count) => throw new NotSupportedException();
        public ValueTask<QuestDefinition?> FindAsync(int id, CancellationToken ct) => ValueTask.FromResult<QuestDefinition?>(id == 14299 ? Quest() : null);
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Runtime : IDungeonRunDomainService
    {
        public DungeonRunState? Current { get; set; }
        public DungeonRunState? Find(CharacterId id) => id.Value == 71 ? Current : null;
        public DungeonRunState? TryMove(CharacterId id, MazeCell target) => throw new NotSupportedException();
        public DungeonRunState? TrySettle(CharacterId id) => throw new NotSupportedException();
        public DungeonRunState? FixClearRewardCards(CharacterId id, ClearRewardCardFace free, ClearRewardCardFace paid, int? cost) => throw new NotSupportedException();
        public ClearRewardCardSlot? TryClaimClearRewardCard(CharacterId id, ClearRewardCardKind kind, int index) => throw new NotSupportedException();
        public bool End(CharacterId id) { Current = null; return true; }
    }
    private sealed class Definitions : IQuestContentRuleService
    {
        public QuestDefinition Value { get; set; } = Quest();
        public ValueTask<QuestDefinition?> FindAsync(int id, CancellationToken ct) => ValueTask.FromResult<QuestDefinition?>(id == 14299 ? Value : null);
        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([Value]);
        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(CancellationToken ct) => throw new NotSupportedException();
        public QuestParameters Parameters() => throw new NotSupportedException();
    }
    private sealed class MirrorRules : IMirrorDungeonRuleService
    {
        public byte ResolveRunLevel(IReadOnlyCollection<byte> levels) => throw new NotSupportedException();
        public MirrorDungeonCatalog Catalog { get; } = new(8000, 31, 21, 2, 55, 10, 5,
            new(5, 3037, 10), [new(55, 86, [new(10100301, 1)])], [new(32, 14299)]);
        public ValueTask<MirrorDungeonEntryRule?> ResolveAsync(int dungeonId, int questId, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Placements : IItemPlacementRuleService
    {
        public ValueTask<ItemPlacement?> PlacementForAsync(int id, CancellationToken ct) => ValueTask.FromResult<ItemPlacement?>(new(2, 10000, new(BusinessInventorySpace.Consumable, 0, 99)));
        public ValueTask<BusinessSlot?> WornSlotForAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ItemWorldClass> ClassifyAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<bool> IsEquipmentAsync(int id, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Preview : IInventoryEffectPreflightDomainService
    {
        public InventoryEffectRejection Rejection { get; set; }
        public ValueTask<InventoryEffectPreviewResult> PreviewAsync(EffectExecutionContext context, IReadOnlyList<DomainEffect> effects, CancellationToken ct) =>
            ValueTask.FromResult(new InventoryEffectPreviewResult(Rejection));
    }
    private sealed class Effects(SqliteDatabase database) : IApplyEffectsDomainService
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public async ValueTask<ApplyEffectsResult> ApplyAsync(EffectExecutionContext context, IReadOnlyList<DomainEffect> effects, CancellationToken ct)
        {
            Calls++;
            await database.WriteAsync(async (command, token) => { command.CommandText = "INSERT INTO mirror_test_rewards VALUES(1)"; await command.ExecuteNonQueryAsync(token); }, ct);
            if (Fail) throw new InvalidOperationException("reward fault");
            return new([]);
        }
    }
}
