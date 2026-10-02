using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Content;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Protocol.Game.CeraShopPurchase;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class WarehousePurchaseTests
{
    [Fact]
    public async Task Duplicate_expansion_cart_is_refused_before_charging_and_storage_failure_rolls_back_wallet()
    {
        using var shop = new CeraShopFixture(configure:s => s.AddSingleton<IInventoryExpansionDefinitionSource,WarehouseStorageTests.ExpansionContent>());
        shop.Content.Add(900001,50,CeraShopItemKind.Service,60);
        var entry = CeraShopTestContent.Entry(900001);
        Assert.Null((await shop.Buy(entry,entry)).Receipt);
        Assert.Equal(100000,shop.Cera);
        shop.Host.Sql("CREATE TRIGGER fail_expansion BEFORE UPDATE ON character_container_state BEGIN SELECT RAISE(ABORT,'forced'); END;");
        await Assert.ThrowsAsync<SqliteException>(()=>shop.Buy(entry));
        Assert.Equal(100000,shop.Cera);
        Assert.Equal(0,shop.Host.Scalar("SELECT COUNT(*) FROM character_container_state WHERE list_type=2"));
    }
    [SkippableFact]
    public async Task Real_catalog_purchases_expand_personal_main_and_avatar_in_order_and_refresh_wire_parameters()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null,"PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive:true);
        foreach (var (product,type,parameter) in new[] {(100063,2,24),(101098,0,8),(102046,0,16),(102531,0,24),(102644,1,7),(102645,1,14)})
        {
            var before=shop.Cera;
            var definition=await shop.Host.Services.GetRequiredService<ICeraShopDefinitionSource>().GetProductAsync(product,default);
            var result=await shop.Buy(CeraShopTestContent.Entry(product));
            Assert.True(result.Receipt is not null,$"{product}: {result.Rejection}: {result.Detail}");
            Assert.Equal(before-definition!.Price!.Cera,shop.Cera);
            Assert.Equal(parameter,shop.Host.Scalar($"SELECT list_param16 FROM character_container_state WHERE character_id=1 AND list_type={type}"));
            Assert.Equal((ushort)parameter,result.Receipt!.ExpandedCharacterInventory!.Containers.Single(c=>(int)c.ContainerType==type).Parameter);
            Assert.Null((await shop.Buy(CeraShopTestContent.Entry(product))).Receipt);
            Assert.Equal(before-definition.Price.Cera,shop.Cera);
        }
        Assert.Equal(0,shop.Host.Scalar("SELECT COUNT(*) FROM items"));
    }
    [SkippableFact]
    public async Task Every_account_cash_tier_charges_catalog_price_and_never_grants_a_tool_item()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null,"PVF_ARCHIVE_PATH is not available.");
        using var shop=new CeraShopFixture(useArchive:true);
        var definition=await shop.Host.Services.GetRequiredService<IInventoryExpansionDefinitionSource>().AccountCargoAsync(default);
        shop.Host.Sql("INSERT INTO account_cargo_state(account_id,selection_key) VALUES(1,8)");
        for(var i=1;i<definition.Tiers.Count;i++)
        {
            var tier=definition.Tiers[i];
            if(tier.ProductId<=0) continue;
            shop.Host.Sql($"UPDATE account_cargo_state SET selection_key={definition.Tiers[i-1].Capacity}");
            var before=shop.Cera;
            var product=await shop.Host.Services.GetRequiredService<ICeraShopDefinitionSource>().GetProductAsync(tier.ProductId,default);
            var result=await shop.Buy(CeraShopTestContent.Entry(tier.ProductId));
            Assert.True(result.Receipt is not null,$"{tier.ProductId}: {result.Rejection}: {result.Detail}");
            Assert.Equal(before-product!.Price!.Cera,shop.Cera);
            Assert.Equal(tier.Capacity,result.Receipt!.ExpandedAccountInventory!.Cargo.SelectionKey);
            Assert.Null((await shop.Buy(CeraShopTestContent.Entry(tier.ProductId))).Receipt);
        }
        Assert.Equal(0,shop.Host.Scalar("SELECT COUNT(*) FROM items"));
    }
    [Fact]
    public async Task Avatar_purchase_at_closed_boundary_refuses_without_payment_then_uses_newly_opened_slot()
    {
        using var shop=new CeraShopFixture(configure:s=>s.AddSingleton<IInventoryExpansionDefinitionSource,WarehouseStorageTests.ExpansionContent>());
        shop.Content.Add(900002,12345,CeraShopItemKind.Avatar,10);
        shop.Content.Items[12345]=shop.Content.Items[12345] with { Avatar=new("[hat avatar]",[],[]) };
        var bag=WarehouseStorageTests.Bag(shop.Host,BusinessInventorySpace.Avatar);
        var registry=shop.Host.Services.GetRequiredService<ICharacterContainerDomainService>();
        var backing=registry.GetContainer(shop.Host.Session.CharacterId,InventoryContainerType.Avatar);
        await WarehouseStorageTests.InTransaction(shop.Host,async()=>{
            for(var i=0;i<105;i++) await backing.PutNewAsync(i,StackableItemCore.Create(8,12345,i+1),
                new(Avatar:new(0,0,new byte[30],0,0,0)),default);
        });
        Assert.Equal(CeraShopRejection.NoRoom,(await shop.Buy(CeraShopTestContent.Entry(900002,0))).Rejection);
        Assert.Equal(100000,shop.Cera);
        shop.Content.Add(900003,2683901,CeraShopItemKind.Service,100);
        Assert.NotNull((await shop.Buy(CeraShopTestContent.Entry(900003))).Receipt);
        Assert.Equal(112,await bag.OpenSlotCountAsync(default));
        Assert.NotNull((await shop.Buy(CeraShopTestContent.Entry(900002,0))).Receipt);
        Assert.NotNull(await bag.ReadSlotAsync(105,default));
        Assert.Equal(99890,shop.Cera);
    }
}
