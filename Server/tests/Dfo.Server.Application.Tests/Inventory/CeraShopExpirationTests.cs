using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class CeraShopExpirationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Daily_shop_reward_merges_same_day_and_keeps_old_zero_deadline_separate(bool legacyPile)
    {
        using var f = new CeraShopFixture();
        f.Content.Placements[3] = f.Content.Placements[3] with { Lifetime = new(0, 0, true) };
        if (legacyPile) await f.Host.Seed(65, 3, 1, 2);
        var first = await f.Buy(CeraShopTestContent.Entry());
        var second = await f.Buy(CeraShopTestContent.Entry());
        Assert.Equal(CeraShopRejection.None, first.Rejection);
        Assert.Equal(CeraShopRejection.None, second.Rejection);
        await TestGameplay.RunAsync(f.Host.Services, async () =>
        {
            var scope = (DomainTransactionScope)f.Host.Services.GetRequiredService<IDomainTransactionAccessor>();
            await using var tx = scope.Begin();
            var store = f.Host.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
                .GetPersistentStore(new(InventoryOwnerType.Character, 1), BusinessInventoryStoreKind.Item);
            var piles = (await store.GetContainer(BusinessInventorySpace.Consumable).GetAllAsync(default))
                .Concat(await store.GetContainer(BusinessInventorySpace.QuickSlot).GetAllAsync(default))
                .Where(x => x.ItemId == 3).ToArray();
            var deadline = new DateTimeOffset(2026, 9, 9, 6, 0, 0, TimeSpan.FromHours(8)).ToUnixTimeSeconds();
            Assert.Equal(2, Assert.Single(piles, x => x.Snapshot.ExpireTime == deadline).Count);
            Assert.Equal(legacyPile ? 2 : 1, piles.Length);
            if (legacyPile) Assert.Equal(1, Assert.Single(piles, x => x.Snapshot.ExpireTime == 0).Count);
        });
    }
}
