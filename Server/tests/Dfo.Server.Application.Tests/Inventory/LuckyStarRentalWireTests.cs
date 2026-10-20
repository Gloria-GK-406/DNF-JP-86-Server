using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Rental;
using Dfo.Server.Protocol.Game.UseStackable;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class LuckyStarRentalWireTests
{
    private static GameMessageMetadata Metadata(ushort type, byte[] body, long ordinal=1, bool fresh=true) =>
        new GameMessageMetadata(1,type,0,1) {InboundOrdinal=ordinal,SequenceIsFresh=fresh}.WithRawBody(body);

    [Dfo.Server.Testing.PvfFact]
    public async Task Purchase_then_rental_retry_preserves_gold_stars_uid_and_absolute_deadline()
    {
        using var f=new CeraShopFixture(useArchive:true,configure:services=>services.AddSingleton<Dfo.Server.Abstractions.Characters.ICharacterSkillPointDomainService,Dfo.Server.Application.Characters.CharacterSkillPointDomainService>());
        f.Host.Sql("UPDATE characters SET job=0; UPDATE character_stats SET carried_gold=5000000; INSERT INTO character_condition(character_id,pc_room_state) VALUES(1,0); INSERT INTO character_pvp(character_id) VALUES(1); INSERT INTO character_settings(character_id) VALUES(1); INSERT INTO character_expert_job(character_id) VALUES(1);");
        var dispatch=f.Host.Services.GetRequiredService<INetworkMessageDispatcher>();
        var body=new byte[22];Array.Fill(body,(byte)0xA5,0,14);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(14),1);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(18),40);
        await dispatch.DispatchAsync(new(f.Host.Connection),new ChargeRentPointRequest(Metadata(0x03D0,body),1,40),default);
        Assert.Null(Assert.Single(f.Host.Sender.Messages.OfType<ChargeRentPointResponse>()).Error);
        Assert.Equal(1000000,f.Host.Gold);
        var rental=f.Host.Services.GetRequiredService<IEquipmentRentalDomainService>();
        Assert.Equal(40,(await rental.PanelAsync(new(1),new(1),default)).LuckyStar);
        await dispatch.DispatchAsync(new(f.Host.Connection),new ChargeRentPointRequest(Metadata(0x03D0,body,2,false),1,40),default);
        Assert.Equal(1000000,f.Host.Gold);
        Assert.Equal(40,(await rental.PanelAsync(new(1),new(1),default)).LuckyStar);
        var rentBody=new byte[22];BinaryPrimitives.WriteInt32LittleEndian(rentBody.AsSpan(14),401000037);
        var rentMeta=new GameMessageMetadata(1,0x03CF,0,2){InboundOrdinal=3,SequenceIsFresh=true}.WithRawBody(rentBody);
        await dispatch.DispatchAsync(new(f.Host.Connection),new RentEquipmentRequest(rentMeta,401000037,0),default);
        Assert.Null(Assert.Single(f.Host.Sender.Messages.OfType<RentEquipmentResponse>()).Error);
        var first=await rental.PanelAsync(new(1),new(1),default);
        Assert.Equal(37,first.LuckyStar);var weapon=Assert.Single(first.Items);
        f.Host.Clock.Advance(TimeSpan.FromMinutes(5));
        var retry=new GameMessageMetadata(1,0x03CF,0,2){InboundOrdinal=4,SequenceIsFresh=false}.WithRawBody(rentBody);
        await dispatch.DispatchAsync(new(f.Host.Connection),new RentEquipmentRequest(retry,401000037,0),default);
        var second=await rental.PanelAsync(new(1),new(1),default);
        Assert.Equal(37,second.LuckyStar);Assert.Equal(weapon,Assert.Single(second.Items));
        Assert.Equal(1,f.Host.Count(401000037));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Real_star_consumable_credit_and_consumption_are_once_only_and_cap_refuses_consumption()
    {
        using var f=new CeraShopFixture(useArchive:true);
        await f.Host.Seed(69,10002998,2);
        var dispatch=f.Host.Services.GetRequiredService<INetworkMessageDispatcher>();
        var stars=f.Host.Services.GetRequiredService<IAccountLuckyStarDomainService>();
        var body=new byte[11];BinaryPrimitives.WriteInt16LittleEndian(body,69);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(7),10002998);
        await dispatch.DispatchAsync(new(f.Host.Connection),new UseStackableRequest(Metadata(0x002C,body),new(InventoryContainerType.Main,69),0,10002998),default);
        Assert.Null(Assert.Single(f.Host.Sender.Messages.OfType<UseStackableResponse>()).ErrorCode);
        Assert.Equal(1,await stars.BalanceAsync(new(1),default));Assert.Equal(1,f.Host.Count(10002998));
        await dispatch.DispatchAsync(new(f.Host.Connection),new UseStackableRequest(Metadata(0x002C,body,2,false),new(InventoryContainerType.Main,69),0,10002998),default);
        Assert.Equal(1,await stars.BalanceAsync(new(1),default));Assert.Equal(1,f.Host.Count(10002998));
        f.Host.Sql("UPDATE accounts SET lucky_star=400;");
        var next=new GameMessageMetadata(1,0x002C,0,2){InboundOrdinal=3,SequenceIsFresh=true}.WithRawBody(body);
        await dispatch.DispatchAsync(new(f.Host.Connection),new UseStackableRequest(next,new(InventoryContainerType.Main,69),0,10002998),default);
        Assert.NotNull(f.Host.Sender.Messages.OfType<UseStackableResponse>().Last().ErrorCode);
        Assert.Equal(400,await stars.BalanceAsync(new(1),default));Assert.Equal(1,f.Host.Count(10002998));
    }
    [Dfo.Server.Testing.PvfFact]
    public async Task Consumable_storage_failure_rolls_back_the_credited_stars_and_item()
    {
        using var f=new CeraShopFixture(useArchive:true);
        await f.Host.Seed(69,10002998,2);
        f.Host.Sql("CREATE TRIGGER reject_star_consume BEFORE UPDATE OF count ON item_slots WHEN OLD.owner_scope='character' AND OLD.owner_id=1 AND OLD.slot_index=69 BEGIN SELECT RAISE(ABORT,'forced consumption failure'); END;");
        var dispatch=f.Host.Services.GetRequiredService<INetworkMessageDispatcher>();
        var body=new byte[11];BinaryPrimitives.WriteInt16LittleEndian(body,69);BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(7),10002998);
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(async ()=>await dispatch.DispatchAsync(new(f.Host.Connection),
            new UseStackableRequest(Metadata(0x002C,body),new(InventoryContainerType.Main,69),0,10002998),default));
        Assert.Equal(2,f.Host.Count(10002998));
        Assert.Equal(0,await f.Host.Services.GetRequiredService<IAccountLuckyStarDomainService>().BalanceAsync(new(1),default));
    }

}
