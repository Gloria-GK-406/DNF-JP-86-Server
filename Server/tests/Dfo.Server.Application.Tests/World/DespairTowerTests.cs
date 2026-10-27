using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class DespairTowerTests
{
    private static DungeonEntryContext Entry(int id = 11107) => new(id, DungeonEntryFacts.None, 0, 0,
        [new(new(1), new(71), 0, true)]);
    private static async Task<LearningFixture> Fixture(Clock? clock = null)
    {
        var f = new LearningFixture(useArchive: true, configure: s =>
            {
            s.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path });
            if (clock is not null) s.AddSingleton<TimeProvider>(clock);
        });
        await f.SeedAsync();
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(new(71),
            new PersistentItemContainer(ContainerId.ForCharacter(new(71), InventoryContainerType.Main),
                new(InventoryContainerType.Main, 0), f.Services.GetRequiredService<IItemDomainStore>(), f.Scope,
                f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>())));
        f.Services.GetRequiredService<IIndependentDropRuleService>().Initialize();
        f.Services.GetRequiredService<IExtraDropRuleService>().Initialize();
        f.Services.GetRequiredService<IMonsterDeclaredDropRuleService>().Initialize();
        f.Services.GetRequiredService<IWorldDropRuleService>().Initialize();
        return f;
    }
    private static async Task<DungeonEntryAttempt> Enter(LearningFixture f, DungeonEntryContext? entry = null, bool commit = true)
    {
        await using var tx = f.Scope.Begin();
        var result = await f.Services.GetRequiredService<IDespairTowerDomainService>().EnterAsync(entry ?? Entry(), default);
        if (commit) await tx.CommitAsync(default);
        return result;
    }
    private static async Task Tickets(LearningFixture f, int count)
    {
        await using var tx = f.Scope.Begin();
        await f.Services.GetRequiredService<IItemGrantDomainService>().GrantAllPersistentAsync(new(71), 4473, count, default);
        await tx.CommitAsync(default);
    }
    private static int TicketCount(LearningFixture f) => f.Services.GetRequiredService<ICharacterContainerDomainService>()
        .GetContainer(new(71), InventoryContainerType.Main)
        .GetAllAsync(default).AsTask().GetAwaiter().GetResult().Where(x => x.ItemId == 4473).Sum(x => x.Count);
    private static async Task<DespairTowerSettlement?> Clear(LearningFixture f, bool commit = true)
    {
        var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
        var run = runs.Find(new CharacterId(71))!;
        if (!run.ClearOpen) Assert.NotNull(runs.TryOpenClear(new(71), run.RunId, run.RoomVersion));
        await using var tx = f.Scope.Begin();
        var result = await f.Services.GetRequiredService<IDespairTowerDomainService>().SettleAsync(new(1), new(71), run.RunId, default);
        if (commit) await tx.CommitAsync(default);
        return result;
    }
    [SkippableFact]
    public async Task All_hundred_real_floors_build_and_have_enemy_APCs()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = await Fixture();
        var rules = f.Services.GetRequiredService<IDespairTowerRuleService>();
        Assert.Equal(100, rules.Catalog.Floors.Count);
        Assert.Equal(70, rules.Catalog.MinimumLevel);
        Assert.Equal(3, rules.Catalog.MaximumDailyClears);
        foreach (var floor in rules.Catalog.Floors)
        {
            Assert.Equal(4473, Assert.Single(floor.Tickets).ItemId);
            var layout = await f.Services.GetRequiredService<IDungeonStructureRuleService>()
                .ResolveAsync(floor.DungeonId, DungeonEntryFacts.None, 42, default);
            Assert.NotNull(layout);
            Assert.False(layout.ConsumesFatigue);
            var cell = await f.Services.GetRequiredService<IDungeonCellFactory>().CreateAsync(new(DungeonRunId.New(),
                layout, layout.StartCell, layout.StartRoomMapId, 0, DateTimeOffset.UtcNow, Entry().Participants), default);
            Assert.NotEmpty(cell.Actors);
            Assert.True(cell.Actors.Any(actor => actor.BlocksClear), $"Floor {floor.Floor} has no hostile clear target.");
        }
    }
    [SkippableFact]
    public async Task First_entry_free_retry_requires_invitation_and_layout_transaction_rollback_preserves_eligibility()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            Assert.NotNull((await Enter(f, commit: false)).Entry);
            Assert.Equal(0, (await f.Services.GetRequiredService<IDespairTowerDomainStore>().ReadAsync(new(71), default)).HighestClearedFloor);
            var first = await Enter(f);
            Assert.True(first.Entry is not null, $"Entry refused: {first.Failure}; {first.Detail}");
            Assert.Equal(1, first.Entry.Layout.Despair!.Floor);
            Assert.Equal(11008, first.Entry.Layout.DungeonId); // forged floor 100 is routed to floor 1
            Assert.Equal(0, (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71), default)).Used);
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            await runs.EndAsync(new(71), runs.Find(new CharacterId(71))!.RunId);
            Assert.Equal(DungeonEntryFailure.MissingRequiredItem, (await Enter(f)).Failure);
            await Tickets(f, 2);
            var retry = await Enter(f, commit: false);
            Assert.True(retry.Entry is not null, $"Retry refused {retry.Failure}; {retry.Detail}");
            Assert.Equal(2, TicketCount(f));
            Assert.NotNull((await Enter(f)).Entry);
            Assert.Equal(1, TicketCount(f));
            Assert.Equal(0, (await f.Services.GetRequiredService<IDespairTowerDomainStore>().ReadAsync(new(71), default)).HighestClearedFloor);
        });
    }
    [SkippableFact]
    public async Task Clear_reward_progress_and_quota_are_atomic_idempotent_durable_and_daily_capped()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            await Tickets(f, 5);
            var service = f.Services.GetRequiredService<IDespairTowerDomainService>();
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            var store = f.Services.GetRequiredService<IDespairTowerDomainStore>();
            Assert.Null((await Enter(f, Entry() with { RequestMode = 1 })).Entry);
            Assert.Null((await Enter(f, Entry() with { PracticeMode = 1 })).Entry);
            Assert.Null((await Enter(f, Entry() with { Participants = [Entry().Participants[0], new(new(2), new(72), 1, false)] })).Entry);
            for (var floor = 1; floor <= 3; floor++)
            {
                var entered = await Enter(f);
                Assert.True(entered.Entry is not null, $"Floor {floor} refused: {entered.Failure}; {entered.Detail}");
                Assert.Equal(floor, entered.Entry.Layout.Despair!.Floor);
                var run = runs.Find(new CharacterId(71))!;
                await using (var tx = f.Scope.Begin())
                    Assert.Null(await service.SettleAsync(new(1), new(71), run.RunId, default)); // no clear authority
                if (floor == 1)
                {
                    Assert.NotNull(await Clear(f, commit: false));
                    Assert.Equal(0, (await store.ReadAsync(new(71), default)).HighestClearedFloor);
                }
                var reward = Assert.IsType<DespairTowerSettlement>(await Clear(f));
                Assert.Equal(floor, reward.Floor);
                Assert.NotEmpty(reward.Rewards);
                Assert.Null(await Clear(f));
                Assert.Equal(floor, (await store.ReadAsync(new(71), default)).HighestClearedFloor);
                await runs.EndAsync(new(71), run.RunId);
            }
            Assert.Equal(3, TicketCount(f)); // free first entry then 2 paid entries
            Assert.Equal(DungeonEntryFailure.DailyEntryLimitReached, (await Enter(f)).Failure);
            await using var reload = new LearningFixture(existing: f.Path);
            Assert.Equal(3, (await reload.Services.GetRequiredService<IDespairTowerDomainStore>().ReadAsync(new(71), default)).HighestClearedFloor);
            var period = DateOnly.FromDateTime(f.Services.GetRequiredService<IDailyResetRuleService>()
                .PeriodStart(DateTimeOffset.UtcNow).ToOffset(TimeSpan.FromHours(8)).DateTime);
            var key = UsageQuotaKey.Despair("clears", period);
            Assert.Equal(3, (await reload.Services.GetRequiredService<IUsageQuotaDomainService>().ReadAsync(new(71), key, default))!.UsedCount);
            f.Sql("UPDATE character_despair_tower SET highest_cleared_floor=100 WHERE character_id=71");
            Assert.Equal(DungeonEntryFailure.DailyEntryLimitReached, (await Enter(f)).Failure);
        });
    }
    [SkippableFact]
    public async Task Daily_reset_reopens_free_entry_and_preserves_the_next_floor()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            var clock = new Clock();
            await using var f = await Fixture(clock);
            var tower = f.Services.GetRequiredService<IDespairTowerDomainService>();
            var initial = await tower.ReadStatusAsync(new(71), default);
            Assert.Equal((1, (byte)1), (initial.NextFloor, initial.FreeEntriesRemaining));
            Assert.Equal(Enumerable.Range(11008, 100), initial.DungeonIds);
            var first = (await Enter(f)).Entry!;
            Assert.Equal((byte)0, (await tower.ReadStatusAsync(new(71), default)).FreeEntriesRemaining);
            Assert.NotNull(await Clear(f));
            var cleared = await tower.ReadStatusAsync(new(71), default);
            Assert.Equal((2, (byte)0), (cleared.NextFloor, cleared.FreeEntriesRemaining));
            await f.Services.GetRequiredService<IDungeonRunDomainService>().EndAsync(new(71), first.Visits[new(71)].Run.RunId);
            Assert.Equal(DungeonEntryFailure.MissingRequiredItem, (await Enter(f)).Failure);
            clock.Now = clock.Now.AddDays(1);
            var reset = await tower.ReadStatusAsync(new(71), default);
            Assert.Equal((2, (byte)1), (reset.NextFloor, reset.FreeEntriesRemaining));
            var next = await Enter(f);
            Assert.NotNull(next.Entry);
            Assert.Equal(2, next.Entry.Layout.Despair!.Floor);
            Assert.Empty(next.Entry.InventoryChanges);
            f.Sql("UPDATE character_despair_tower SET highest_cleared_floor=100 WHERE character_id=71");
            var finished = await tower.ReadStatusAsync(new(71), default);
            Assert.Equal((1, (byte)0), (finished.NextFloor, finished.FreeEntriesRemaining));
        });
    }

    [SkippableFact]
    public async Task Final_floor_rewards_once_then_next_day_enters_and_settles_floor_one()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            var clock = new Clock { Now = new(2026, 10, 20, 5, 59, 59, TimeSpan.FromHours(8)) };
            await using var f = await Fixture(clock);
            f.Sql("INSERT INTO character_despair_tower(character_id,highest_cleared_floor,last_clear_run) VALUES(71,99,'previous-floor')");
            var period = DateOnly.FromDateTime(f.Services.GetRequiredService<IDailyResetRuleService>()
                .PeriodStart(clock.Now).ToOffset(TimeSpan.FromHours(8)).DateTime);
            await using (var tx = f.Scope.Begin())
            {
                await f.Services.GetRequiredService<IEventService>().DirectAsync(new DespairQuotaRecorded(new(71), period, "clears", null, 2), default);
                await tx.CommitAsync(default);
            }
            var tower = f.Services.GetRequiredService<IDespairTowerDomainService>();
            var store = f.Services.GetRequiredService<IDespairTowerDomainStore>();
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            var entry = Assert.IsType<DungeonPartyEntry>((await Enter(f)).Entry);
            Assert.Equal(100, entry.Layout.Despair!.Floor);
            Assert.NotNull(await Clear(f, commit: false));
            Assert.Equal(99, (await store.ReadAsync(new(71), default)).HighestClearedFloor);
            var reward = Assert.IsType<DespairTowerSettlement>(await Clear(f));
            Assert.Equal(100, reward.Floor);
            Assert.Equal(1, Assert.Single(reward.Rewards, item => item.ItemId == 3314).Count);
            Assert.Null(await Clear(f));
            Assert.Equal(100, (await store.ReadAsync(new(71), default)).HighestClearedFloor);
            var status = await tower.ReadStatusAsync(new(71), default);
            Assert.Equal((1, (byte)0), (status.NextFloor, status.FreeEntriesRemaining));
            await runs.EndAsync(new(71), runs.Find(new CharacterId(71))!.RunId);
            Assert.Equal(DungeonEntryFailure.DailyEntryLimitReached, (await Enter(f)).Failure);
            clock.Now = clock.Now.AddSeconds(1);
            status = await tower.ReadStatusAsync(new(71), default);
            Assert.Equal((1, (byte)1), (status.NextFloor, status.FreeEntriesRemaining));
            Assert.Equal(1, (await Enter(f)).Entry!.Layout.Despair!.Floor);
            Assert.Equal(1, Assert.IsType<DespairTowerSettlement>(await Clear(f)).Floor);
            await using var reopened = new LearningFixture(existing: f.Path);
            Assert.Equal(1, (await reopened.Services.GetRequiredService<IDespairTowerDomainStore>().ReadAsync(new(71), default)).HighestClearedFloor);
        });
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [SkippableFact]
    public async Task Reward_write_failure_rolls_back_floor_inventory_and_daily_clear()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            var entry = (await Enter(f)).Entry!;
            f.Sql("CREATE TRIGGER fail_tower_clear BEFORE INSERT ON character_daily_counters WHEN NEW.counter_key LIKE 'despair.clears:%' BEGIN SELECT RAISE(ABORT,'tower test failure'); END;");
            var before = (await f.Services.GetRequiredService<ICharacterContainerDomainService>()
                .GetContainer(new(71), InventoryContainerType.Main).GetAllAsync(default)).Count;
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => Clear(f));
            Assert.Equal(0, (await f.Services.GetRequiredService<IDespairTowerDomainStore>().ReadAsync(new(71), default)).HighestClearedFloor);
            Assert.Equal(before, (await f.Services.GetRequiredService<ICharacterContainerDomainService>()
                .GetContainer(new(71), InventoryContainerType.Main).GetAllAsync(default)).Count);
            f.Sql("DROP TRIGGER fail_tower_clear");
            Assert.NotNull(await Clear(f));
        });
    }

    [SkippableFact]
    public async Task Full_equipment_bag_delivers_reward_by_mail_without_losing_progress()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            var rules = f.Services.GetRequiredService<IDespairTowerRuleService>();
            var sample = Enumerable.Range(0, 100).SelectMany(seed => rules.Rewards(1, 85, 70, (ulong)seed)).First();
            var equipment = f.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
                .GetStore(new(71), BusinessInventoryStoreKind.Item).GetContainer(BusinessInventorySpace.CarriedEquipment);
            var capacity = await equipment.OpenSlotCountAsync(default);
            await using (var tx = f.Scope.Begin())
            {
                await f.Services.GetRequiredService<IItemGrantDomainService>().GrantAllPersistentAsync(new(71), sample.ItemId, capacity, default);
                await tx.CommitAsync(default);
            }
            Assert.NotNull((await Enter(f)).Entry);
            var award = Assert.IsType<DespairTowerSettlement>(await Clear(f));
            Assert.NotEmpty(award.Rewards);
            var inbox = await f.Services.GetRequiredService<Dfo.Server.Abstractions.Mail.IMailboxDomainService>().InboxAsync(new(71));
            Assert.Equal(award.Rewards.Sum(x => x.Count), inbox.SelectMany(x => x.Attachments).Count());
            Assert.Equal(1, (await f.Services.GetRequiredService<IDespairTowerDomainStore>().ReadAsync(new(71), default)).HighestClearedFloor);
            Assert.All(inbox.SelectMany(x => x.Attachments), item => Assert.False(item.Claimed));
            // Inbox uses a read-only transaction and therefore a distinct SQLite pool.
            // LearningFixture only clears its default writable connection string.
            using var readPool = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                { DataSource = f.Path, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly }.ToString());
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(readPool);
        });
    }

}
