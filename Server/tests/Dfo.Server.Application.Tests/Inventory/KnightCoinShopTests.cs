using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
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
        var before = shop.Sender.Messages.Count;
        var physical = shop.Services.GetRequiredService<IInventorySlotMappingRuleService>().ToGameSlot(granted.Slot);
        await SellWire.Sell(shop, physical.Index, 2, physical.Space);
        var sale = Assert.IsType<SellItemResponse>(Assert.Single(shop.Sender.Messages.Skip(before)));
        Assert.NotNull(sale.Slot); Assert.Equal(physical, sale.Slot); Assert.Equal(2, sale.Count);
        Assert.Equal(18000,shop.Gold-80000);Assert.Equal(98000,sale.Gold);Assert.Equal(0,shop.Count(itemId));
    }
}
