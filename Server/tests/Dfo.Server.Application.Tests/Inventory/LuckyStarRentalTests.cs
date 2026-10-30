using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Application.World;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class LuckyStarRentalTests
{
    private static ShopFixture Fixture(int stars=0,int gold=1000000)
    {
        var f=new ShopFixture(s=>
        {
            var content=new RentalContent();
            s.AddSingleton<ILuckyStarRentalContentSource>(content);
            s.AddSingleton<IEquipmentDefinitionSource>(content);
            s.AddSingleton<IItemPlacementDefinitionSource>(content);
            s.AddSingleton<IDungeonDefinitionSource>(content);
            s.AddSingleton<IDungeonRunDomainService>(TestParties.Runs());
            s.AddDungeonRunViews();
        });
        f.Sql($"UPDATE accounts SET lucky_star={stars}; UPDATE character_stats SET carried_gold={gold};");
        f.SetLevel(85);
        return f;
    }
    private static DomainTransactionScope Scope(ShopFixture f)=>(DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>();
    private static IAccountLuckyStarDomainService Stars(ShopFixture f)=>f.Services.GetRequiredService<IAccountLuckyStarDomainService>();
    private static IEquipmentRentalDomainService Rentals(ShopFixture f)=>f.Services.GetRequiredService<IEquipmentRentalDomainService>();
    private static async Task<T> Commit<T>(ShopFixture f,Func<ValueTask<T>> work)
    { await using var tx=Scope(f).Begin();var value=await work();await tx.CommitAsync(default);return value; }

    [Fact]
    public async Task Purchase_and_item_points_are_atomic_and_old_excess_is_spendable()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f=Fixture();
            var bought=await Commit(f,()=>Stars(f).PurchaseAsync(new(1),new(1),2,default));
            Assert.True(bought.Succeeded); Assert.Equal(2,bought.Balance);Assert.Equal(800000,f.Gold);
            Assert.Equal(7,(await Commit(f,()=>Stars(f).GrantFromItemAsync(new(1),8001,default))).Balance);
            f.Sql("UPDATE accounts SET lucky_star=399;");
            Assert.Equal(LuckyStarRejection.LimitReached,(await Commit(f,()=>Stars(f).PurchaseAsync(new(1),new(1),2,default))).Rejection);
            Assert.Equal(800000,f.Gold);Assert.Equal(399,await Stars(f).BalanceAsync(new(1),default));
            Assert.Equal(LuckyStarRejection.LimitReached,(await Commit(f,()=>Stars(f).GrantFromItemAsync(new(1),8001,default))).Rejection);
            f.Sql("UPDATE accounts SET lucky_star=999;");
            Assert.Equal(999,(await Commit(f,()=>Stars(f).GrantAsync(new(1),1,true,default))).Balance);
            Assert.Equal(989,(await Commit(f,()=>Stars(f).SpendAsync(new(1),10,default))).Balance);
        });
    }
    [Theory]
    [InlineData(0,LuckyStarRejection.InvalidCount)]
    [InlineData(-1,LuckyStarRejection.InvalidCount)]
    [InlineData(int.MaxValue,LuckyStarRejection.InvalidCount)]
    [InlineData(11,LuckyStarRejection.InsufficientGold)]
    public async Task Invalid_or_unaffordable_purchase_keeps_both_balances(int count,LuckyStarRejection rejection)
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f=Fixture();
            Assert.Equal(rejection,(await Commit(f,()=>Stars(f).PurchaseAsync(new(1),new(1),count,default))).Rejection);
            Assert.Equal(0,await Stars(f).BalanceAsync(new(1),default)); Assert.Equal(1000000,f.Gold);
        });
    }
    [Fact]
    public async Task Rental_renewal_keeps_uid_uses_absolute_deadline_and_filters_expired_then_allows_new_instance()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f=Fixture(100);
            var start=f.Clock.GetUtcNow();
            var first=await Commit(f,()=>Rentals(f).RentAsync(new(1),new(1),9002,default));
            Assert.True(first.Succeeded);Assert.False(first.Renewed);
            var item=Assert.Single(first.Panel.Items);Assert.Equal(90,first.Panel.LuckyStar);
            Assert.Equal(start.AddHours(24).ToUnixTimeSeconds(),item.ExpireTime);
            Assert.Equal(999999998,InventoryItemCoreForTest(f.StoredCore(9)).Value);
            f.Clock.Advance(TimeSpan.FromHours(2));
            var renewed=await Commit(f,()=>Rentals(f).RentAsync(new(1),new(1),9002,default));
            Assert.True(renewed.Renewed);Assert.Equal(item.Uid,Assert.Single(renewed.Panel.Items).Uid);
            Assert.Equal(start.AddHours(26).ToUnixTimeSeconds(),Assert.Single(renewed.Panel.Items).ExpireTime);
            Assert.Equal(1,f.Count(9002));
            f.Clock.Advance(TimeSpan.FromHours(24));
            Assert.Empty((await Rentals(f).PanelAsync(new(1),new(1),default)).Items);
            var next=await Commit(f,()=>Rentals(f).RentAsync(new(1),new(1),9002,default));
            Assert.True(next.Succeeded);Assert.NotEqual(item.Uid,Assert.Single(next.Panel.Items).Uid);
            var entry=await f.Services.GetRequiredService<Dfo.Server.Abstractions.Persistence.ICharacterRentalTable>().ReadByCharacterAsync(new(1),default);
            Assert.Single(entry);
        });
    }
    [Fact]
    public async Task Full_inventory_or_relationship_failure_rolls_back_stars_items_and_relation()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f=Fixture(100);
            for(var i=9;i<=40;i++) await f.SeedCore(i,StackableItemCore.Create(1,9002,123));
            var full=await Commit(f,()=>Rentals(f).RentAsync(new(1),new(1),9002,default));
            Assert.Equal(EquipmentRentalRejection.InventoryFull,full.Rejection);
            Assert.Equal(100,await Stars(f).BalanceAsync(new(1),default));Assert.Equal(32,f.Count(9002));
            Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
            using var other=Fixture(100);
            other.Sql("CREATE TRIGGER fail_rental BEFORE INSERT ON equipment_rental_instances BEGIN SELECT RAISE(ABORT,'forced rental failure'); END;");
            await Assert.ThrowsAnyAsync<Exception>(()=>Commit(other,()=>Rentals(other).RentAsync(new(1),new(1),9002,default)));
            Assert.Equal(100,await Stars(other).BalanceAsync(new(1),default));Assert.Equal(0,other.Count(9002));
            Assert.Equal(0,other.Scalar("SELECT COUNT(*) FROM equipment_rental_instances"));
        });
    }
    [Fact]
    public async Task Capped_clear_is_recorded_and_cannot_be_redeemed_after_spending_and_failed_clear_can_retry()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f=Fixture(400);
            var runs=f.Services.GetRequiredService<IDungeonRunDomainService>();
            DungeonRunId Open()
            {
                if (runs.Find(new Dfo.Server.Data.Characters.CharacterId(1)) is { } previous) runs.End(new(1), previous.RunId);
                var layout=new DungeonLayout(144,0,1,1,new(0,0),new(0,0),61000,1,
                    new Dictionary<MazeCell,DungeonCellDefinition>{[new(0,0)]=DungeonCellDefinition.Fixed(61000)},85);
                var run=runs.PreparePartyEntry([new(new(1),new(1),0,true)],layout,0,
                    new DungeonCellSeed(new(0,0),61000,100,[],[]),DungeonRunId.New(),f.Clock.GetUtcNow()).Run;
                Assert.NotNull(runs.TryOpenClear(new(1),run.RunId,run.RoomVersion));
                return run.RunId;
            }
            var id=Open();
            Assert.Equal(0,(await Commit(f,()=>Stars(f).GrantForClearAsync(new(1),new(1),id,default))).Added);
            await Commit(f,()=>Stars(f).SpendAsync(new(1),10,default));
            Assert.Equal(390,(await Commit(f,()=>Stars(f).GrantForClearAsync(new(1),new(1),id,default))).Balance);
            Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM lucky_star_clear_rewards"));
            id=Open();
            f.Sql("CREATE TRIGGER fail_star BEFORE UPDATE OF lucky_star ON accounts WHEN NEW.lucky_star<>OLD.lucky_star BEGIN SELECT RAISE(ABORT,'forced star failure'); END;");
            await Assert.ThrowsAnyAsync<Exception>(()=>Commit(f,()=>Stars(f).GrantForClearAsync(new(1),new(1),id,default)));
            Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM lucky_star_clear_rewards"));
            f.Sql("DROP TRIGGER fail_star;");
            Assert.Equal(391,(await Commit(f,()=>Stars(f).GrantForClearAsync(new(1),new(1),id,default))).Balance);
            Assert.Equal(2,f.Scalar("SELECT COUNT(*) FROM lucky_star_clear_rewards"));
        });
    }
    [Fact]
    public async Task Ten_active_rentals_limit_new_templates_but_existing_uid_can_renew()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f=Fixture(1000);
            for(var item=9002;item<9012;item++) Assert.True((await Commit(f,()=>Rentals(f).RentAsync(new(1),new(1),item,default))).Succeeded);
            Assert.Equal(EquipmentRentalRejection.TooManyRentals,(await Commit(f,()=>Rentals(f).RentAsync(new(1),new(1),9012,default))).Rejection);
            Assert.Equal(900,await Stars(f).BalanceAsync(new(1),default));
            var first=(await Rentals(f).PanelAsync(new(1),new(1),default)).Items.Single(x=>x.ItemId==9002);
            var renewed=await Commit(f,()=>Rentals(f).RentAsync(new(1),new(1),9002,default));
            Assert.True(renewed.Renewed);Assert.Equal(first.Uid,renewed.Panel.Items.Single(x=>x.ItemId==9002).Uid);
            Assert.Equal(10,renewed.Panel.Items.Count);
        });
    }
    [Fact]
    public async Task Rental_uid_survives_move_and_repository_reopen_then_expiry_cleanup_removes_relation()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f=Fixture(100);
            var rented=await Commit(f,()=>Rentals(f).RentAsync(new(1),new(1),9002,default));
            var uid=Assert.Single(rented.Panel.Items).Uid;
            await using(var tx=Scope(f).Begin())
            {
                var session=f.Services.GetRequiredService<IItemDomainStore>().SessionIn(Scope(f).Current!);
                var item=Assert.Single(await session.GetAllAsync(ContainerId.ForCharacter(new(1),InventoryContainerType.Main),default));
                await session.PutAsync(ContainerId.ForCharacter(new(1),InventoryContainerType.Equipment),3,await session.TakeAsync(item,null,default),default);
                await tx.CommitAsync(default);
            }
            var original=f.Services.GetRequiredService<Dfo.Server.Persistence.Database.SqliteDatabase>();
            var database=new Dfo.Server.Persistence.Database.SqliteDatabase(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(original.ConnectionString).DataSource,TestRequired.TransactionScope());
            var reopened=new Dfo.Server.Persistence.Inventory.SqliteEquipmentRentalDomainStore(database);
            Assert.Equal(uid,Assert.Single(await reopened.ReadAsync(new(1),default)).Uid);
            Assert.Equal(uid,Assert.Single((await Rentals(f).PanelAsync(new(1),new(1),default)).Items).Uid);
            f.Clock.Advance(TimeSpan.FromHours(24));
            var cleanup=await Commit(f,()=>f.Services.GetRequiredService<IExpiredCharacterItemCleanupDomainService>().CleanupAsync(new(1),new(1),default));
            Assert.Contains(InventoryContainerType.Equipment,cleanup.ChangedContainers);
            Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM equipment_rental_instances"));
            Assert.Equal(0,f.Count(9002));
        });
    }
    [Fact]
    public async Task Deleting_rental_item_removes_its_relationship_in_same_transaction()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f=Fixture(100);
            await Commit(f,()=>Rentals(f).RentAsync(new(1),new(1),9002,default));
            await using(var tx=Scope(f).Begin())
            {
                var store=f.Services.GetRequiredService<IItemDomainStore>().SessionIn(Scope(f).Current!);
                var item=Assert.Single(await store.GetAllAsync(ContainerId.ForCharacter(new(1),InventoryContainerType.Main),default));
                await store.DeleteItemAsync(await store.TakeAsync(item,null,default),default);
                await tx.CommitAsync(default);
            }
            Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM equipment_rental_instances"));
            Assert.Empty((await Rentals(f).PanelAsync(new(1),new(1),default)).Items);
        });
    }
    private static InventoryItemCoreState InventoryItemCoreForTest(byte[] bytes)=>Dfo.Server.Persistence.Inventory.InventoryItemCore.Decode(bytes);
    private sealed class RentalContent : ILuckyStarRentalContentSource,IEquipmentDefinitionSource,IItemPlacementDefinitionSource,IDungeonDefinitionSource
    {
        public LuckyStarRentalCatalog Load()=>new(400,100000,1,new HashSet<int>{18},Enumerable.Range(9002,11).Select(x=>new RentalOffer(x,QuestJobRequirement.Knight,1,10)).ToArray());
        public ValueTask<int?> RentPointsAsync(int id,CancellationToken ct)=>ValueTask.FromResult<int?>(id==8001?5:null);
        public ValueTask<int?> QualityTierAsync(int id,CancellationToken ct)=>ValueTask.FromResult<int?>(4);
        public ValueTask<IReadOnlySet<byte>?> UsableJobsAsync(int id,CancellationToken ct)=>ValueTask.FromResult<IReadOnlySet<byte>?>(new HashSet<byte>{12});
        public ValueTask<int> InitializeAsync(CancellationToken ct)=>ValueTask.FromResult(1);
        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int id,CancellationToken ct)=>ValueTask.FromResult<EquipmentDefinition?>(id is >=9002 and <=9012?new(1,1,2,"weapon",50):null);
        public ValueTask<ItemPlacement?> GetAsync(int id,CancellationToken ct)=>ValueTask.FromResult<ItemPlacement?>(id is >=9002 and <=9012?new(1,1,new(BusinessInventorySpace.CarriedEquipment,0,55)){WorldClass=ItemWorldClass.Equipment}:null);
        ValueTask<DungeonDefinition?> IDungeonDefinitionSource.GetAsync(int id,CancellationToken ct)=>ValueTask.FromResult<DungeonDefinition?>(new(id,"test",80,true,[],85){RecommendedLevels=[80,90]});
    }
}
