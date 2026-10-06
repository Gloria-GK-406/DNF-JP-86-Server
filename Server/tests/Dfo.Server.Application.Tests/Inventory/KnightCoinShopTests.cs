using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Protocol.Game.SellItem;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class KnightCoinShopTests
{
    [PvfTheory]
    [InlineData(490002916)]
    [InlineData(490700609)]
    public async Task Configured_seria_coin_purchase_and_sale_use_real_pvf_and_commit_the_gold_spread(int itemId)
    {
        using var shop=new ShopFixture(services=>
        {
            services.RemoveAll<INpcShopDefinitionSource>();
            services.AddSingleton<INpcShopDefinitionSource,PvfNpcShopDefinitionSource>();
            services.RemoveAll<IItemPlacementDefinitionSource>();
            services.AddSingleton<IItemPlacementDefinitionSource,PvfItemPlacementDefinitionSource>();
        });
        var purchase=await shop.Command(itemId,2);
        Assert.Equal(NpcShopRejection.None,purchase.Rejection);Assert.NotNull(purchase.Receipt);
        Assert.Equal(80000,shop.Gold);Assert.Equal(2,shop.Count(itemId));
        var granted=Assert.Single(purchase.Receipt.GrantedSlots);
        Assert.Equal(BusinessInventorySpace.Material,granted.Slot.Space);
        Assert.Equal(0,granted.Core.ExpireTime);
        var sale=await shop.Services.GetRequiredService<ICommandBus>().SendAsync(
            new SellItemCommand(shop.OnlineUser,shop.Session,granted.Slot,2,2));
        Assert.Equal(ItemSaleRejection.None,sale.Rejection);Assert.NotNull(sale.Receipt);
        Assert.Equal(18000,shop.Gold-80000);Assert.Equal(98000,sale.Receipt.Gold);Assert.Equal(0,shop.Count(itemId));
    }
}
