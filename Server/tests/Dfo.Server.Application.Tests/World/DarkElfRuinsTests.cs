using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Tests.World;

public sealed class DarkElfRuinsTests
{
    [SkippableFact]
    public async Task Monster_death_integration_freezes_once_and_awards_no_kill_experience()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var clock = new Clock { Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.FromHours(8)) };
        await using var f = await Fixture(clock);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            var character = f.Services.GetRequiredService<ICharacterDomainService>().Get(Character);
            var before = await character.ProgressAsync(default);
            var cell = new MazeCell(0, 0);
            var layout = new DungeonLayout(5006, 0, 1, 1, cell, cell, 53009, 1,
                new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(53009) })
                { Mode = new DungeonRunMode.DarkElfRuins(new(new(2026, 10, 26), false, 1)), ConsumesFatigue = false, ExperienceWeight = 0 };
            DungeonRunState run;
            await using (var tx = f.Scope.Begin())
            {
                run = runs.PreparePartyEntry([new(new(1), Character, 0, true)], layout, DungeonDifficulty.Normal,
                    new(cell, 53009, 1, [new(1, new(56349, 85, 3), false, false, [])], []), DungeonRunId.New(), clock.Now).Run;
                await tx.CommitAsync(default);
            }
            var request = new MonsterDeathSettlementRequest(f.Session, [f.Session], run.RunId, run.RoomVersion, 1);
            var deaths = f.Services.GetRequiredService<IMonsterDeathDomainService>();
            for (var i = 0; i < 2; i++)
                await using (var tx = f.Scope.Begin())
                {
                    var result = await TestEffectFootprints.Run(request.Participants, cap => deaths.SettleAsync(request, cap, default));
                    Assert.Equal(i == 0 ? 1 : 0, result.Participants.Count);
                    await tx.CommitAsync(default);
                }
            Assert.Equal(before, await character.ProgressAsync(default));
            Assert.Single(runs.Find(Character)!.Kills);
            var store = f.Services.GetRequiredService<IDarkElfRuinsDomainStore>();
            Assert.NotNull(await store.RewardAsync(Character, run.RunId.Value, default));
            Assert.Equal(1, (await store.ReadAsync(Character, default)).Clears);
        });
    }
    [SkippableFact]
    public async Task The_real_guardian_dummy_is_a_level_scaled_combatant_that_blocks_its_own_room_clear()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var clock = new Clock { Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.FromHours(8)) };
        await using var f = await Fixture(clock);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var rules = f.Services.GetRequiredService<IDarkElfRuinsRuleService>();
            var entry = await rules.EntryAsync(5008, DayOfWeek.Monday, default);
            var maze = rules.ChooseMaze(entry, [new(License: 3, MonthlyEntries: 24)], 1);
            Assert.True(maze.Guardian);
            Assert.False(rules.ChooseMaze(entry, [new(License: 3, MonthlyEntries: 24, Guardians: 2)], 1).Guardian);
            var layout = await f.Services.GetRequiredService<IDungeonStructureRuleService>().ResolveAsync(5008,
                DungeonEntryFacts.None with { RequiredMazeIndex = maze.Index, Difficulty = DungeonDifficulty.Tier2 }, 1, default);
            layout = rules.PrepareLayout(layout!, 85, new(new(2026, 10, 26), true, 3));
            var actors = f.Services.GetRequiredService<IDungeonRoomActorSource>();
            var found = false;
            foreach (var (cell, declaration) in layout.Cells)
                foreach (var map in declaration.MapCandidates.Concat(declaration.BossMapCandidates).Distinct())
                {
                    if (!(await actors.GetAsync(map, default)).Any(x => rules.IsGuardian(x.Code))) continue;
                    var seed = await f.Services.GetRequiredService<IDungeonCellFactory>().CreateAsync(
                        new(DungeonRunId.New(), layout, cell, map, DungeonDifficulty.Tier2, clock.Now,
                            [new(new(1), Character, 0, true)]), default);
                    var guardian = Assert.Single(seed.Actors, x => rules.IsGuardian(x.Actor.Code));
                    Assert.True(guardian.Actor.Declaration!.IsDummy);
                    Assert.True(guardian.RequiredCombatant);
                    Assert.True(guardian.IsMonster);
                    Assert.True(guardian.BlocksClear);
                    Assert.Equal(85, guardian.Actor.Level);
                    Assert.False(new DungeonCellState(cell, map, 0, seed.FirstActorSequence, seed.Actors, []).IsCleared);
                    found = true;
                }
            Assert.True(found);
        });
    }
    private static readonly CharacterId Character = new(71);
    [SkippableFact]
    public async Task Alicia_portal_uses_only_its_declared_direction_after_clearing_the_source_and_grants_no_guardian_reward()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var clock = new Clock { Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.FromHours(8)) };
        await using var f = await Fixture(clock);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var structures = f.Services.GetRequiredService<IDungeonStructureRuleService>();
            var rules = f.Services.GetRequiredService<IDarkElfRuinsRuleService>();
            var layout = await structures.ResolveAsync(5008, DungeonEntryFacts.None with { RequiredMazeIndex = 1, Difficulty = (DungeonDifficulty)2 }, 123, default);
            layout = rules.PrepareLayout(layout!, 85, new(new(2026, 10, 26), true, 3));
            var warp = Assert.Single(layout.Warps);
            Assert.Equal(layout.StartCell, warp.Key);
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            await using (var tx = f.Scope.Begin())
            {
                runs.PreparePartyEntry([new(new(1), Character, 0, true)], layout, (DungeonDifficulty)2,
                    new(warp.Key, layout.StartRoomMapId, 1, [new(1, new(56349, 85, 0), false, false, [])], []), DungeonRunId.New(), clock.Now);
                await tx.CommitAsync(default);
            }
            var visits = f.Services.GetRequiredService<IDungeonVisitDomainService>();
            await using (var tx = f.Scope.Begin())
                Assert.Null((await visits.TryMoveAsync(Character, runs.Find(Character)!, warp.Value, default)).Visit);
            var death = runs.TryClaimActorDeath(Character, 1)!;
            Assert.NotNull(runs.CommitActorDeath(death));
            await using (var tx = f.Scope.Begin())
            {
                Assert.NotNull((await visits.TryMoveAsync(Character, runs.Find(Character)!, warp.Value, default)).Visit);
                await tx.CommitAsync(default);
            }
            Assert.Equal(warp.Value, runs.Find(Character)!.CurrentCell);
            Assert.False(layout.CanTraverse(warp.Value, warp.Key));
            var reward = rules.FreezeReward(await rules.EntryAsync(5008, DayOfWeek.Monday, default), false, 0, 1);
            Assert.False(reward.GuardianDefeated); Assert.Empty(reward.Guardian);
            Assert.Equal(2, reward.Daily.Count);
        });
    }

    private static DungeonEntryContext Entry(int dungeon = 5006, DungeonDifficulty tier = DungeonDifficulty.Normal) =>
        new(dungeon, DungeonEntryFacts.None with { Difficulty = tier }, 0, 0, [new(new(1), Character, 0, true)]);

    [SkippableFact]
    public async Task Party_admission_refusal_does_not_charge_any_member_and_success_commits_both()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = await Fixture(new Clock { Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.FromHours(8)) });
        var peer = f.EnterWorld(2, 72, "ruins-member");
        f.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(71,2656,1),(72,2656,1)");
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var domain = f.Services.GetRequiredService<IDarkElfRuinsDomainService>();
            var router = f.Services.GetRequiredService<IDungeonEntryRoutingDomainService>();
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            var parties = f.Services.GetRequiredService<IPartyService>();
            PartyCharacterSnapshot Member(SelectedSessionIdentity session) => new(session.AccountId, session.CharacterId,
                session.WireUserId, new(1, 0, 0));
            Assert.True(parties.RequestInvite(Member(f.Session), Member(peer), new(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(Member(peer), Member(f.Session)).Succeeded);
            var entry = Entry() with { Participants = [new(new(1), Character, 0, true), new(new(2), new(72), 1, false)] };
            await using (var tx = f.Scope.Begin())
            {
                Assert.Equal(DungeonEntryFailure.MissingRequiredItem, (await router.EnterAsync(entry, default)).Failure);
                await tx.CommitAsync(default);
            }
            Assert.Equal(1000, await Cubes(f));
            Assert.Null(runs.Find(Character)); Assert.Null(runs.Find(new CharacterId(72)));
            Assert.Equal(2, (await domain.SelectionAsync(Character, default)).Remaining);
            Assert.Equal(2, (await domain.SelectionAsync(new(72), default)).Remaining);
            await using (var tx = f.Scope.Begin())
            {
                await f.Services.GetRequiredService<IItemGrantDomainService>().GrantAllPersistentAsync(new(72), 3033, 1000, default);
                Assert.NotNull((await router.EnterAsync(entry, default)).Entry);
                await tx.CommitAsync(default);
            }
            Assert.Equal(990, await Cubes(f));
            Assert.IsType<DungeonRunMode.DarkElfRuins>(runs.Find(Character)!.Layout.Mode);
            Assert.Equal(runs.Find(Character)!.RunId, runs.Find(new CharacterId(72))!.RunId);
            Assert.Equal(1, (await domain.SelectionAsync(Character, default)).Remaining);
            Assert.Equal(1, (await domain.SelectionAsync(new(72), default)).Remaining);
        });
    }

    [SkippableFact]
    public async Task Monthly_quota_resets_at_the_first_daily_boundary_without_resetting_license()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var clock = new Clock { Now = new(2026, 11, 1, 5, 59, 0, TimeSpan.FromHours(8)) };
        await using var f = await Fixture(clock);
        f.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(71,2656,1)");
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var store = f.Services.GetRequiredService<IDarkElfRuinsDomainStore>();
            var domain = f.Services.GetRequiredService<IDarkElfRuinsDomainService>();
            await using (var tx = f.Scope.Begin())
            {
                Assert.True(await store.CompareExchangeAsync(Character, new(),
                    new(3, 7, new(2026, 10, 31), 2, new(2026, 10, 1), 30, 2, 1), default));
                await tx.CommitAsync(default);
            }
            Assert.Equal(0, (await domain.SelectionAsync(Character, default)).Remaining);
            clock.Now = clock.Now.AddMinutes(1);
            Assert.Equal(2, (await domain.SelectionAsync(Character, default)).Remaining);
            await using (var tx = f.Scope.Begin())
            {
                Assert.NotNull((await domain.EnterAsync(Entry(5008, (DungeonDifficulty)2), default)).Entry);
                await tx.CommitAsync(default);
            }
            var state = await store.ReadAsync(Character, default);
            Assert.Equal(3, state.License); Assert.Equal(7, state.Clears);
            Assert.Equal(new DateOnly(2026, 11, 1), state.Month);
            Assert.Equal(1, state.MonthlyEntries); Assert.Equal(1, state.Entries);
            Assert.Equal(f.Services.GetRequiredService<IDungeonRunDomainService>().Find(Character)!.Layout.Mode is DungeonRunMode.DarkElfRuins { Plan.Guardian: true } ? 1 : 0, state.Guardians);
        });
    }

    [SkippableFact]
    public async Task Entry_cost_and_daily_limit_are_atomic_persistent_and_reset_at_six()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var clock = new Clock { Now = new(2026, 10, 26, 23, 0, 0, TimeSpan.FromHours(8)) };
        await using var f = await Fixture(clock);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var domain = f.Services.GetRequiredService<IDarkElfRuinsDomainService>();
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            await using (var tx = f.Scope.Begin())
                Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await domain.EnterAsync(Entry(), default)).Failure);
            f.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(71,2656,1)");
            await using (var tx = f.Scope.Begin())
            {
                var attempt = await domain.EnterAsync(Entry(), default);
                Assert.True(attempt.Entry is not null, attempt.ToString());
            }
            Assert.Null(runs.Find(Character));
            Assert.Equal(1000, await Cubes(f));
            Assert.Equal(2, (await domain.SelectionAsync(Character, default)).Remaining);
            await using (var tx = f.Scope.Begin())
            {
                Assert.Equal(DungeonEntryFailure.OutsideOpeningHours, (await domain.EnterAsync(Entry(5016), default)).Failure);
                Assert.Equal(DungeonEntryFailure.DifficultyNotUnlocked,
                    (await domain.EnterAsync(Entry(5007, (DungeonDifficulty)1), default)).Failure);
                Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await domain.EnterAsync(Entry() with { PracticeMode = 1 }, default)).Failure);
            }
            for (var i = 0; i < 2; i++)
            {
                await using (var tx = f.Scope.Begin())
                {
                    var attempt = await domain.EnterAsync(Entry(), default);
                    Assert.NotNull(attempt.Entry);
                    Assert.Equal(0, attempt.Entry!.Layout.ExperienceWeight);
                    Assert.False(attempt.Entry.Layout.ConsumesFatigue);
                    await tx.CommitAsync(default);
                }
                await using var leave = f.Scope.Begin();
                Assert.True(await runs.EndAsync(Character, runs.Find(Character)!.RunId));
                await leave.CommitAsync(default);
            }
            Assert.Equal(980, await Cubes(f));
            await using (var tx = f.Scope.Begin())
                Assert.Equal(DungeonEntryFailure.DailyEntryLimitReached, (await domain.EnterAsync(Entry(), default)).Failure);
            Assert.Equal(0, (await domain.SelectionAsync(Character, default)).Remaining);
            Assert.Equal(2, (await domain.SelectionAsync(new(72), default)).Remaining);
            Assert.Equal(0, (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(Character, default)).Used);
            clock.Now = new(2026, 10, 27, 5, 59, 0, TimeSpan.FromHours(8));
            Assert.Equal(0, (await domain.SelectionAsync(Character, default)).Remaining);
            clock.Now = clock.Now.AddMinutes(1);
            Assert.Equal(2, (await domain.SelectionAsync(Character, default)).Remaining);
            Assert.Equal((byte)DayOfWeek.Tuesday, (await domain.SelectionAsync(Character, default)).DayIndex);
            await using var restarted = new LearningFixture(existing: f.Path,
                configure: s => s.AddSingleton<TimeProvider>(clock));
            Assert.Equal(2, (await restarted.Services.GetRequiredService<IDarkElfRuinsDomainStore>().ReadAsync(Character, default)).Entries);
        });
    }

    [SkippableTheory]
    [InlineData(2, 0, 20)]
    [InlineData(2, 4, 20)]
    [InlineData(3, 0, 40)]
    [InlineData(4, 0, 80)]
    public async Task Licensed_star_selects_effective_difficulty_even_when_client_keeps_another_ordinary_tier(int license, byte requested, int cost)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var clock = new Clock { Now = new(2026, 10, 26, 23, 0, 0, TimeSpan.FromHours(8)) };
        await using var f = await Fixture(clock);
        f.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(71,2656,1)");
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var store = f.Services.GetRequiredService<IDarkElfRuinsDomainStore>();
            var domain = f.Services.GetRequiredService<IDarkElfRuinsDomainService>();
            await using (var tx = f.Scope.Begin())
            {
                Assert.True(await store.CompareExchangeAsync(Character, new(),
                    new(license, 0, new(2026, 10, 26), 0, new(2026, 10, 1), 0, 0, 1), default));
                await tx.CommitAsync(default);
            }
            await using (var tx = f.Scope.Begin())
            {
                var attempt = await domain.EnterAsync(Entry(5005 + license, (DungeonDifficulty)requested), default);
                Assert.True(attempt.Entry is not null, attempt.ToString());
                var run = attempt.Entry!.Visits[Character].Run;
                Assert.Equal((DungeonDifficulty)(license - 1), run.Difficulty);
                Assert.Equal(license, Assert.IsType<DungeonRunMode.DarkElfRuins>(run.Layout.Mode).Plan.License);
                await tx.CommitAsync(default);
            }
            Assert.Equal(1000 - cost, await Cubes(f));
            Assert.Equal(1, (await domain.SelectionAsync(Character, default)).Remaining);
        });
    }

    [SkippableFact]
    public async Task Exhausted_daily_quota_takes_precedence_over_tier_and_difficulty_without_charging()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var clock = new Clock { Now = new(2026, 10, 26, 23, 0, 0, TimeSpan.FromHours(8)) };
        await using var f = await Fixture(clock);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var store = f.Services.GetRequiredService<IDarkElfRuinsDomainStore>();
            var domain = f.Services.GetRequiredService<IDarkElfRuinsDomainService>();
            var exhausted = new DarkElfRuinsProgress(2, 0, new(2026, 10, 26), 2, new(2026, 10, 1), 2, 0, 1);
            await using (var tx = f.Scope.Begin())
            {
                Assert.True(await store.CompareExchangeAsync(Character, new(), exhausted, default));
                await tx.CommitAsync(default);
            }
            foreach (var entry in new[] { Entry(5007), Entry(5008, DungeonDifficulty.Tier2) })
                await using (var tx = f.Scope.Begin())
                {
                    Assert.Equal(DungeonEntryFailure.DailyEntryLimitReached, (await domain.EnterAsync(entry, default)).Failure);
                    await tx.CommitAsync(default);
                }
            Assert.Equal(exhausted, await store.ReadAsync(Character, default));
            Assert.Equal(1000, await Cubes(f));
            Assert.Null(f.Services.GetRequiredService<IDungeonRunDomainService>().Find(Character));
        });
    }

    [SkippableFact]
    public async Task Clear_freezes_one_reward_then_requires_result_ack_and_recovers_unclaimed_after_exit()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var clock = new Clock { Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.FromHours(8)) };
        await using var f = await Fixture(clock);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            var domain = f.Services.GetRequiredService<IDarkElfRuinsDomainService>();
            var store = f.Services.GetRequiredService<IDarkElfRuinsDomainStore>();
            var cell = new MazeCell(0, 0);
            var layout = new DungeonLayout(5006, 0, 1, 1, cell, cell, 53009, 123,
                new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(53009) })
                { Mode = new DungeonRunMode.DarkElfRuins(new(new(2026, 10, 26), false, 1)), ConsumesFatigue = false, ExperienceWeight = 0 };
            DungeonRunState run;
            await using (var tx = f.Scope.Begin())
            {
                run = runs.PreparePartyEntry([new(new(1), Character, 0, true)], layout, DungeonDifficulty.Normal,
                    new(cell, 53009, 1, [new(1, new(56349, 85, 3), false, false, [])], []), DungeonRunId.New(), clock.Now).Run;
                await tx.CommitAsync(default);
            }
            await using (var tx = f.Scope.Begin()) Assert.False(await domain.ClaimAsync(f.Session, default));
            var claim = runs.TryClaimActorDeath(Character, 1)!;
            await using (var tx = f.Scope.Begin()) await domain.CompleteClearAsync(claim, [f.Session], default);
            Assert.Null(await store.RewardAsync(Character, run.RunId.Value, default));
            Assert.Equal(0, (await store.ReadAsync(Character, default)).Clears);
            await using (var tx = f.Scope.Begin())
            {
                await domain.CompleteClearAsync(claim, [f.Session], default);
                await domain.CompleteClearAsync(claim, [f.Session], default);
                await tx.CommitAsync(default);
            }
            Assert.NotNull(runs.CommitActorDeath(claim));
            Assert.NotNull(runs.TryOpenClear(Character, run.RunId, run.RoomVersion));
            Assert.Equal(1, (await store.ReadAsync(Character, default)).Clears);
            await using (var tx = f.Scope.Begin())
            {
                Assert.Equal(1, (await domain.ResultAsync(f.Session, default))!.PresentationStage);
                Assert.False(await domain.ClaimAsync(f.Session, default));
                Assert.Equal(2, (await domain.ResultAsync(f.Session, default))!.PresentationStage);
                await tx.CommitAsync(default);
            }
            await using (var tx = f.Scope.Begin()) Assert.True(await domain.ClaimAsync(f.Session, default));
            Assert.False((await store.RewardAsync(Character, run.RunId.Value, default))!.Claimed);
            Assert.Equal(0, await Count(f, 10155143));
            await using (var tx = f.Scope.Begin())
            {
                Assert.True(await runs.EndAsync(Character, run.RunId));
                await domain.RecoverRewardsAsync(f.Session, default);
                await domain.RecoverRewardsAsync(f.Session, default);
                await tx.CommitAsync(default);
            }
            Assert.True((await store.RewardAsync(Character, run.RunId.Value, default))!.Claimed);
            Assert.Empty(await store.PendingRewardsAsync(Character, default));
            Assert.Equal(1, await Count(f, 10155143));
            Assert.Equal(1, await Count(f, 10155137));
        });
    }

    private static async Task<int> Count(LearningFixture f, int item) =>
        (await f.Services.GetRequiredService<ICharacterContainerDomainService>().GetContainer(Character, InventoryContainerType.Main)
            .GetAllAsync(default)).Where(x => x.ItemId == item).Sum(x => x.Count);

    private static async Task<int> Cubes(LearningFixture f) =>
        (await f.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
            .GetPersistentStore(new(InventoryOwnerType.Account, 1), BusinessInventoryStoreKind.Item)
            .GetContainer(BusinessInventorySpace.SharedMaterial).GetAllAsync(default))
            .Where(x => x.ItemId == 3033).Sum(x => x.Count);

    private static async Task<LearningFixture> Fixture(Clock clock)
    {
        var f = new LearningFixture(useArchive: true, configure: s =>
        {
            s.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path });
            s.AddSingleton<TimeProvider>(clock);
        });
        await f.SeedAsync();
        var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
        TestGameplay.Run(f.Services, () => containers.Register(Character, new PersistentItemContainer(
            ContainerId.ForCharacter(Character, InventoryContainerType.Main), new(InventoryContainerType.Main, 0),
            f.Services.GetRequiredService<IItemDomainStore>(), f.Scope,
            f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>())));
        f.Services.GetRequiredService<IIndependentDropRuleService>().Initialize();
        f.Services.GetRequiredService<IExtraDropRuleService>().Initialize();
        f.Services.GetRequiredService<IMonsterDeclaredDropRuleService>().Initialize();
        f.Services.GetRequiredService<IWorldDropRuleService>().Initialize();
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var tx = f.Scope.Begin();
            await f.Services.GetRequiredService<IItemGrantDomainService>().GrantAllPersistentAsync(Character, 3033, 1000, default);
            await tx.CommitAsync(default);
        });
        return f;
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
