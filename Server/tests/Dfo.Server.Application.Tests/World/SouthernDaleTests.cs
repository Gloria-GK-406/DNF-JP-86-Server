using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class SouthernDaleTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Shared_gate_is_a_union_and_each_dungeon_keeps_its_own_switch(bool tau, bool southern)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture();
            await f.SeedAsync();
            var activities = f.Services.GetRequiredService<IEventManagementDomainService>();
            await using (var tx = f.Scope.Begin())
            {
                if (!tau) await activities.ChangeAsync(new(2215, 0, "disable"), "test", DateTimeOffset.UtcNow, default);
                if (!southern) await activities.ChangeAsync(new(2330, 0, "disable"), "test", DateTimeOffset.UtcNow, default);
                await tx.CommitAsync(default);
            }
            var state = await f.Services.GetRequiredService<IEventPlayerProjectionDomainService>().ReadNowAsync(f.Session, default);
            Assert.Equal(tau || southern ? 1 : 0, state.Information.Count(x => x.EventId == 69));
            Assert.Equal(tau, state.Information.Any(x => x.EventId == 2215));
            Assert.Equal(southern, state.Information.Any(x => x.EventId == 2330));
            Assert.Equal(tau, Assert.Single(state.ScheduledDungeons, x => x.DungeonId == 3700).Open);
            Assert.Equal(southern, Assert.Single(state.ScheduledDungeons, x => x.DungeonId == 100).Open);
            Assert.Equal(southern, Assert.Single(state.ScheduledQuests, x => x.QuestId == 20549).Open);
        });
    }

    [Theory]
    [InlineData(-2, -1)]
    [InlineData(1, 2)]
    public async Task Southern_outside_its_period_cannot_keep_the_shared_gate_alive(int start, int end)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var clock = new Clock();
            await using var f = new LearningFixture(configure: s => s.AddSingleton<TimeProvider>(clock));
            await f.SeedAsync();
            var activities = f.Services.GetRequiredService<IEventManagementDomainService>();
            await using (var tx = f.Scope.Begin())
            {
                await activities.ChangeAsync(new(2215, 0, "disable"), "test", clock.Now, default);
                await activities.ChangeAsync(new(2330, 0, "configure", "南部溪谷", "南部", "", "", "",
                    clock.Now.AddDays(start), clock.Now.AddDays(end)), "test", clock.Now, default);
                await tx.CommitAsync(default);
            }
            var snapshot = await f.Services.GetRequiredService<IEventPlayerProjectionDomainService>().ReadNowAsync(f.Session, default);
            Assert.DoesNotContain(snapshot.Information, x => x.EventId is 69 or 2330);
            Assert.False(Assert.Single(snapshot.ScheduledDungeons, x => x.DungeonId == 100).Open);
            Assert.False(Assert.Single(snapshot.ScheduledQuests, x => x.QuestId == 20549).Open);
        });
    }

    [SkippableFact]
    public async Task Every_declared_candidate_materializes_with_existing_combat_and_loot()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = await Fixture();
        var definition = await f.Services.GetRequiredService<ISouthernDaleContentSource>().ReadAsync(default);
        Assert.Equal(27, definition.Dungeon.MinimumRequiredLevel);
        Assert.Equal(4, definition.PartyLimit);
        Assert.Equal(2680739, Assert.Single(definition.ClearRewards).ItemId);
        Assert.False(definition.Dungeon.OffersPaidCard);
        var layout = (await f.Services.GetRequiredService<IDungeonStructureRuleService>()
            .ResolveAsync(100, DungeonEntryFacts.None, 17, default))!;
        Assert.Equal(7, layout.Cells.Count);
        var candidates = layout.Cells.SelectMany(x => x.Value.MapCandidates.Concat(x.Value.BossMapCandidates)).Distinct().ToArray();
        Assert.Equal(16, candidates.Length);
        var allActors = new List<DungeonCellActorState>();
        foreach (var (cell, room) in layout.Cells)
            foreach (var map in room.MapCandidates.Concat(room.BossMapCandidates).Distinct())
            {
                var seed = await f.Services.GetRequiredService<IDungeonCellFactory>().CreateAsync(
                    new(DungeonRunId.New(), layout, cell, map, DungeonDifficulty.Normal,
                        DateTimeOffset.UtcNow, Entry().Participants), default);
                Assert.NotEmpty(seed.Actors);
                allActors.AddRange(seed.Actors);
            }
        Assert.Contains(allActors, x => x.IsAiCharacter);
        Assert.Contains(allActors, x => x.Actor.Code == 56424);
        foreach (var boss in new[] { 61424, 61007, 61153 }) Assert.Contains(allActors, x => x.Actor.Code == boss);
    }

    [SkippableFact]
    public async Task Admission_charges_each_member_once_and_rollback_or_refusal_keeps_assets()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            var southern = f.Services.GetRequiredService<ISouthernDaleDomainService>();
            var grants = f.Services.GetRequiredService<IItemGrantDomainService>();
            MakeParty(f);
            var context = Entry() with { Participants = [.. Entry().Participants, new(new(2), new(72), 1, false)] };
            await using (var tx = f.Scope.Begin())
            {
                await grants.GrantAllPersistentAsync(new(71), 2680738, 2, default);
                Assert.Equal(DungeonEntryFailure.MissingRequiredItem, (await southern.EnterAsync(context, default)).Failure);
                await grants.GrantAllPersistentAsync(new(72), 2680738, 2, default);
                await tx.CommitAsync(default);
            }
            Assert.Equal(2, await Count(f, 71, 2680738));
            await using (var tx = f.Scope.Begin()) Assert.NotNull((await southern.EnterAsync(context, default)).Entry);
            Assert.Null(f.Services.DungeonRuns().Find(new CharacterId(71)));
            Assert.Equal(2, await Count(f, 71, 2680738));
            Assert.Equal(2, await Count(f, 72, 2680738));
            await using (var tx = f.Scope.Begin())
            {
                Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await southern.EnterAsync(context with { PracticeMode = 1 }, default)).Failure);
                Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await southern.EnterAsync(context with { RequestMode = 1 }, default)).Failure);
                Assert.NotNull((await southern.EnterAsync(context, default)).Entry);
                await tx.CommitAsync(default);
            }
            Assert.Equal(1, await Count(f, 71, 2680738));
            Assert.Equal(1, await Count(f, 72, 2680738));
            await using (var tx = f.Scope.Begin())
                Assert.Equal(DungeonEntryFailure.AlreadyInDungeon, (await southern.EnterAsync(context, default)).Failure);
            Assert.Equal(1, await Count(f, 71, 2680738));
            Assert.Equal(0, (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71), default)).Used);
        });
    }

    [SkippableFact]
    public async Task Disabled_or_low_level_admission_does_not_consume_a_ticket()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            await using (var tx = f.Scope.Begin())
            {
                await f.Services.GetRequiredService<IItemGrantDomainService>().GrantAllPersistentAsync(new(71), 2680738, 2, default);
                await f.Services.GetRequiredService<ICharacterDomainService>().Get(new(71)).SetLevelAsync(26, default);
                var southern = f.Services.GetRequiredService<ISouthernDaleDomainService>();
                Assert.Equal(DungeonEntryFailure.LevelTooLow, (await southern.EnterAsync(Entry(), default)).Failure);
                await f.Services.GetRequiredService<IEventManagementDomainService>().ChangeAsync(new(2330, 0, "disable"), "test", DateTimeOffset.UtcNow, default);
                Assert.Equal(DungeonEntryFailure.OutsideOpeningHours, (await southern.EnterAsync(Entry(), default)).Failure);
                await tx.CommitAsync(default);
            }
            Assert.Equal(2, await Count(f, 71, 2680738));
        });
    }

    [SkippableFact]
    public async Task Clear_reward_is_atomic_for_the_party_and_durable_against_retries()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            // The second player enters the world through a connection of its own.
            var second = Dfo.Server.Testing.TestSessions.InWorld(
                f.Services.GetRequiredService<Dfo.Server.Application.Sessions.SelectedSessionDomainService>(),
                f.Services.GetRequiredService<Dfo.Server.Application.Users.OnlineStateRegistry>(),
                new(2), new(72), "southern-second");
            var players = new[] { f.Session, second };
            MakeParty(f);
            var runs = f.Services.DungeonRuns();
            var cell = new MazeCell(0, 0);
            var layout = new DungeonLayout(100, 0, 1, 1, cell, cell, 12006, 1,
                new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(12006) })
                { ConsumesFatigue = false, Mode = new DungeonRunMode.Plain(DungeonRunKind.SouthernDale) };
            await using (var setup = f.Scope.Begin())
            {
                runs.PreparePartyEntry(players.Select((x, i) => new DungeonRunParticipant(x.AccountId, x.CharacterId, (byte)i, i == 0)).ToArray(),
                    layout, DungeonDifficulty.Normal, new(cell, 12006, 1, [new(1, new(61424, 30, 3), false, false, [])], []),
                    DungeonRunId.New(), DateTimeOffset.UtcNow);
                await setup.CommitAsync(default);
            }
            var run = runs.Find(new CharacterId(71))!;
            var deaths = f.Services.GetRequiredService<IMonsterDeathDomainService>();
            var request = new MonsterDeathSettlementRequest(f.Session, players, run.RunId, run.RoomVersion, 1);
            await using (var tx = f.Scope.Begin())
                Assert.Equal(2, (await TestEffectFootprints.Run(request.Participants, cap => deaths.SettleAsync(request, cap, default))).Participants.Count);
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM southern_dale_clear_rewards"));
            Assert.Equal(0, await Count(f, 71, 2680739));
            Assert.Equal(0, await Count(f, 72, 2680739));
            f.Sql("CREATE TRIGGER fail_southern_member BEFORE INSERT ON southern_dale_clear_rewards WHEN NEW.character_id=72 BEGIN SELECT RAISE(ABORT,'member reward failure'); END");
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await using var tx = f.Scope.Begin();
                await TestEffectFootprints.Run(request.Participants, cap => deaths.SettleAsync(request, cap, default));
                await tx.CommitAsync(default);
            });
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM southern_dale_clear_rewards"));
            Assert.Equal(0, await Count(f, 71, 2680739));
            Assert.Equal(0, await Count(f, 72, 2680739));
            Assert.False(runs.Find(new CharacterId(71))!.CurrentCellState.FindActor(1)!.IsDead);
            f.Sql("DROP TRIGGER fail_southern_member");
            for (var retry = 0; retry < 2; retry++)
                await using (var tx = f.Scope.Begin())
                {
                    var result = await TestEffectFootprints.Run(request.Participants, cap => deaths.SettleAsync(request, cap, default));
                    Assert.Equal(retry == 0 ? 2 : 0, result.Participants.Count);
                    await tx.CommitAsync(default);
                }
            Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM southern_dale_clear_rewards"));
            Assert.Equal(1, await Count(f, 71, 2680739));
            Assert.Equal(1, await Count(f, 72, 2680739));
            await using var restarted = new LearningFixture(existing: f.Path);
            await using var transaction = restarted.Scope.Begin();
            Assert.False(await restarted.Services.GetRequiredService<ISouthernDaleDomainStore>().TryClaimClearAsync(run.RunId.Value, new(71), default));
        });
    }

    [SkippableFact]
    public async Task Login_online_refill_and_restart_share_one_six_oclock_target()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        var clock = new Clock();
        await using var f = await Fixture(s => s.AddSingleton<TimeProvider>(clock));
        var southern = f.Services.GetRequiredService<ISouthernDaleDomainService>();
        var rule = (await f.Services.GetRequiredService<IDailyRefillConfigurationDomainService>().EffectiveAsync(clock.Now, default))
            .Single(x => x.ItemId == 2680738);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                await f.Services.GetRequiredService<IDailyRefillDomainService>().ApplyDailyRefillAsync(new(1), new(71), [rule], clock.Now, default);
                Assert.True(await southern.RefillOnlineTicketAsync(f.Session, default));
                await tx.CommitAsync(default);
            }
        });
        Assert.Equal(2, await Count(f, 71, 2680738));
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                var bag = f.Services.GetRequiredService<ICharacterContainerDomainService>().GetContainer(new(71), InventoryContainerType.Main);
                var ticket = Assert.Single(await bag.GetAllAsync(default), x => x.ItemId == 2680738);
                var consumed = await bag.TakeAsync(ticket, 1, default);
                await ItemLifecycleScope.In(f.Scope.Current!).DeleteItemAsync(consumed, default);
                await bag.FlushAsync(default);
                await tx.CommitAsync(default);
            }
        });
        clock.Now = clock.Now.AddMinutes(1);
        for (var retry = 0; retry < 2; retry++)
            await Scheduling.TestScheduledTasks.RunActualAsync(f.Services, "southern-dale.ticket-refill.online");
        Assert.Equal(2, await Count(f, 71, 2680738));
        await using var restarted = new LearningFixture(existing: f.Path, useArchive: true,
            configure: s => s.AddSingleton(new Dfo.Server.Infrastructure.Content.PvfContentOptions { ArchivePath = ContentArchive.Path }));
        await TestGameplay.RunAsync(restarted.Services, async () =>
        {
            await using var transaction = restarted.Scope.Begin();
            var result = await restarted.Services.GetRequiredService<IDailyRefillDomainService>().ApplyDailyRefillAsync(new(1), new(71), [rule], clock.Now, default);
            Assert.Empty(result.Grants);
        });
    }

    private static DungeonEntryContext Entry() => new(100, DungeonEntryFacts.None, 0, 0, [new(new(1), new(71), 0, true)]);
    private static void MakeParty(LearningFixture f)
    {
        var parties = f.Services.GetRequiredService<IPartyService>();
        var scope = new PartyDirectoryScope(11, 1, 1);
        var leader = new PartyCharacterSnapshot(new(1), new(71), 71, scope);
        var member = new PartyCharacterSnapshot(new(2), new(72), 72, scope);
        Assert.True(parties.RequestInvite(leader, member, new(0, 0, 0, 0)).Succeeded);
        Assert.True(parties.AcceptInvite(member, leader).Succeeded);
    }
    private static async Task<int> Count(LearningFixture f, int character, int item) =>
        (await f.Services.GetRequiredService<ICharacterContainerDomainService>().GetContainer(new(character), InventoryContainerType.Main)
            .GetAllAsync(default)).Where(x => x.ItemId == item).Sum(x => x.Count);
    private static async Task<LearningFixture> Fixture(Action<IServiceCollection>? configure = null)
    {
        var f = new LearningFixture(useArchive: true, configure: s =>
        {
            s.AddSingleton(new Dfo.Server.Infrastructure.Content.PvfContentOptions { ArchivePath = ContentArchive.Path });
            configure?.Invoke(s);
        });
        await f.SeedAsync();
        var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
        foreach (var id in new[] { 71, 72 }) TestGameplay.Run(f.Services, () => containers.Register(new(id), new PersistentItemContainer(
            ContainerId.ForCharacter(new(id), InventoryContainerType.Main), new(InventoryContainerType.Main, 0),
            f.Services.GetRequiredService<IItemDomainStore>(), f.Scope,
            f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>())));
        f.Services.GetRequiredService<IIndependentDropRuleService>().Initialize();
        f.Services.GetRequiredService<IExtraDropRuleService>().Initialize();
        f.Services.GetRequiredService<IMonsterDeclaredDropRuleService>().Initialize();
        f.Services.GetRequiredService<IWorldDropRuleService>().Initialize();
        return f;
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 19, 5, 59, 0, TimeSpan.FromHours(8));
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }
}
