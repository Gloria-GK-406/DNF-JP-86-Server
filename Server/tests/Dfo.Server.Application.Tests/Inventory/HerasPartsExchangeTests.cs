using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Protocol.Game.BuyItem;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class HerasPartsExchangeTests
{
    [SkippableFact]
    public async Task Real_Yinha_shop_charges_85_parts_and_creates_one_equipment_instance()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH")), "PVF unavailable");
        using var f = new CeraShopFixture(useArchive: true, configure: s => s.AddSingleton<INpcShopDefinitionSource, PvfNpcShopDefinitionSource>());
        await f.Host.Seed(121, 490002846, 84, 3);
        ValueTask<BuyItemOutcome> Purchase() => WholeBuy.Command(f.Host, 300340000, 1, 1277, 20152);
        Assert.Equal(NpcShopRejection.InsufficientMaterials, (await Purchase()).Rejection);
        Assert.Equal(84, f.Host.Count(490002846));
        await f.Host.Seed(121, 490002846, 85, 3);
        var outcome = await Purchase();
        Assert.NotNull(outcome.Receipt);
        Assert.Equal(0, f.Host.Count(490002846));
        Assert.Equal(1, f.Host.Count(300340000));
        Assert.Equal(1, f.Host.Scalar("SELECT count(*) FROM item_slots s JOIN items i ON s.core_uid=i.item_uid WHERE s.owner_scope='character' AND s.owner_id=1 AND s.kind='world'"));
    }
}
