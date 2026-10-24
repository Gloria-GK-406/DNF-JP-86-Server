using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.BuyItem;
using Dfo.Server.Protocol.Game.LotteryOpening;
using Dfo.Server.Protocol.Game.PackageOpening;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class GoldSilverHornRewardTests
{
    [Dfo.Server.Testing.PvfTheory]
    [InlineData(490001561, 10, false)]
    [InlineData(490001562, 10, false)]
    [InlineData(490001563, 99, true)]
    [InlineData(490001564, 99, true)]
    [InlineData(490001565, 5, false)]
    public async Task Native_purchase_then_open_consumes_fragments_only_at_purchase_and_legendary_gold_only_at_opening(
        int itemId, int fragments, bool legendary)
    {
        using var f = new CeraShopFixture(useArchive: true, configure: s =>
            s.AddSingleton<INpcShopDefinitionSource, PvfNpcShopDefinitionSource>());
        var content = f.Host.Services.GetRequiredService<INpcShopDefinitionSource>();
        var shop = (await content.GetShopAsync(20152, default))!;
        Assert.NotNull(shop);
        var product = (await content.GetItemAsync(itemId, default))!;
        var material = Assert.Single(product.Materials);
        Assert.Equal(490001570, material.ItemId); Assert.Equal(fragments, material.Count);
        await f.Host.Seed(121, 490001570, fragments, 3);
        var commands = f.Host.Services.GetRequiredService<ICommandBus>();
        var purchase = await WholeBuy.Command(f.Host, itemId, 1, shop.ShopId, 20152);
        Assert.NotNull(purchase.Receipt);
        Assert.Equal(0, f.Host.Count(490001570));
        Assert.Equal(1, f.Host.Count(itemId));
        Assert.Equal(100000, f.Host.Gold);
        var slot = f.Host.Services.GetRequiredService<IInventorySlotMappingRuleService>().ToGameSlot(Assert.Single(purchase.Receipt.GrantedSlots).Slot).Index;
        if (itemId == 490001565)
        {
            var result = await OpeningDomainTestDriver.Package(f.Host, f.Host.Session, new(slot, []));
            Assert.Equal(OpeningRejection.None, result.Rejection);
            Assert.Single(result.Receipt!.Rewards);
        }
        else
        {
            await f.Host.Services.GetRequiredService<ILotteryDefinitionCache>().InitializeAsync();
            if (legendary)
            {
                await Assert.ThrowsAsync<OpeningRejectedException>(() => commands.SendAsync(
                    new LotteryOpeningCommand(f.Host.OnlineUser, f.Host.Session, slot, 0)).AsTask());
                Assert.Equal(1, f.Host.Count(itemId)); Assert.Equal(100000, f.Host.Gold);
                f.Host.Sql("UPDATE character_stats SET carried_gold=15000000 WHERE character_id=1");
                f.Host.Sql("CREATE TRIGGER gold_silver_reward_fault BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT,'reward blocked'); END");
                await Assert.ThrowsAnyAsync<Exception>(() => commands.SendAsync(
                    new LotteryOpeningCommand(f.Host.OnlineUser, f.Host.Session, slot, 0)).AsTask());
                Assert.Equal(1, f.Host.Count(itemId)); Assert.Equal(15000000, f.Host.Gold);
                f.Host.Sql("DROP TRIGGER gold_silver_reward_fault");
            }
            var result = await commands.SendAsync(new LotteryOpeningCommand(f.Host.OnlineUser, f.Host.Session, slot, 0));
            Assert.Single(result.Receipt.Rewards);
        }
        Assert.Equal(0, f.Host.Count(itemId));
        Assert.Equal(0, f.Host.Count(490001570));
        Assert.Equal(legendary ? 8000000 : 100000, f.Host.Gold);
    }
}
