using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Parties;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class AltarLifecycleTests
{
    [SkippableTheory]
    [InlineData(11006, 15, false)]
    [InlineData(11007, 7, false)]
    [InlineData(11007, 7, true)]
    public async Task Real_content_completes_every_round_and_awards_once(int dungeonId, int rounds, bool manualChoice)
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            var clock = new TestClock();
            await using var f = await Fixture(clock);
            var altars = f.Services.GetRequiredService<IAltarDungeonDomainService>();
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            var character = f.Services.GetRequiredService<ICharacterDomainService>().Get(new(71));
            DungeonEntryAttempt entered;
            await using (var tx = f.Scope.Begin())
            {
                entered = await altars.EnterAsync(Entry(dungeonId), default);
                Assert.True(entered.Entry is not null, $"{entered.Failure}: {entered.Detail}");
                await tx.CommitAsync(default);
            }
            var runId = entered.Entry!.Visits[new CharacterId(71)].Run.RunId;
            var iterations = 0;
            while (altars.Find(runId) is { Status: not AltarStatus.Awarded } state)
            {
                Assert.True(++iterations < 2000, $"Altar did not terminate: {state.Status} round={state.CompletedRounds} wave={state.NextWave}");
                var run = runs.Find(runId)!;
                if (state.Status is AltarStatus.AwaitingReady or AltarStatus.AwaitingRoom)
                {
                    if (state.Status == AltarStatus.AwaitingRoom)
                    {
                        var target = new MazeCell(checked((byte)(run.CurrentCell.X + 1)), run.CurrentCell.Y);
                        var moved = await f.Services.GetRequiredService<IDungeonVisitDomainService>().TryMoveAsync(
                            new(71), run, target, default);
                        Assert.NotNull(moved.Visit);
                        run = runs.Find(runId)!;
                    }
                    runs.TryMarkParticipantReady(new(71), runId, run.RoomVersion);
                    await using var tx = f.Scope.Begin();
                    Assert.NotNull(await altars.ReadyAsync(f.Session, runId, run.RoomVersion, default));
                    await tx.CommitAsync(default);
                    continue;
                }
                if (manualChoice && state.Status == AltarStatus.ChoosingDifficulty)
                {
                    await using var tx = f.Scope.Begin();
                    Assert.NotNull(await altars.SelectDifficultyAsync(f.Session, runId, run.RoomVersion, state.Generation, 2, default));
                    await tx.CommitAsync(default);
                    await using var duplicate = f.Scope.Begin();
                    Assert.Null(await altars.SelectDifficultyAsync(f.Session, runId, run.RoomVersion, state.Generation, 2, default));
                    continue;
                }
                // Existing death transaction tests cover rewards; this fixture exercises all real-content phases.
                foreach (var actor in run.CurrentCellState.Actors.Where(x => x.BlocksClear && !x.IsDead))
                {
                    var claim = runs.TryClaimActorDeath(new(71), actor.Sequence);
                    Assert.NotNull(claim);
                    Assert.NotNull(runs.CommitActorDeathChange(claim!));
                }
                if (state.NextDeadline is { } due) clock.At = due.AddMilliseconds(1);
                await using (var tx = f.Scope.Begin())
                {
                    Assert.NotNull(await altars.AdvanceAsync(runId, run.RoomVersion, state.Generation, clock.At, default));
                    await tx.CommitAsync(default);
                }
            }
            var complete = altars.Find(runId)!;
            Assert.False(complete.Failed); Assert.Equal(rounds, complete.CompletedRounds);
            Assert.Equal(AltarStatus.Awarded, complete.Status);
            var gold = await character.GoldAsync(default);
            var exp = await character.ProgressAsync(default);
            await using (var tx = f.Scope.Begin())
            {
                Assert.Null(await altars.SettleBeforeExitAsync(runId, default));
                await tx.CommitAsync(default);
            }
            Assert.Equal(gold, await character.GoldAsync(default));
            Assert.Equal(exp, await character.ProgressAsync(default));
            Assert.Equal(2, (await altars.ReadQuotaAsync(new(71), default)).Single(x => x.DungeonId == dungeonId).Remaining);
        });
    }

    [SkippableTheory]
    [InlineData(11006)]
    [InlineData(11007)]
    public async Task Character_state_reads_current_altar_quota_instead_of_legacy_defaults(int dungeonId)
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            var clock = new TestClock();
            await using var f = await Fixture(clock);
            f.Sql("INSERT INTO character_condition(character_id,pc_room_state) VALUES(71,0); INSERT INTO character_pvp(character_id) VALUES(71); INSERT INTO character_settings(character_id) VALUES(71); INSERT INTO character_expert_job(character_id) VALUES(71)");
            var entries = f.Services.GetRequiredService<ICharacterEntryStateDomainService>();
            var initial = await entries.LoadAsync(new(1), new(71), default);
            Assert.Equal(3, Assert.Single(initial.Public.Extras.Dimensions, x => x.Key == dungeonId).Value2);
            f.Sql("INSERT INTO character_dimensions(character_id,sort_order,dim_key,val1,val2) VALUES(71,0,11006,7,3),(71,1,11007,8,3),(71,2,11006,99,99),(71,3,122,4,9)");
            var altars = f.Services.GetRequiredService<IAltarDungeonDomainService>();
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            for (var used = 1; used <= 3; used++)
            {
                await using (var tx = f.Scope.Begin())
                {
                    Assert.NotNull((await altars.EnterAsync(Entry(dungeonId), default)).Entry);
                    await tx.CommitAsync(default);
                }
                Assert.True(await runs.EndAsync(new(71), runs.Find(new CharacterId(71))!.RunId));
                var state = await entries.LoadAsync(new(1), new(71), default);
                Assert.Equal(3 - used, Assert.Single(state.Public.Extras.Dimensions, x => x.Key == dungeonId).Value2);
                Assert.Equal(dungeonId == 11006 ? 7 : 8, Assert.Single(state.Public.Extras.Dimensions, x => x.Key == dungeonId).Value1);
                Assert.Equal(3, Assert.Single(state.Public.Extras.Dimensions, x => x.Key == (dungeonId == 11006 ? 11007 : 11006)).Value2);
                Assert.Equal((3, 122L, 4, 9), Assert.Single(state.Public.Extras.Dimensions, x => x.Key == 122));
            }
            clock.At = clock.At.AddDays(1);
            var nextDay = await entries.LoadAsync(new(1), new(71), default);
            Assert.Equal(3, Assert.Single(nextDay.Public.Extras.Dimensions, x => x.Key == dungeonId).Value2);
        });
    }

    private static DungeonEntryContext Entry(int id) => new(id, DungeonEntryFacts.None with { Difficulty = DungeonDifficulty.Normal },
        0, 0, [new(new(1), new(71), 0, true)]);
    private static async Task<LearningFixture> Fixture(TestClock clock)
    {
        var f = new LearningFixture(useArchive: true, configure: services =>
        {
            services.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path });
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IPartyMemberStatusDomainService>(new StubPartyMemberStatusDomainService());
        });
        await f.SeedAsync();
        f.Sql("INSERT INTO character_stats(character_id,carried_gold) VALUES(71,0),(72,0)");
        foreach (var memberId in new CharacterId[] { new(71), new(72) })
            f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(memberId,
                new PersistentItemContainer(ContainerId.ForCharacter(memberId, InventoryContainerType.Main),
                    new(InventoryContainerType.Main, 0), f.Services.GetRequiredService<IItemDomainStore>(), f.Scope,
                    f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>()));
        f.Services.GetRequiredService<IIndependentDropRuleService>().Initialize();
        f.Services.GetRequiredService<IExtraDropRuleService>().Initialize();
        f.Services.GetRequiredService<IMonsterDeclaredDropRuleService>().Initialize();
        f.Services.GetRequiredService<IWorldDropRuleService>().Initialize();
        var character = f.Services.GetRequiredService<ICharacterDomainService>().Get(new(71));
        await using (var tx = f.Scope.Begin())
        { _ = await character.AddGoldAsync(1_000_000, default); await tx.CommitAsync(default); }
        Assert.Equal(1_000_000, await character.GoldAsync(default));
        return f;
    }

    [SkippableTheory]
    [InlineData(11006)]
    [InlineData(11007)]
    public async Task Entry_rollback_and_refusal_preserve_costs_and_quota_then_three_entries_persist_until_reset(int dungeonId)
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            var clock = new TestClock();
            await using var f = await Fixture(clock);
            var altars = f.Services.GetRequiredService<IAltarDungeonDomainService>();
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            var character = f.Services.GetRequiredService<ICharacterDomainService>().Get(new(71));
            if (dungeonId == 11006)
            {
                await using (var tx = f.Scope.Begin()) { await character.TrySpendGoldAsync(1_000_000, default); await tx.CommitAsync(default); }
                await using (var tx = f.Scope.Begin())
                    Assert.Equal(DungeonEntryFailure.InsufficientGold, (await altars.EnterAsync(Entry(dungeonId), default)).Failure);
                Assert.Equal(3, (await altars.ReadQuotaAsync(new(71), default)).Single(x => x.DungeonId == dungeonId).Remaining);
                await using (var tx = f.Scope.Begin()) { await character.AddGoldAsync(1_000_000, default); await tx.CommitAsync(default); }
            }
            await using (var tx = f.Scope.Begin()) Assert.NotNull((await altars.EnterAsync(Entry(dungeonId), default)).Entry);
            Assert.Null(runs.Find(new CharacterId(71)));
            Assert.Equal(1_000_000, await character.GoldAsync(default));
            for (var i = 0; i < 3; i++)
            {
                await using (var tx = f.Scope.Begin()) { Assert.NotNull((await altars.EnterAsync(Entry(dungeonId), default)).Entry); await tx.CommitAsync(default); }
                var run = runs.Find(new CharacterId(71))!;
                await using (var tx = f.Scope.Begin())
                {
                    // Loading and old-generation requests must not start any wave.
                    Assert.Null(await altars.ReadyAsync(f.Session, run.RunId, run.RoomVersion, default));
                    Assert.Null(await altars.AdvanceAsync(run.RunId, run.RoomVersion, -1, clock.At, default));
                    Assert.NotNull(await altars.SettleBeforeExitAsync(run.RunId, default));
                    await tx.CommitAsync(default);
                }
                await runs.EndAsync(new(71), run.RunId);
                await using (var tx = f.Scope.Begin())
                    Assert.Null(await altars.AdvanceAsync(run.RunId, run.RoomVersion, 0, clock.At.AddDays(1), default));
            }
            Assert.Equal(dungeonId == 11006 ? 902800 : 1_000_000, await character.GoldAsync(default));
            await using (var tx = f.Scope.Begin())
                Assert.Equal(DungeonEntryFailure.DailyEntryLimitReached, (await altars.EnterAsync(Entry(dungeonId), default)).Failure);
            await using (var reopened = new LearningFixture(f.Path, useArchive: true, configure: services =>
            {
                services.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path });
                services.AddSingleton<TimeProvider>(clock);
            }))
                Assert.Equal(0, (await reopened.Services.GetRequiredService<IAltarDungeonDomainService>().ReadQuotaAsync(new(71), default))
                    .Single(x => x.DungeonId == dungeonId).Remaining);
            clock.At = clock.At.AddDays(1);
            Assert.Equal(3, (await altars.ReadQuotaAsync(new(71), default)).Single(x => x.DungeonId == dungeonId).Remaining);
        });
    }

    [SkippableFact]
    public async Task Leader_preparation_waits_for_the_last_loaded_member_before_any_round_projection()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            var clock = new TestClock();
            await using var f = await Fixture(clock);
            var altars = f.Services.GetRequiredService<IAltarDungeonDomainService>();
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            MakeParty(f);
            await using (var tx = f.Scope.Begin())
            {
                Assert.NotNull((await altars.EnterAsync(Entry(11007) with { Participants =
                    [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)] }, default)).Entry);
                await tx.CommitAsync(default);
            }
            var run = runs.Find(new CharacterId(71))!;
            runs.TryMarkParticipantReady(new(71), run.RunId, run.RoomVersion);
            await using (var tx = f.Scope.Begin())
            {
                var pending = await altars.ReadyAsync(f.Session, run.RunId, run.RoomVersion, default);
                Assert.NotNull(pending); Assert.False(pending.RoundStarted); Assert.Empty(pending.Spawns);
                await tx.CommitAsync(default);
            }
            var state = altars.Find(run.RunId)!;
            clock.At = state.NextDeadline!.Value.AddMilliseconds(1);
            await using (var tx = f.Scope.Begin())
            {
                var pending = await altars.AdvanceAsync(run.RunId, run.RoomVersion, state.Generation, clock.At, default);
                Assert.NotNull(pending); Assert.False(pending.RoundStarted); Assert.Empty(pending.Spawns);
                await tx.CommitAsync(default);
            }
            runs.TryMarkParticipantReady(new(72), run.RunId, run.RoomVersion);
            state = altars.Find(run.RunId)!; clock.At = state.NextDeadline!.Value.AddMilliseconds(1);
            await using (var tx = f.Scope.Begin())
            {
                Assert.True((await altars.AdvanceAsync(run.RunId, run.RoomVersion, state.Generation, clock.At, default))!.RoundStarted);
                await tx.CommitAsync(default);
            }
        });
    }

    private static IPartyService MakeParty(LearningFixture f)
    {
        var parties = f.Services.GetRequiredService<IPartyService>();
        var scope = new PartyDirectoryScope(11, 1, 1);
        var leader = new PartyCharacterSnapshot(new(1), new(71), 71, scope);
        var member = new PartyCharacterSnapshot(new(2), new(72), 72, scope);
        Assert.True(parties.RequestInvite(leader, member, new(0, 0, 0, 0)).Succeeded);
        Assert.True(parties.AcceptInvite(member, leader).Succeeded);
        return parties;
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Member_departure_leaves_the_remaining_party_running_without_repaying_the_departed_member(bool disconnected)
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            var clock = new TestClock();
            await using var f = await Fixture(clock);
            var parties = MakeParty(f);
            var altars = f.Services.GetRequiredService<IAltarDungeonDomainService>();
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            await using (var tx = f.Scope.Begin())
            {
                Assert.NotNull((await altars.EnterAsync(Entry(11007) with { Participants =
                    [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)] }, default)).Entry);
                await tx.CommitAsync(default);
            }
            var run = runs.Find(new CharacterId(71))!;
            runs.TryMarkParticipantReady(new(71), run.RunId, run.RoomVersion);
            runs.TryMarkParticipantReady(new(72), run.RunId, run.RoomVersion);
            await using (var tx = f.Scope.Begin()) { await altars.ReadyAsync(f.Session, run.RunId, run.RoomVersion, default); await tx.CommitAsync(default); }
            await ReachRound(1);
            if (!disconnected)
                await using (var tx = f.Scope.Begin())
                {
                    var paid = await altars.SettleBeforeExitAsync(run.RunId, default, new(72));
                    Assert.Equal(new CharacterId(72), Assert.Single(paid!.Awards).Participant.CharacterId);
                    Assert.NotEqual(AltarStatus.Awarded, paid.State.Status);
                    await tx.CommitAsync(default);
                }
            var former = await f.Services.GetRequiredService<ICharacterDomainService>().Get(new(72)).ProgressAsync(default);
            await runs.EndAsync(new(72), run.RunId);
            Assert.True(parties.LeaveParty(new(72)).Succeeded);
            Assert.Single(runs.Find(run.RunId)!.Participants);
            await using (var tx = f.Scope.Begin()) Assert.Null(await altars.SettleBeforeExitAsync(run.RunId, default, new(72)));
            await ReachRound(2);
            Assert.Equal(former, await f.Services.GetRequiredService<ICharacterDomainService>().Get(new(72)).ProgressAsync(default));
            await using (var tx = f.Scope.Begin())
            {
                Assert.Equal(new CharacterId(71), Assert.Single((await altars.SettleBeforeExitAsync(run.RunId, default))!.Awards).Participant.CharacterId);
                await tx.CommitAsync(default);
            }

            async Task ReachRound(int target)
            {
                for (var i = 0; altars.Find(run.RunId)!.CompletedRounds < target; i++)
                {
                    Assert.True(i < 300);
                    foreach (var actor in runs.Find(run.RunId)!.CurrentCellState.Actors.Where(x => x.BlocksClear && !x.IsDead))
                        runs.CommitActorDeathChange(runs.TryClaimActorDeath(new(71), actor.Sequence)!);
                    var state = altars.Find(run.RunId)!; clock.At = state.NextDeadline!.Value.AddMilliseconds(1);
                    await using var tx = f.Scope.Begin();
                    await altars.AdvanceAsync(run.RunId, run.RoomVersion, state.Generation, clock.At, default);
                    await tx.CommitAsync(default);
                }
            }
        });
    }

    [SkippableFact]
    public async Task Abandon_after_a_completed_round_awards_progress_once_and_rollback_keeps_the_claim_open()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            var clock = new TestClock();
            await using var f = await Fixture(clock);
            var altars = f.Services.GetRequiredService<IAltarDungeonDomainService>();
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            await using (var tx = f.Scope.Begin()) { Assert.NotNull((await altars.EnterAsync(Entry(11007), default)).Entry); await tx.CommitAsync(default); }
            var run = runs.Find(new CharacterId(71))!;
            runs.TryMarkParticipantReady(new(71), run.RunId, run.RoomVersion);
            await using (var tx = f.Scope.Begin()) { await altars.ReadyAsync(f.Session, run.RunId, run.RoomVersion, default); await tx.CommitAsync(default); }
            for (var i = 0; altars.Find(run.RunId)!.CompletedRounds == 0; i++)
            {
                Assert.True(i < 200);
                foreach (var actor in runs.Find(run.RunId)!.CurrentCellState.Actors.Where(x => x.BlocksClear && !x.IsDead))
                    runs.CommitActorDeathChange(runs.TryClaimActorDeath(new(71), actor.Sequence)!);
                var state = altars.Find(run.RunId)!; clock.At = state.NextDeadline!.Value.AddMilliseconds(1);
                await using var tx = f.Scope.Begin();
                await altars.AdvanceAsync(run.RunId, run.RoomVersion, state.Generation, clock.At, default);
                await tx.CommitAsync(default);
            }
            var character = f.Services.GetRequiredService<ICharacterDomainService>().Get(new(71));
            var before = await character.ProgressAsync(default);
            await using (var tx = f.Scope.Begin()) Assert.NotEmpty((await altars.SettleBeforeExitAsync(run.RunId, default, new(71)))!.Awards);
            Assert.Equal(before, await character.ProgressAsync(default));
            await using (var tx = f.Scope.Begin())
            {
                var paid = await altars.SettleBeforeExitAsync(run.RunId, default, new(71));
                Assert.True(paid!.State.Failed); Assert.NotEmpty(paid.Awards);
                Assert.True(paid.Awards[0].Experience!.Breakdown.Base > 0);
                await tx.CommitAsync(default);
            }
            var after = await character.ProgressAsync(default);
            Assert.NotEqual(before, after);
            await using (var tx = f.Scope.Begin()) Assert.Null(await altars.SettleBeforeExitAsync(run.RunId, default, new(71)));
            await runs.EndAsync(new(71), run.RunId);
            await using (var tx = f.Scope.Begin()) Assert.Null(await altars.AdvanceAsync(run.RunId, run.RoomVersion, 0, clock.At.AddDays(1), default));
            Assert.Equal(after, await character.ProgressAsync(default));
        });
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset At { get; set; } = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => At;
    }
}
