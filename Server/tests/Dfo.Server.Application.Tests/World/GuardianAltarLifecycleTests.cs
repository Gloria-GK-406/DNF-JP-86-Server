using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class GuardianAltarLifecycleTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset At { get; set; } = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => At;
    }
    private static async Task<LearningFixture> Fixture(Clock clock)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var fixture = new LearningFixture(useArchive: true, configure: services =>
        {
            services.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path });
            services.AddSingleton<TimeProvider>(clock);
        });
        await fixture.SeedAsync();
        fixture.Sql("INSERT INTO character_stats(character_id,carried_gold) VALUES(71,1000000)");
        var id = new CharacterId(71);
        fixture.Services.GetRequiredService<ICharacterContainerDomainService>().Register(id,
            new PersistentItemContainer(ContainerId.ForCharacter(id, InventoryContainerType.Main), new(InventoryContainerType.Main, 0),
                fixture.Services.GetRequiredService<IItemDomainStore>(), fixture.Scope,
                fixture.Services.GetRequiredService<IItemPlacementRuleService>(), fixture.Services.GetRequiredService<IItemSlotRuleService>()));
        return fixture;
    }
    private static DungeonEntryContext Entry => new(3511, DungeonEntryFacts.None, 0, 0, [new(new(1), new(71), 0, true)]);

    [SkippableFact]
    public async Task Managed_disable_blocks_admission_and_old_selected_session_cannot_continue_or_mutate_progress()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var clock = new Clock(); await using var f = await Fixture(clock);
            var altar = f.Services.GetRequiredService<IGuardianAltarDomainService>();
            var events = f.Services.GetRequiredService<Dfo.Server.Abstractions.Events.IEventManagementDomainService>();
            await using (var tx = f.Scope.Begin()) { await altar.OpenAsync(f.Session, default); await tx.CommitAsync(default); }
            await using (var tx = f.Scope.Begin()) { await events.ChangeAsync(new(119, 0, "disable"), "test", clock.At, default); await tx.CommitAsync(default); }
            await using (var tx = f.Scope.Begin())
            { Assert.Null(await altar.OpenAsync(f.Session, default)); Assert.Null((await altar.EnterAsync(Entry, default)).Entry); }
            await using (var tx = f.Scope.Begin()) { await events.ChangeAsync(new(119, 1, "enable"), "test", clock.At, default); await tx.CommitAsync(default); }
            await using (var tx = f.Scope.Begin()) { Assert.NotNull((await altar.EnterAsync(Entry, default)).Entry); await tx.CommitAsync(default); }
            var run = f.Services.GetRequiredService<IDungeonRunDomainService>().Find(new CharacterId(71))!;
            var old = altar.Find(run.RunId)!;
            Assert.True(f.Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().ReleaseExact(f.Session));
            await using (var tx = f.Scope.Begin())
            {
                Assert.Null(await altar.AdvanceAsync(run.RunId, old.Generation, clock.At.AddDays(1), default));
                Assert.Null(await altar.SetSlotAsync(f.Session, 0, GuardianAltarShopKind.Unit, 2000, default));
                altar.PruneStaleSessions(); await tx.CommitAsync(default);
            }
            Assert.Empty(altar.Pending());
            Assert.Equal(3, (await altar.ReadProgressAsync(new(71), default)).FreeTickets);
        });
    }

    [SkippableFact]
    public async Task Energy_upgrades_summon_cost_cooldown_and_abandon_obey_the_run_transaction()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var clock = new Clock(); await using var f = await Fixture(clock);
            var altar = f.Services.GetRequiredService<IGuardianAltarDomainService>();
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            await using (var tx = f.Scope.Begin()) { await altar.OpenAsync(f.Session, default); await tx.CommitAsync(default); }
            await using (var tx = f.Scope.Begin()) { Assert.NotNull((await altar.EnterAsync(Entry, default)).Entry); await tx.CommitAsync(default); }
            var run = runs.Find(new CharacterId(71))!;
            runs.TryMarkParticipantReady(new(71), run.RunId, run.RoomVersion);
            await using (var tx = f.Scope.Begin()) { await altar.ReadyAsync(f.Session, run.RunId, default); await tx.CommitAsync(default); }
            var state = altar.Find(run.RunId)!; clock.At = clock.At.AddSeconds(10);
            await using (var tx = f.Scope.Begin()) { await altar.AdvanceAsync(run.RunId, state.Generation, clock.At, default); await tx.CommitAsync(default); }
            Assert.Equal(10000, altar.Find(run.RunId)!.Energy);
            await using (var tx = f.Scope.Begin()) Assert.NotNull(await altar.UpgradeEnergyAsync(f.Session, 0, default));
            Assert.Equal(0, altar.Find(run.RunId)!.EnergyLevel);
            await using (var tx = f.Scope.Begin()) { Assert.NotNull(await altar.UpgradeEnergyAsync(f.Session, 0, default)); await tx.CommitAsync(default); }
            Assert.Equal(1, altar.Find(run.RunId)!.EnergyLevel); Assert.Equal(2000, altar.Find(run.RunId)!.Energy);
            await using (var tx = f.Scope.Begin()) Assert.Null(await altar.UpgradeEnergyAsync(f.Session, 0, default));
            state = altar.Find(run.RunId)!; clock.At = clock.At.AddSeconds(3);
            await using (var tx = f.Scope.Begin()) { await altar.AdvanceAsync(run.RunId, state.Generation, clock.At, default); await tx.CommitAsync(default); }
            var unit = f.Services.GetRequiredService<IGuardianAltarRuleService>().SlotUnit(await altar.ReadProgressAsync(new(71), default), 0)!;
            var energy = altar.Find(run.RunId)!.Energy; var actors = runs.Find(run.RunId)!.CurrentCellState.Actors.Count;
            await using (var tx = f.Scope.Begin()) Assert.Null(await altar.SummonAsync(f.Session, 0, unit.ResourceId + 100, 1, default));
            await using (var tx = f.Scope.Begin()) Assert.NotNull(await altar.SummonAsync(f.Session, 0, unit.ResourceId, 1, default));
            Assert.Equal(energy, altar.Find(run.RunId)!.Energy);
            await using (var tx = f.Scope.Begin()) { Assert.Empty((await altar.SummonAsync(f.Session, 0, unit.ResourceId, 1, default))!.Spawns); await tx.CommitAsync(default); }
            Assert.Equal(energy - unit.NeedPoint!.Value * 1000, altar.Find(run.RunId)!.Energy);
            Assert.Equal(actors, runs.Find(run.RunId)!.CurrentCellState.Actors.Count);
            Assert.True(altar.Find(run.RunId)!.UnitCooldowns[0] > clock.At);
            await using (var tx = f.Scope.Begin()) Assert.Null(await altar.SummonAsync(f.Session, 0, unit.ResourceId, 1, default));
            await using (var tx = f.Scope.Begin()) { Assert.Equal(GuardianAltarStatus.Abandoned, (await altar.AbandonAsync(f.Session, default))!.State.Status); await tx.CommitAsync(default); }
            await using (var tx = f.Scope.Begin()) Assert.Null(await altar.AdvanceAsync(run.RunId, state.Generation, clock.At.AddDays(1), default));
            Assert.Equal(3, (await altar.ReadProgressAsync(new(71), default)).FreeTickets);
            Assert.Equal(0, f.Scalar("SELECT count(*) FROM guardian_altar_settlements"));
        });
    }

    [SkippableFact]
    public async Task Loading_timeout_fails_without_reward_and_departure_cleans_pending_callbacks()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var clock = new Clock(); await using var f = await Fixture(clock);
            var altar = f.Services.GetRequiredService<IGuardianAltarDomainService>();
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            await using (var tx = f.Scope.Begin()) { await altar.OpenAsync(f.Session, default); await tx.CommitAsync(default); }
            await using (var tx = f.Scope.Begin()) { Assert.NotNull((await altar.EnterAsync(Entry, default)).Entry); await tx.CommitAsync(default); }
            var run = runs.Find(new CharacterId(71))!; var loading = altar.Find(run.RunId)!;
            clock.At = loading.Deadline!.Value;
            await using (var tx = f.Scope.Begin()) { Assert.Equal(GuardianAltarStatus.Lost, (await altar.AdvanceAsync(run.RunId, loading.Generation, clock.At, default))!.State.Status); await tx.CommitAsync(default); }
            Assert.Equal(clock.At.AddSeconds(50), altar.Find(run.RunId)!.Deadline);
            Assert.Equal(3, (await altar.ReadProgressAsync(new(71), default)).FreeTickets);
            Assert.Equal(0, f.Scalar("SELECT count(*) FROM guardian_altar_settlements"));
            Assert.True(await runs.EndAsync(new(71), run.RunId));
            await using (var tx = f.Scope.Begin()) { altar.PruneStaleSessions(); await tx.CommitAsync(default); }
            Assert.Empty(altar.Pending()); Assert.Null(altar.Find(run.RunId));
        });
    }

    [SkippableFact]
    public async Task All_36_ordinary_stage_difficulties_finish_with_independent_unlocks_and_72_stars()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var clock = new Clock(); await using var f = await Fixture(clock);
            var altar = f.Services.GetRequiredService<IGuardianAltarDomainService>();
            var store = f.Services.GetRequiredService<IGuardianAltarProgressDomainStore>();
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            await using (var tx = f.Scope.Begin()) { await altar.OpenAsync(f.Session, default); await tx.CommitAsync(default); }
            var old = (await store.ReadAsync(new(71), default))!;
            await using (var tx = f.Scope.Begin()) { await store.SaveAsync(new(71), old with { Revision = old.Revision + 1, ExtraTickets = 100 }, old.Revision, default); await tx.CommitAsync(default); }
            foreach (var difficulty in Enum.GetValues<GuardianAltarDifficulty>())
            foreach (var stage in f.Services.GetRequiredService<IGuardianAltarRuleService>().Catalog.Stages.Where(x => !x.Survival))
            {
                await using (var tx = f.Scope.Begin()) { Assert.NotNull(await altar.OpenAsync(f.Session, default)); await tx.CommitAsync(default); }
                var context = Entry with { DungeonId = stage.DungeonId, Facts = DungeonEntryFacts.None with { Difficulty = (Dfo.Server.Data.World.DungeonDifficulty)difficulty } };
                await using (var tx = f.Scope.Begin()) { Assert.NotNull((await altar.EnterAsync(context, default)).Entry); await tx.CommitAsync(default); }
                var run = runs.Find(new CharacterId(71))!; runs.TryMarkParticipantReady(new(71), run.RunId, run.RoomVersion);
                Assert.Equal((ushort)0, run.CurrentCellState.Actors[0].Sequence);
                await using (var tx = f.Scope.Begin()) { await altar.ReadyAsync(f.Session, run.RunId, default); await tx.CommitAsync(default); }
                var ticks = 0;
                while (altar.Find(run.RunId) is { Status: GuardianAltarStatus.Fighting } state)
                {
                    Assert.True(++ticks <= 3);
                    foreach (var enemy in runs.Find(run.RunId)!.CurrentCellState.Actors.Where(x => x.BlocksClear && !x.IsDead))
                    {
                        await using var tx = f.Scope.Begin();
                        Assert.True(await altar.ReportDeathAsync(f.Session, run.RunId, run.RoomVersion, enemy.Sequence, default)); await tx.CommitAsync(default);
                    }
                    clock.At = clock.At.AddMilliseconds(state.Admission.Map.Phases.Sum(x => x.DurationMilliseconds) + 1000);
                    await using (var tx = f.Scope.Begin()) { Assert.NotNull(await altar.AdvanceAsync(run.RunId, state.Generation, clock.At, default)); await tx.CommitAsync(default); }
                    var actors = runs.Find(run.RunId)!.CurrentCellState.Actors;
                    Assert.Equal(Enumerable.Range(0, actors.Count).Select(x => (ushort)x), actors.Select(x => x.Sequence));
                }
                Assert.Equal(GuardianAltarStatus.Won, altar.Find(run.RunId)!.Status);
                Assert.True(await runs.EndAsync(new(71), run.RunId));
            }
            var result = await altar.ReadProgressAsync(new(71), default);
            Assert.Equal(72, result.GameStars); Assert.Equal(67, result.ExtraTickets + result.FreeTickets);
            Assert.All(result.Stages.Values, x => Assert.Equal(2, x));
            Assert.Equal(36, f.Scalar("SELECT count(*) FROM guardian_altar_settlements"));
        });
    }

    [SkippableFact]
    public async Task Entry_and_progress_rollback_together_and_daily_refill_preserves_extra_tickets()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var clock = new Clock(); await using var f = await Fixture(clock);
            var altar = f.Services.GetRequiredService<IGuardianAltarDomainService>();
            var store = f.Services.GetRequiredService<IGuardianAltarProgressDomainStore>();
            await using (var tx = f.Scope.Begin()) { Assert.NotNull(await altar.OpenAsync(f.Session, default)); await tx.CommitAsync(default); }
            var before = await store.ReadAsync(new(71), default);
            await using (var tx = f.Scope.Begin())
                Assert.NotNull((await altar.EnterAsync(Entry, default)).Entry);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(await store.ReadAsync(new(71), default)));
            Assert.Null(f.Services.GetRequiredService<IDungeonRunDomainService>().Find(new CharacterId(71)));
            await using (var tx = f.Scope.Begin())
            {
                var updated = before! with { Revision = before!.Revision + 1, FreeTickets = 0, ExtraTickets = 2 };
                await store.SaveAsync(new(71), updated, before.Revision, default); await tx.CommitAsync(default);
            }
            clock.At = clock.At.AddDays(1);
            await using (var tx = f.Scope.Begin())
            {
                var refreshed = await altar.OpenAsync(f.Session, default);
                Assert.Equal(3, refreshed!.FreeTickets); Assert.Equal(2, refreshed.ExtraTickets);
                await tx.CommitAsync(default);
            }
        });
    }
    [SkippableFact]
    public async Task Real_stage_runs_all_phases_pause_fences_old_callbacks_and_clear_awards_once()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var clock = new Clock(); await using var f = await Fixture(clock);
            var altar = f.Services.GetRequiredService<IGuardianAltarDomainService>();
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            await using (var tx = f.Scope.Begin()) { await altar.OpenAsync(f.Session, default); await tx.CommitAsync(default); }
            await using (var tx = f.Scope.Begin())
            { Assert.NotNull((await altar.EnterAsync(Entry, default)).Entry); await tx.CommitAsync(default); }
            var run = runs.Find(new CharacterId(71))!;
            Assert.NotNull(run.Layout.Guardian); Assert.Null(run.Layout.Altar); Assert.False(run.CurrentCellState.IsCleared);
            runs.TryMarkParticipantReady(new(71), run.RunId, run.RoomVersion);
            await using (var tx = f.Scope.Begin()) { await altar.ReadyAsync(f.Session, run.RunId, default); await tx.CommitAsync(default); }
            var original = altar.Find(run.RunId)!;
            await using (var tx = f.Scope.Begin()) { await altar.PauseAsync(f.Session, true, default); await tx.CommitAsync(default); }
            clock.At = clock.At.AddHours(1);
            await using (var tx = f.Scope.Begin())
                Assert.Null(await altar.AdvanceAsync(run.RunId, original.Generation, clock.At, default));
            Assert.Equal(0, altar.Find(run.RunId)!.Energy);
            await using (var tx = f.Scope.Begin()) { await altar.PauseAsync(f.Session, false, default); await tx.CommitAsync(default); }
            var spawned = 0; var iterations = 0;
            while (altar.Find(run.RunId) is { Status: GuardianAltarStatus.Fighting } state)
            {
                Assert.True(++iterations < 300);
                foreach (var actor in runs.Find(run.RunId)!.CurrentCellState.Actors.Where(x => !x.IsDead && x.BlocksClear))
                {
                    await using var tx = f.Scope.Begin();
                    Assert.True(await altar.ReportDeathAsync(f.Session, run.RunId, run.RoomVersion, actor.Sequence, default));
                    await tx.CommitAsync(default);
                }
                clock.At = state.Deadline!.Value;
                await using (var tx = f.Scope.Begin())
                {
                    var result = await altar.AdvanceAsync(run.RunId, state.Generation, clock.At, default);
                    Assert.NotNull(result); spawned += result.Spawns.Length; await tx.CommitAsync(default);
                }
            }
            Assert.True(spawned > 10); Assert.True(iterations >= 145);
            Assert.Equal(GuardianAltarStatus.Won, altar.Find(run.RunId)!.Status);
            var progress = await altar.ReadProgressAsync(new(71), default);
            Assert.Equal(3, progress.GameStars); Assert.Equal(2, progress.FreeTickets);
            Assert.Equal(0, progress.Stages[3511]); Assert.Equal(-1, progress.Stages[3512]);
            Assert.Equal(1, f.Scalar("SELECT count(*) FROM guardian_altar_settlements"));
            await using (var tx = f.Scope.Begin()) Assert.Null(await altar.AdvanceAsync(run.RunId, original.Generation, clock.At.AddDays(1), default));
            Assert.Equal(progress.Revision, (await altar.ReadProgressAsync(new(71), default)).Revision);
        });
    }
    [SkippableFact]
    public async Task Center_death_is_failure_and_death_rollback_does_not_kill_or_reward()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var clock = new Clock(); await using var f = await Fixture(clock);
            var altar = f.Services.GetRequiredService<IGuardianAltarDomainService>();
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            await using (var tx = f.Scope.Begin()) { await altar.OpenAsync(f.Session, default); await tx.CommitAsync(default); }
            await using (var tx = f.Scope.Begin()) { Assert.NotNull((await altar.EnterAsync(Entry, default)).Entry); await tx.CommitAsync(default); }
            var run = runs.Find(new CharacterId(71))!; runs.TryMarkParticipantReady(new(71), run.RunId, run.RoomVersion);
            await using (var tx = f.Scope.Begin()) { await altar.ReadyAsync(f.Session, run.RunId, default); await tx.CommitAsync(default); }
            var center = run.CurrentCellState.Actors.Single(x => x.Actor.Code == 64597);
            Assert.Equal((ushort)0, center.Sequence);
            await using (var tx = f.Scope.Begin()) Assert.True(await altar.ReportDeathAsync(f.Session, run.RunId, run.RoomVersion, center.Sequence, default));
            Assert.False(runs.Find(run.RunId)!.CurrentCellState.FindActor(center.Sequence)!.IsDead);
            Assert.Equal(GuardianAltarStatus.Fighting, altar.Find(run.RunId)!.Status);
            await using (var tx = f.Scope.Begin()) { Assert.True(await altar.ReportDeathAsync(f.Session, run.RunId, run.RoomVersion, center.Sequence, default)); await tx.CommitAsync(default); }
            Assert.Equal(GuardianAltarStatus.Ending, altar.Find(run.RunId)!.Status);
            var state = altar.Find(run.RunId)!; clock.At = state.Deadline!.Value;
            await using (var tx = f.Scope.Begin())
            { Assert.Equal(GuardianAltarStatus.Lost, (await altar.AdvanceAsync(run.RunId, state.Generation, clock.At, default))!.State.Status); await tx.CommitAsync(default); }
            Assert.Equal(0, (await altar.ReadProgressAsync(new(71), default)).GameStars);
            Assert.Equal(3, (await altar.ReadProgressAsync(new(71), default)).FreeTickets);
            Assert.Equal(0, f.Scalar("SELECT count(*) FROM guardian_altar_settlements"));
        });
    }

    [SkippableTheory]
    [InlineData(0, 3)]
    [InlineData(100, 99)]
    public async Task Survival_retains_the_center_without_loading_again_and_consumes_one_reward_ticket_per_clear(int extraTickets, int expectedRounds)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var clock = new Clock(); await using var f = await Fixture(clock);
            var altar = f.Services.GetRequiredService<IGuardianAltarDomainService>();
            var rules = f.Services.GetRequiredService<IGuardianAltarRuleService>();
            var store = f.Services.GetRequiredService<IGuardianAltarProgressDomainStore>();
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            await using (var tx = f.Scope.Begin()) { await altar.OpenAsync(f.Session, default); await tx.CommitAsync(default); }
            var old = (await store.ReadAsync(new(71), default))!; var unlocked = old;
            foreach (var stage in rules.Catalog.Stages.Where(x => !x.Survival))
                unlocked = rules.RecordClear(unlocked, rules.Admission(unlocked, stage.DungeonId, GuardianAltarDifficulty.Easy)!);
            await using (var tx = f.Scope.Begin())
            { await store.SaveAsync(new(71), unlocked with { Revision = old.Revision + 1, ExtraTickets = extraTickets }, old.Revision, default); await tx.CommitAsync(default); }
            await using (var tx = f.Scope.Begin())
            { Assert.NotNull((await altar.EnterAsync(Entry with { DungeonId = 3523 }, default)).Entry); await tx.CommitAsync(default); }
            var initial = runs.Find(new CharacterId(71))!;
            var center = initial.CurrentCellState.Actors.Single(x => x.Actor.Code == 64597).Sequence;
            Assert.Equal((ushort)0, center);
            runs.TryMarkParticipantReady(new(71), initial.RunId, initial.RoomVersion);
            await using (var tx = f.Scope.Begin()) { await altar.ReadyAsync(f.Session, initial.RunId, default); await tx.CommitAsync(default); }
            var iterations = 0;
            while (altar.Find(initial.RunId) is { Status: not GuardianAltarStatus.Won } state)
            {
                Assert.True(++iterations < 400);
                clock.At = state.Status == GuardianAltarStatus.BetweenRounds ? state.Deadline!.Value
                    : state.At.AddMilliseconds(state.Admission.Map.Phases.Sum(x => x.DurationMilliseconds) + 1000);
                await using (var tx = f.Scope.Begin())
                {
                    var result = await altar.AdvanceAsync(initial.RunId, state.Generation, clock.At, default);
                    Assert.NotNull(result);
                    if (state.Status == GuardianAltarStatus.BetweenRounds)
                    { Assert.True(result.RoundStarted); Assert.Equal(GuardianAltarStatus.Fighting, result.State.Status); }
                    await tx.CommitAsync(default);
                }
                var current = runs.Find(initial.RunId)!;
                Assert.Equal(Enumerable.Range(0, current.CurrentCellState.Actors.Count).Select(x => (ushort)x),
                    current.CurrentCellState.Actors.Select(x => x.Sequence));
                Assert.Equal(initial.RoomVersion, current.RoomVersion);
                Assert.False(current.CurrentCellState.FindActor(center)!.IsDead);
                foreach (var enemy in current.CurrentCellState.Actors.Where(x => x.BlocksClear && !x.IsDead))
                {
                    await using var tx = f.Scope.Begin();
                    Assert.True(await altar.ReportDeathAsync(f.Session, initial.RunId, current.RoomVersion, enemy.Sequence, default));
                    await tx.CommitAsync(default);
                }
            }
            var completed = await altar.ReadProgressAsync(new(71), default);
            Assert.Equal(expectedRounds, completed.BestSurvival);
            Assert.Equal(expectedRounds, f.Scalar("SELECT count(*) FROM guardian_altar_settlements"));
            Assert.Equal(3 + extraTickets - expectedRounds, completed.FreeTickets + completed.ExtraTickets);
            Assert.Equal(36, completed.GameStars);
        });
    }

    [SkippableFact]
    public async Task Shop_reset_and_achievement_are_atomic_replay_safe_and_survive_reopen()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var clock = new Clock(); await using var f = await Fixture(clock);
            var altar = f.Services.GetRequiredService<IGuardianAltarDomainService>();
            var rules = f.Services.GetRequiredService<IGuardianAltarRuleService>();
            var store = f.Services.GetRequiredService<IGuardianAltarProgressDomainStore>();
            await using (var tx = f.Scope.Begin()) { await altar.OpenAsync(f.Session, default); await tx.CommitAsync(default); }
            var old = (await store.ReadAsync(new(71), default))!; var unlocked = old;
            foreach (var stage in rules.Catalog.Stages.Take(4))
                unlocked = rules.RecordClear(unlocked, rules.Admission(unlocked, stage.DungeonId, GuardianAltarDifficulty.Easy)!);
            await using (var tx = f.Scope.Begin())
            { await store.SaveAsync(new(71), unlocked with { Revision = old.Revision + 1 }, old.Revision, default); await tx.CommitAsync(default); }
            await using (var tx = f.Scope.Begin()) Assert.NotNull(await altar.BuyAsync(f.Session, GuardianAltarShopKind.Unit, 1000, 2, 3, default));
            Assert.Equal(12, (await altar.ReadProgressAsync(new(71), default)).AvailableStars);
            await using (var tx = f.Scope.Begin()) { Assert.NotNull(await altar.BuyAsync(f.Session, GuardianAltarShopKind.Unit, 1000, 2, 3, default)); await tx.CommitAsync(default); }
            Assert.Equal(9, (await altar.ReadProgressAsync(new(71), default)).AvailableStars);
            var upgradedView = altar.DescribeProgress(await altar.ReadProgressAsync(new(71), default));
            var upgradedUnit = upgradedView.Purchases.Single(x => x.Kind == GuardianAltarShopKind.Unit && x.Id == 1000);
            Assert.Equal(3, upgradedUnit.Level);
            Assert.Equal(3, upgradedUnit.InvestedStars);
            Assert.False(upgradedView.Achievements.Single(x => x.Id == 1).Claimed);
            await using (var tx = f.Scope.Begin()) Assert.Null(await altar.BuyAsync(f.Session, GuardianAltarShopKind.Unit, 1000, 2, 3, default));
            await using (var tx = f.Scope.Begin())
            { Assert.NotNull(await altar.ExchangeSlotsAsync(f.Session, 0, 1, 1000, 2000, default)); await tx.CommitAsync(default); }
            await using (var tx = f.Scope.Begin()) Assert.Null(await altar.ExchangeSlotsAsync(f.Session, 0, 1, 1000, 2000, default));
            var character = f.Services.GetRequiredService<ICharacterDomainService>().Get(new(71));
            var gold = await character.GoldAsync(default);
            await using (var tx = f.Scope.Begin()) Assert.NotNull(await altar.ResetStarsAsync(f.Session, default));
            Assert.Equal(gold, await character.GoldAsync(default));
            await using (var tx = f.Scope.Begin()) { Assert.NotNull(await altar.ResetStarsAsync(f.Session, default)); await tx.CommitAsync(default); }
            Assert.Equal(gold - 500, await character.GoldAsync(default));
            Assert.Equal(12, (await altar.ReadProgressAsync(new(71), default)).AvailableStars);
            await using (var tx = f.Scope.Begin()) Assert.NotNull(await altar.ClaimAchievementAsync(f.Session, 1, default));
            Assert.DoesNotContain(1, (await altar.ReadProgressAsync(new(71), default)).ClaimedAchievements);
            await using (var tx = f.Scope.Begin()) { Assert.NotNull(await altar.ClaimAchievementAsync(f.Session, 1, default)); await tx.CommitAsync(default); }
            await using (var tx = f.Scope.Begin()) Assert.Null(await altar.ClaimAchievementAsync(f.Session, 1, default));
            await using var reopened = new LearningFixture(f.Path, useArchive: true, configure: services =>
            { services.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path }); services.AddSingleton<TimeProvider>(clock); });
            var persisted = await reopened.Services.GetRequiredService<IGuardianAltarProgressDomainStore>().ReadAsync(new(71), default);
            Assert.Contains(1, persisted!.ClaimedAchievements); Assert.Equal(1, persisted.StarResetCount);
            Assert.Equal(12, persisted.AvailableStars);
            var claimedView = altar.DescribeProgress(persisted);
            Assert.True(claimedView.Achievements.Single(x => x.Id == 1).Claimed);
            Assert.Null(rules.ClaimableAchievement(persisted, 1));
        });
    }
}
