using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.DeleteItem;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class SkillMaterialConsumptionTests
{
    [Fact]
    public async Task Type_29_skill_request_consumes_the_tower_cube_and_projects_type_29_after_commit()
    {
        using var f = new ShopFixture();
        var scope = f.Services.GetRequiredService<Dfo.Server.Application.Transactions.DomainTransactionScope>();
        var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
        await using (var tx = scope.Begin())
        {
            await f.Services.GetRequiredService<IItemGrantDomainService>()
                .GrantAsync(f.Session.AccountId, f.Session.CharacterId, containers, 6515, 2, default);
            await tx.CommitAsync(default);
        }
        await f.Services.GetRequiredService<DeleteItemHandler>().HandleAsync(new(f.Connection),
            new DeleteItemRequest(GameMessageMetadata.Empty, new(InventoryContainerType.QuickSlot, 3), 1, 2, 6515, 2, 0, 1), default);
        var ack = Assert.IsType<DeleteItemResponse>(f.Sender.Messages[0]);
        Assert.True(ack.Success);
        Assert.Equal(new GameSlot(InventoryContainerType.QuickSlot, 3), ack.Slot);
        Assert.Equal("InventoryDeltaMessage", f.Sender.Messages[1].GetType().Name);
        Assert.Empty(await containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot).GetAllAsync(default));
        Assert.Equal(0, f.Count(6515));
    }

    [Theory]
    [InlineData(359, 3262)]
    [InlineData(358, 3037)]
    public async Task Effect_consumption_commits_before_ack_and_absolute_projection(int slot, int item)
    {
        using var f = new ShopFixture();
        await f.SeedShared(item, 2);
        f.Sender.BeforeSend = () => Assert.Equal(1, f.SharedCount(item));
        await Request(f, slot, item, 1, operation: 3);
        Assert.True(Assert.IsType<DeleteItemResponse>(f.Sender.Messages[0]).Success);
        Assert.Equal(2, f.Sender.Messages.Count);
        Assert.Equal("InventoryDeltaMessage", f.Sender.Messages[1].GetType().Name);
    }

    [Theory]
    [InlineData(359, 3262, 3)]
    [InlineData(358, 3037, 2)]
    public async Task Depleted_account_material_sends_only_consumption_ack(int slot, int item, ushort operation)
    {
        using var f = new ShopFixture();
        await f.SeedShared(item, 1);
        f.Sender.BeforeSend = () => Assert.Equal(0, f.SharedCount(item));
        await Request(f, slot, item, 1, operation);
        var ack = Assert.IsType<DeleteItemResponse>(Assert.Single(f.Sender.Messages));
        Assert.True(ack.Success);
        Assert.Equal(1, ack.AppliedCount);
    }

    [Fact]
    public async Task Effect_insufficient_balance_does_not_consume_or_project()
    {
        using var f = new ShopFixture();
        await f.SeedShared(3262, 1, 1);
        await Request(f, 359, 3262, 2, operation: 3);
        Assert.False(Assert.IsType<DeleteItemResponse>(Assert.Single(f.Sender.Messages)).Success);
        Assert.Equal(1, f.SharedCount(3262, 1));
    }
    [Theory]
    [InlineData(358,3037)]
    [InlineData(356,3035)]
    public async Task Operation_two_consumes_account_material_and_notifies_after_commit(int slot,int item)
    {
        using var f = new ShopFixture();
        await f.SeedShared(item, 3);
        f.Sender.BeforeSend = () => Assert.Equal(1,f.SharedCount(item));
        await Request(f,slot,item,2);
        var ack=Assert.IsType<DeleteItemResponse>(f.Sender.Messages[0]);
        Assert.True(ack.Success);Assert.Equal(2,ack.AppliedCount);
        Assert.Equal(2,f.Sender.Messages.Count);
        Assert.Equal("InventoryDeltaMessage",f.Sender.Messages[1].GetType().Name);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    public async Task Carried_material_is_consumed_and_zero_clears_the_slot(int count, ushort operation)
    {
        using var f = new ShopFixture();await f.Seed(65,5000,3);
        await Request(f,65,5000,count,operation);
        Assert.True(Assert.IsType<DeleteItemResponse>(f.Sender.Messages[0]).Success);
        Assert.Equal(3-count,f.Count(5000));Assert.Equal(2,f.Sender.Messages.Count);
    }

    [Theory]
    [InlineData(0,1,0)]
    [InlineData(4,1,0)]
    [InlineData(2,2,0)]
    [InlineData(2,1,1)]
    [InlineData(3,2,0)]
    [InlineData(3,1,1)]
    public async Task Invalid_wire_shape_or_unknown_operation_does_not_consume(ushort operation,byte entries,uint reserved)
    {
        using var f = new ShopFixture();await f.SeedShared(3037, 3, 1);
        await Request(f,358,3037,1,operation,entries,reserved);
        Assert.False(Assert.IsType<DeleteItemResponse>(Assert.Single(f.Sender.Messages)).Success);
        Assert.Equal(3,f.SharedCount(3037, 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batch_validation_and_storage_failure_never_leave_partial_consumption(bool effect)
    {
        using var f=new ShopFixture();await f.Seed(65,5000,3);
        await f.SeedShared(3037, 3, 1);
        var bus=f.Services.GetRequiredService<ICommandBus>();
        MaterialCost cube=new(new BusinessSlot(BusinessInventorySpace.SharedMaterial,4),3037,2);
        MaterialCost carried=new(new(BusinessInventorySpace.Consumable,0),5000,4);
        ValueTask<MaterialConsumptionResult> Consume(MaterialCost cost) => effect
            ? bus.SendAsync(new ConsumeEffectMaterialsCommand(f.OnlineUser,f.Session,[cube,cost]))
            : bus.SendAsync(new ConsumeSkillMaterialsCommand(f.OnlineUser,f.Session,[cube,cost]));
        var invalid=await Consume(carried);
        Assert.False(invalid.Success);
        Assert.Equal(3,f.SharedCount(3037, 1));
        f.Sql("CREATE TRIGGER refuse_skill BEFORE DELETE ON item_slots BEGIN SELECT RAISE(ABORT,'test skill failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(async () => await Consume(carried with {Count=3}));
        Assert.Equal(3,f.SharedCount(3037, 1));
        Assert.Equal(3,f.Count(5000));Assert.Empty(f.Sender.Messages);
    }

    [Fact]
    public async Task Last_cube_then_insufficient_balance_never_double_debits()
    {
        using var f=new ShopFixture();await f.SeedShared(3037, 1, 1);
        await Request(f,358,3037,1);
        Assert.True(Assert.IsType<DeleteItemResponse>(f.Sender.Messages[0]).Success);
        await Request(f,358,3037,1);
        Assert.False(Assert.IsType<DeleteItemResponse>(f.Sender.Messages.Last()).Success);
        Assert.Equal(0,f.SharedCount(3037, 1));
    }

    [Fact]
    public async Task Equipment_is_not_a_skill_material()
    {
        using var f=new ShopFixture();await f.SeedCore(9,StackableItemCore.Create(1,3000,27312));
        await Request(f,9,3000,1);
        Assert.False(Assert.IsType<DeleteItemResponse>(Assert.Single(f.Sender.Messages)).Success);
        Assert.Equal(1,f.Count(3000));
    }

    [Fact]
    public async Task Stale_session_and_duplicate_locations_are_refused()
    {
        using var f=new ShopFixture();await f.SeedShared(3037, 3, 1);
        MaterialCost cost=new(new BusinessSlot(BusinessInventorySpace.SharedMaterial,4),3037,1);
        var bus=f.Services.GetRequiredService<ICommandBus>();
        Assert.False((await bus.SendAsync(new ConsumeSkillMaterialsCommand(f.OnlineUser,f.Session,[cost,cost]))).Success);
        var old=f.Session;f.Select(2);
        Assert.False((await bus.SendAsync(new ConsumeSkillMaterialsCommand(f.OnlineUser,old,[cost]))).Success);
        Assert.Equal(3,f.SharedCount(3037, 1));
    }

    private static ValueTask Request(ShopFixture f,int slot,int item,int count,ushort operation=2,byte entries=1,uint reserved=0) =>
        f.Services.GetRequiredService<DeleteItemHandler>().HandleAsync(new(f.Connection),
            new DeleteItemRequest(GameMessageMetadata.Empty,new(InventoryContainerType.Main,slot),entries,operation,item,count,reserved,1),default);
}
