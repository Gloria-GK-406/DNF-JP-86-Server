using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Protocol.Game.BuyItem;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class DarkElfRuinsExchangeTests
{
    [PvfFact]
    public async Task Alicia_exchanges_the_clear_material_once_and_refuses_without_sufficient_materials()
    {
        using var f = new CeraShopFixture(useArchive: true,
            configure: s => s.AddSingleton<INpcShopDefinitionSource, PvfNpcShopDefinitionSource>());
        var source = f.Host.Services.GetRequiredService<INpcShopDefinitionSource>();
        var shop = await source.GetShopAsync(385, default);
        Assert.NotNull(shop); Assert.Equal(165, shop.ShopId);
        Assert.Contains(shop.Pages, x => x.ItemIds.Contains(10149509));
        ValueTask<BuyItemOutcome> Buy() => WholeBuy.Command(f.Host, 10149509, 1, 165, 385);
        Assert.Equal(NpcShopRejection.InsufficientMaterials, (await Buy()).Rejection);
        await f.Host.Seed(121, 10155143, 1, 3);
        Assert.NotNull((await Buy()).Receipt);
        Assert.Equal(0, f.Host.Count(10155143));
        Assert.Equal(1, f.Host.Count(10149509));
        Assert.Equal(NpcShopRejection.InsufficientMaterials, (await Buy()).Rejection);
        Assert.Equal(1, f.Host.Count(10149509));
    }
}
