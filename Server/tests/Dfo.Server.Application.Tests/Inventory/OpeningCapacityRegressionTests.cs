using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.MagicBoxBatchOpening;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class OpeningCapacityRegressionTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(80)]
    public async Task Opening_with_capacity_checks_grants_coins_and_spills_past_a_full_quick_bar(int source)
    {
        using var f = new OpeningFixture();
        EnableCapacityChecks(f.Host);
        f.Content.Definitions[1000] = f.Content.Definitions[1000] with
        {
            MaterialItemId = 0, MaterialCount = 0,
            Pools = [new(1, [new(new(42, 3), 100)]), new(1, [new(new(3, 2), 100)])],
        };
        await f.Host.Seed(source, 1000, 2);
        foreach (var slot in Enumerable.Range(3, 6).Where(slot => slot != source))
            await f.Host.Seed(slot, 6000, 1);

        var result = await Open(f, source);

        Assert.NotNull(result.Receipt);
        Assert.Equal(1, f.Host.Count(1000));
        Assert.Equal(3, f.Host.Count(1));
        Assert.Equal(0, f.Host.Count(42));
        Assert.Equal(2, f.Host.Count(3));
        Assert.Contains(result.Receipt.Updates, row => row.Slot == new BusinessSlot(BusinessInventorySpace.Consumable, 0)
            && row.Core.ItemId == 3 && row.Core.Value == 2);
        Assert.Contains(result.Receipt.Updates, row => row.Slot == new BusinessSlot(BusinessInventorySpace.VirtualResource, 1)
            && row.Core.ItemId == 1 && row.Core.Value == 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Full_physical_bag_allows_only_virtual_rewards_and_refusal_keeps_all_assets(bool physicalReward)
    {
        using var f = new OpeningFixture();
        EnableCapacityChecks(f.Host);
        f.Content.Definitions[1000] = f.Content.Definitions[1000] with
        {
            MaterialItemId = 0, MaterialCount = 0,
            Pools = physicalReward
                ? [new(1, [new(new(42, 3), 100)]), new(1, [new(new(3, 2), 100)])]
                : [new(1, [new(new(42, 3), 100)])],
        };
        await f.Host.Seed(3, 1000, 2);
        foreach (var slot in Enumerable.Range(4, 5).Concat(Enumerable.Range(65, 32)))
            await f.Host.Seed(slot, 6000, 1);

        if (physicalReward)
        {
            var failure = await Assert.ThrowsAsync<OpeningRejectedException>(() => Open(f, 3));
            Assert.Equal(OpeningRejection.NoRoom, failure.Reason);
        }
        else
        {
            var result = await Open(f, 3);
            Assert.NotNull(result.Receipt);
        }
        Assert.Equal(physicalReward ? 2 : 1, f.Host.Count(1000));
        Assert.Equal(physicalReward ? 0 : 3, f.Host.Count(1));
        Assert.Equal(0, f.Host.Count(3));
        Assert.Equal(37, f.Host.Count(6000));
    }

    private static Task<MagicBoxOpeningResult> Open(OpeningFixture f, int source) =>
        f.Host.Services.GetRequiredService<ICommandBus>().SendAsync(new MagicBoxBatchOpeningCommand(
            f.Host.OnlineUser, f.Host.Session, new(0, source, 1000, -1, 0, 1))).AsTask();

    // Match production entry binding: physical writes must use the same capacity service as previews.
    private static void EnableCapacityChecks(ShopFixture host)
    {
        var services = host.Services;
        var registry = services.GetRequiredService<ICharacterContainerDomainService>();
        var other = new[] { InventoryContainerType.Equipment, InventoryContainerType.Avatar, InventoryContainerType.Pet }
            .Select(type => registry.GetContainer(host.Session.CharacterId, type)).ToArray();
        registry.Clear(host.Session.CharacterId);
        foreach (var container in other) registry.Register(host.Session.CharacterId, container);
        registry.Register(host.Session.CharacterId, new PersistentItemContainer(
            ContainerId.ForCharacter(host.Session.CharacterId, InventoryContainerType.Main), host.Session.CharacterId,
            new(InventoryContainerType.Main, 0), services.GetRequiredService<IItemDomainStore>(),
            services.GetRequiredService<IDomainTransactionAccessor>(), null,
            services.GetRequiredService<IItemPlacementRuleService>(), services.GetRequiredService<IItemArrivalRuleService>(),
            services.GetRequiredService<IItemSlotRuleService>(), services.GetRequiredService<IInventoryCapacityDomainService>()));
    }
}
