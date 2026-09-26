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
    [Theory]
    [InlineData(358,3037,"cube_clear")]
    [InlineData(356,3035,"cube_red")]
    public async Task Operation_two_consumes_account_material_and_notifies_after_commit(int slot,int item,string column)
    {
        using var f = new ShopFixture();
        f.Sql($"UPDATE accounts SET {column}=3 WHERE account_id=1");
        f.Sender.BeforeSend = () => Assert.Equal(1,f.Scalar($"SELECT {column} FROM accounts WHERE account_id=1"));
        await Request(f,slot,item,2);
        var ack=Assert.IsType<DeleteItemResponse>(f.Sender.Messages[0]);
        Assert.True(ack.Success);Assert.Equal(2,ack.AppliedCount);
        Assert.Equal(2,f.Sender.Messages.Count);
        Assert.Equal("InventoryDeltaMessage",f.Sender.Messages[1].GetType().Name);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Carried_material_is_consumed_and_zero_clears_the_slot(int count)
    {
        using var f = new ShopFixture();await f.Seed(65,5000,3);
        await Request(f,65,5000,count);
        Assert.True(Assert.IsType<DeleteItemResponse>(f.Sender.Messages[0]).Success);
        Assert.Equal(3-count,f.Count(5000));Assert.Equal(2,f.Sender.Messages.Count);
    }

    [Theory]
    [InlineData(0,1,0)]
    [InlineData(3,1,0)]
    [InlineData(2,2,0)]
    [InlineData(2,1,1)]
    public async Task Invalid_wire_shape_or_unknown_operation_does_not_consume(ushort operation,byte entries,uint reserved)
    {
        using var f = new ShopFixture();f.Sql("UPDATE accounts SET cube_clear=3 WHERE account_id=1");
        await Request(f,358,3037,1,operation,entries,reserved);
        Assert.False(Assert.IsType<DeleteItemResponse>(Assert.Single(f.Sender.Messages)).Success);
        Assert.Equal(3,f.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
    }

    [Fact]
    public async Task Batch_validation_and_storage_failure_never_leave_partial_consumption()
    {
        using var f=new ShopFixture();await f.Seed(65,5000,3);
        f.Sql("UPDATE accounts SET cube_clear=3 WHERE account_id=1");
        var bus=f.Services.GetRequiredService<ICommandBus>();
        SkillMaterialCost cube=new(new SkillMaterialLocation.Account(AccountMaterialKind.ClearCube),3037,2);
        SkillMaterialCost carried=new(new SkillMaterialLocation.Carried(new(BusinessInventorySpace.Consumable,0)),5000,4);
        var invalid=await bus.SendAsync(new ConsumeSkillMaterialsCommand(f.OnlineUser,f.Session,[cube,carried]));
        Assert.False(invalid.Success);
        Assert.Equal(3,f.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
        f.Sql("CREATE TRIGGER refuse_skill BEFORE DELETE ON item_slots BEGIN SELECT RAISE(ABORT,'test skill failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(async () => await bus.SendAsync(
            new ConsumeSkillMaterialsCommand(f.OnlineUser,f.Session,[cube,carried with {Count=3}])));
        Assert.Equal(3,f.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
        Assert.Equal(3,f.Count(5000));Assert.Empty(f.Sender.Messages);
    }

    [Fact]
    public async Task Last_cube_then_insufficient_balance_never_double_debits()
    {
        using var f=new ShopFixture();f.Sql("UPDATE accounts SET cube_clear=1 WHERE account_id=1");
        await Request(f,358,3037,1);
        Assert.True(Assert.IsType<DeleteItemResponse>(f.Sender.Messages[0]).Success);
        await Request(f,358,3037,1);
        Assert.False(Assert.IsType<DeleteItemResponse>(f.Sender.Messages.Last()).Success);
        Assert.Equal(0,f.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
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
        using var f=new ShopFixture();f.Sql("UPDATE accounts SET cube_clear=3 WHERE account_id=1");
        SkillMaterialCost cost=new(new SkillMaterialLocation.Account(AccountMaterialKind.ClearCube),3037,1);
        var bus=f.Services.GetRequiredService<ICommandBus>();
        Assert.False((await bus.SendAsync(new ConsumeSkillMaterialsCommand(f.OnlineUser,f.Session,[cost,cost]))).Success);
        var old=f.Session;f.Select(2);
        Assert.False((await bus.SendAsync(new ConsumeSkillMaterialsCommand(f.OnlineUser,old,[cost]))).Success);
        Assert.Equal(3,f.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
    }

    private static ValueTask Request(ShopFixture f,int slot,int item,int count,ushort operation=2,byte entries=1,uint reserved=0) =>
        f.Services.GetRequiredService<DeleteItemHandler>().HandleAsync(new(f.Connection),
            new DeleteItemRequest(GameMessageMetadata.Empty,new(InventoryContainerType.Main,slot),entries,operation,item,count,reserved,1),default);
}
