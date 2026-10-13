using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.VendingMachine;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class VendingMachineTests
{
    private sealed class Ticket(long value) : ILotteryRandomSource
    {
        public long NextInt64(long bound) => value;
    }
    private static CeraShopFixture Fixture(long ticket = 0)
    {
        var f = new CeraShopFixture(useArchive: true, configure: s => s.AddSingleton<ILotteryRandomSource>(new Ticket(ticket)));
        f.Host.Services.GetRequiredService<IVendingMachineDefinitionSource>().Initialize();
        return f;
    }
    private static Task<VendingMachineResult> Use(CeraShopFixture f, int slot=121, int machine=1) =>
        f.Host.Services.GetRequiredService<ICommandBus>().SendAsync(new VendingMachineCommand(f.Host.OnlineUser, f.Host.Session, machine, 1, slot)).AsTask();

    private static ValueTask Network(CeraShopFixture f, ushort sequence) =>
        EffectRequestTransport.Send(f.Host, new VendingMachineRequest(new(1, 0x00D7, 0, sequence), 1, 1, 121));

    [Dfo.Server.Testing.PvfFact]
    public async Task Coin_and_reward_commit_once_and_native_ack_precedes_inventory()
    {
        using var f = Fixture(); await f.Host.Seed(121,7454,3);
        await Network(f, 0);
        var response=Assert.IsType<VendingMachineResponse>(f.Host.Sender.Messages[0]);
        var result=Assert.IsType<VendingMachineResult>(response.Result);
        Assert.Equal(2,f.Host.Count(7454)); Assert.Equal(4,f.Host.Count(7463));
        var bytes=f.Encode(response).AsSpan(15).ToArray();
        Assert.Equal(1,bytes[0]); Assert.Equal(121,BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(1)));
        Assert.Equal(2,BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(3)));
        Assert.Equal(7463,BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(7)));
        Assert.Equal(4,BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(11)));
        Assert.Equal(-1,BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(15)));
        Assert.Equal(1,BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(23)));
        Assert.Equal(127,bytes.Length); // 25 header + selector byte + 101B item.
        await Network(f, 0);
        Assert.Equal(4,f.Host.Count(7463));
        await Network(f, 1); Assert.Equal(1,f.Host.Count(7454)); Assert.Equal(8,f.Host.Count(7463));
        await Network(f, 0);
        Assert.Equal(2, f.Host.Sender.Batches.Count);
        Assert.Equal(2, f.Host.Sender.Messages.OfType<VendingMachineResponse>().Count());
        Assert.Equal(1,f.Host.Count(7454));
    }
    [Dfo.Server.Testing.PvfFact]
    public async Task Advanced_machine_requires_its_coin_and_grants_its_own_reward_quantity()
    {
        using var f=Fixture(); await f.Host.Seed(121,7454,2); await f.Host.Seed(122,2749933,1);
        var rejected=await Assert.ThrowsAsync<OpeningRejectedException>(()=>Use(f,machine:3));
        Assert.Equal(OpeningRejection.MissingMaterial,rejected.Reason);
        Assert.Equal(2,f.Host.Count(7454)); Assert.Equal(1,f.Host.Count(2749933));
        var result=await Use(f,slot:122,machine:3);
        Assert.Equal(0,result.Remaining); Assert.Contains(new BusinessSlot(BusinessInventorySpace.Material,1),result.Receipt.Cleared);
        Assert.Equal(5,result.Reward.Count); Assert.Equal(5,f.Host.Count(7463));
        Assert.Equal(0,f.Host.Count(2749933)); Assert.Equal(2,f.Host.Count(7454));
    }
    [Dfo.Server.Testing.PvfFact]
    public async Task Last_coin_is_removed_and_missing_coin_cannot_grant()
    {
        using var f=Fixture(); await f.Host.Seed(121,7454,1);
        var result=await Use(f); Assert.Equal(0,result.Remaining); Assert.Contains(new BusinessSlot(BusinessInventorySpace.Material,0),result.Receipt.Cleared);
        Assert.Equal(0,f.Host.Count(7454));
        await Assert.ThrowsAsync<OpeningRejectedException>(()=>Use(f)); Assert.Equal(4,f.Host.Count(7463));
    }
    [Dfo.Server.Testing.PvfFact]
    public async Task Full_bag_leaves_coin_intact()
    {
        using var f=Fixture(); await f.Host.Seed(121,7454,2);
        for(var i=65;i<=120;i++) await f.Host.Seed(i,201,1);
        for(var i=3;i<=8;i++) await f.Host.Seed(i,201,1);
        var noRoom=await Assert.ThrowsAsync<OpeningRejectedException>(()=>Use(f));
        Assert.Equal(OpeningRejection.NoRoom,noRoom.Reason); Assert.Equal(2,f.Host.Count(7454));
        Assert.Equal(0,f.Host.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
    }
    [Dfo.Server.Testing.PvfFact]
    public async Task Storage_failure_rolls_back_and_same_sequence_retries()
    {
        using var f=Fixture(); await f.Host.Seed(121,7454,2);
        f.Host.Sql("CREATE TRIGGER reject_vending_reward BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT,'blocked'); END");
        await Assert.ThrowsAnyAsync<Exception>(()=>Use(f)); Assert.Equal(2,f.Host.Count(7454)); Assert.Equal(0,f.Host.Count(7463));
        f.Host.Sql("DROP TRIGGER reject_vending_reward"); await Use(f); Assert.Equal(1,f.Host.Count(7454));
    }
    [Dfo.Server.Testing.PvfFact]
    public async Task Invalid_machine_wrong_slot_and_stale_session_do_not_consume()
    {
        using var f=Fixture(); await f.Host.Seed(121,7454,2);
        await Assert.ThrowsAsync<OpeningRejectedException>(()=>Use(f,machine:999));
        await Assert.ThrowsAsync<OpeningRejectedException>(()=>Use(f,slot:122));
        f.Host.Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().ReleaseExact(f.Host.Session);
        // Stale admission stops before the domain handler opens its UoW.
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Use(f)); Assert.Equal(2,f.Host.Count(7454));
    }
    [Dfo.Server.Testing.PvfTheory]
    [InlineData(1)] [InlineData(3)]
    public async Task Every_positive_weight_reward_has_supported_current_delivery_content(int machine)
    {
        using var f=Fixture();
        var d=f.Host.Services.GetRequiredService<IVendingMachineDefinitionSource>().GetRequired(machine,1);
        var rules=f.Host.Services.GetRequiredService<IPackageOpeningRuleService>();
        long before=0;
        foreach(var row in d.Rewards)
        {
            if(row.CumulativeWeight>before)
            {
                var grants=await rules.PrepareAsync([row.Reward],f.Host.Clock.GetUtcNow(),default);
                Assert.NotEmpty(grants);
                Assert.All(grants,g=>Assert.DoesNotContain(g.Item.Placement.WorldClass,new[]{Dfo.Server.Abstractions.Content.ItemWorldClass.Avatar,Dfo.Server.Abstractions.Content.ItemWorldClass.Creature}));
            }
            before=row.CumulativeWeight;
        }
    }
    [Dfo.Server.Testing.PvfTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task No_entitlement_or_not_in_town_refuses_without_consumption(bool leaveTown)
    {
        var premium=new Dfo.Server.Application.Tests.Support.CardPremiums(1){Enabled=leaveTown};
        using var f=new CeraShopFixture(useArchive:true,configure:s=>s.AddSingleton<Dfo.Server.Abstractions.Accounts.IPremiumServiceDomainService>(premium));
        f.Host.Services.GetRequiredService<IVendingMachineDefinitionSource>().Initialize();
        await f.Host.Seed(121,7454,2);
        if(leaveTown)
        {
            var towns=f.Host.Services.GetRequiredService<Dfo.Server.Abstractions.World.ITownAreaDomainService>();
            towns.LeaveExact(f.Host.Session.CharacterId,towns.Find(f.Host.Session.CharacterId)!.Lease,Dfo.Server.Abstractions.World.TownAreaDepartureReason.DungeonEntry);
        }
        await Assert.ThrowsAsync<OpeningRejectedException>(()=>Use(f)); Assert.Equal(2,f.Host.Count(7454));
    }
    [Dfo.Server.Testing.PvfFact]
    public async Task Concurrent_duplicate_requests_have_one_commit()
    {
        using var f=Fixture(); await f.Host.Seed(121,7454,2);
        await Task.WhenAll(Network(f, 1).AsTask(), Network(f, 1).AsTask());
        Assert.Equal(1,f.Host.Count(7454)); Assert.Equal(4,f.Host.Count(7463));
        Assert.Single(f.Host.Sender.Messages.OfType<VendingMachineResponse>()); Assert.Single(f.Host.Sender.Batches);
    }
    [Fact]
    public void Weighted_intervals_skip_zero_weights_and_respect_boundaries()
    {
        var d=new VendingMachineDefinition(1,1,7454,1,[new(new(10,1),0,false),new(new(20,2),2,false),new(new(30,3),5,false)]);
        Assert.Equal(20,VendingMachineDomainService.Select(d,0).ItemId);
        Assert.Equal(20,VendingMachineDomainService.Select(d,1).ItemId);
        Assert.Equal(30,VendingMachineDomainService.Select(d,2).ItemId);
        Assert.Equal(30,VendingMachineDomainService.Select(d,4).ItemId);
        Assert.Throws<InvalidOperationException>(()=>VendingMachineDomainService.Select(d,5));
    }
}
