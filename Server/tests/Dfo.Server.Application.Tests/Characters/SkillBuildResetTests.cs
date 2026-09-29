using Dfo.Server.Abstractions.Content;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.Characters;

public sealed class SkillBuildResetTests
{
    [Fact]
    public async Task Third_build_has_independent_budget_and_reset_and_survives_reload()
    {
        await using var f = new LearningFixture(); await f.SeedAsync();
        await using (var tx = f.Scope.Begin())
        {
            await f.State.UnlockAsync(new(71), new(256), default);
            await f.State.ActivateAsync(new(71), new(256), default);
            await tx.CommitAsync(default);
        }
        await using (var tx = f.Scope.Begin())
        {
            var result = await f.Services.GetRequiredService<ICharacterSkillLearningDomainService>()
                .LearnAsync(f.Session, 256, [new(1,0,3)], 0, default);
            Assert.Equal(CharacterSkillLearningRejection.None,result.Rejection);
            Assert.Equal(945,result.Receipt!.Ledger.For(new(256)).Sp);
            Assert.Equal(975,result.Receipt.Ledger.For(new(0)).Sp);
            await tx.CommitAsync(default);
        }
        await using (var tx = f.Scope.Begin())
        {
            await f.State.ResetAsync(new(71),new(256),default);
            await f.State.ResetAsync(new(71),new(256),default);
            await tx.CommitAsync(default);
        }
        await using var reloaded = new LearningFixture(f.Path);
        var facts = (await reloaded.State.ReadAsync(new(71),default))!;
        Assert.Equal(256,(await reloaded.State.BuildsAsync(new(71),default)).Active.Value);
        var ledger = reloaded.Rules.GetLedger(facts.Career,facts.Level,facts.Pages,facts.PointBasis);
        Assert.Equal(1005,ledger.For(new(256)).Sp);
        Assert.Equal(975,ledger.For(new(0)).Sp);
        Assert.Equal(36+3,ledger.For(new(256)).Tp);
        Assert.All(facts.Pages.Single(p=>p.PageIndex==256).Skills, skill => Assert.Equal(4u,skill.SkillId));
    }

    [Fact]
    public async Task Failed_reset_restores_learning_and_does_not_reset_another_character()
    {
        await using var f = new LearningFixture(); await f.SeedAsync();
        f.Sql("CREATE TRIGGER fail_reset BEFORE INSERT ON character_skills WHEN NEW.character_id=71 BEGIN SELECT RAISE(ABORT,'reset failure'); END");
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var tx = f.Scope.Begin();
            await f.State.ResetAllAsync(new(71),default);
            await tx.CommitAsync(default);
        });
        Assert.Equal(2,f.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND page_index=0"));
        Assert.Equal(8,f.Scalar("SELECT level FROM character_skills WHERE character_id=72"));
        Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM character_skill_builds"));
    }

    [Fact]
    public async Task Locked_build_is_not_reset_or_switched_and_unlock_does_not_copy_paid_learning()
    {
        await using var f = new LearningFixture(); await f.SeedAsync();
        await using var tx = f.Scope.Begin();
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>await f.State.ResetAsync(new(71),new(1),default));
        var learning=f.Services.GetRequiredService<ICharacterSkillLearningDomainService>();
        Assert.Null(await learning.SwitchAsync(f.Session,0,1,default));
        await f.State.UnlockAsync(new(71),new(2),default);
        Assert.Equal(2,await learning.SwitchAsync(f.Session,0,2,default));
        Assert.Null(await learning.SwitchAsync(f.Session,0,2,default));
        await tx.CommitAsync(default);
        Assert.DoesNotContain((await f.State.ReadAsync(new(71),default))!.Pages.Single(p=>p.PageIndex==2).Skills,s=>s.SkillId==1);
    }

    [Fact]
    public async Task Refund_requires_water_and_does_not_change_state_without_it()
    {
        await using var f = new LearningFixture(); await f.SeedAsync();
        Assert.Equal(CharacterSkillLearningRejection.ResetItemRequired,(await f.LearnAsync([new(1,1,2)])).Rejection);
        Assert.Equal(2,f.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND page_index=0"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refund_consumes_one_water_and_updates_only_target_build_atomically(bool failSkillWrite)
    {
        await using var f = new LearningFixture(configure:s=>s.AddSingleton<IOpeningInventoryDomainService, WaterInventory>());
        await f.SeedAsync();
        f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(new(71),new WaterBag());
        f.Sql("UPDATE characters SET bonus_tp=3 WHERE character_id=71");
        if(failSkillWrite) f.Sql("CREATE TRIGGER fail_refund BEFORE DELETE ON character_skills WHEN OLD.character_id=71 BEGIN SELECT RAISE(ABORT,'refund failure'); END");
        if(failSkillWrite) await Assert.ThrowsAnyAsync<Exception>(()=>f.LearnAsync([new(1,1,2)]));
        else
        {
            var result=await f.LearnAsync([new(1,1,2)]);
            Assert.Equal(CharacterSkillLearningRejection.None,result.Rejection);
            Assert.NotNull(result.Receipt!.Inventory);
            Assert.Equal(0,Assert.Single(result.Receipt.Skills).Level);
            Assert.Equal(1005,result.Receipt.Ledger.For(new(0)).Sp);
        }
        Assert.Equal(failSkillWrite?3:2,f.Scalar("SELECT bonus_tp FROM characters WHERE character_id=71"));
        Assert.Equal(failSkillWrite?1:0,f.Scalar("SELECT COUNT(*) FROM character_skills WHERE character_id=71 AND page_index=0"));
        Assert.Equal(1,f.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND page_index=1"));
    }

    // Transactional stand-in payment: a scalar row makes rollback observable without reproducing inventory rules.
    private sealed class WaterInventory(SqliteDatabase db):IOpeningInventoryDomainService
    {
        public ValueTask<ContainerSlotItem> SourceAsync(SelectedSessionIdentity session,BusinessSlot slot,DateTimeOffset now,CancellationToken ct) => ValueTask.FromResult(WaterBag.Water);
        public async ValueTask<OpeningInventoryReceipt> SettleAsync(SelectedSessionIdentity session,IReadOnlyList<OpeningSlotConsumption> consumed,ImmutableArray<OpeningPreparedGrant> grants,DateTimeOffset now,CancellationToken ct)
        {
            Assert.Equal(new OpeningSlotConsumption(new(BusinessInventorySpace.Consumable,4),3,1),Assert.Single(consumed)); Assert.Empty(grants);
            await db.ExecuteAsync("UPDATE characters SET bonus_tp=bonus_tp-1 WHERE character_id=71",_=>{},ct);
            return new(session,[],[],[],now);
        }
    }
    private sealed class WaterBag:IItemContainer
    {
        public static ContainerSlotItem Water => new(69,StackableItemCore.Create(2,3,1),ItemWorldClass.Interchangeable);
        public ContainerId Id=>ContainerId.ForCharacter(new(71),InventoryContainerType.Main);
        public InventoryContainerType Type=>InventoryContainerType.Main;
        public ContainerShape Shape=>new(Type,0);
        public bool IsRemovable=>false;
        public ValueTask<IReadOnlyList<ContainerSlotItem>> ReadAllAsync(CancellationToken ct)=>ValueTask.FromResult<IReadOnlyList<ContainerSlotItem>>([Water]);
        public ValueTask<ContainerSlotItem?> ReadSlotAsync(int slot,CancellationToken ct)=>ValueTask.FromResult<ContainerSlotItem?>(slot==69?Water:null);
        public ValueTask<ContainerSlotPair> ReadSlotsAsync(int a,int b,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask PutAsync(int slot,InventoryItemCoreState core,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask ClearAsync(int slot,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<ItemPlacementOutcome> AddAsync(int item,int count,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<ItemPlacementOutcome> RefillAsync(ItemRefill refill,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<ItemMoveOutcome> MoveToAsync(int from,int to,int count,CancellationToken ct)=>throw new NotSupportedException();
    }
}
