using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class SharedMaterialPvfTests
{
    [PvfTheory]
    [InlineData(BusinessInventorySpace.PersonalCargo)]
    [InlineData(BusinessInventorySpace.AccountCargo)]
    public async Task ActualPvfAllowsAllElevenMaterialsToSplitMergeAndReturnWithoutLosingQuantity(BusinessInventorySpace cargoSpace)
    {
        using var host = new ShopFixture(services =>
            services.AddSingleton<IItemPlacementDefinitionSource, PvfItemPlacementDefinitionSource>());
        host.Sql("INSERT INTO account_cargo_state(account_id,selection_key) VALUES(1,8)");
        WarehouseStorageTests.Bind(host);
        var rules = host.Services.GetRequiredService<IItemPlacementRuleService>();
        var movement = host.Services.GetRequiredService<ICargoTransferDomainService>();
        var cargo = WarehouseStorageTests.Bag(host, cargoSpace);
        for (var index = 0; index < AccountMaterialCatalog.Definitions.Count; index++)
        {
            var definition = AccountMaterialCatalog.Definitions[index];
            var placement = await rules.PlacementForAsync(definition.ItemId, default);
            Assert.NotNull(placement);
            Assert.Equal(definition.ItemKind, placement.ItemKind);
            Assert.Equal(new(BusinessInventorySpace.SharedMaterial, index, index), placement.Destination);
            Assert.Equal(int.MaxValue, placement.StackLimit);
            await host.SeedShared(definition.ItemId, 37);
            var materialSlot = new BusinessSlot(BusinessInventorySpace.SharedMaterial, index);
            var cargoSlot = new BusinessSlot(cargoSpace, 0);
            await Move(materialSlot, cargoSlot, 7);
            await Move(materialSlot, cargoSlot, 3);
            Assert.Equal(27, host.SharedCount(definition.ItemId));
            var stored = await cargo.ReadSlotAsync(0, default);
            Assert.NotNull(stored);
            Assert.Equal(definition.ItemId, stored.ItemId);
            Assert.Equal(definition.ItemKind, stored.ItemKind);
            Assert.Equal(10, stored.Count);
            await Move(cargoSlot, materialSlot, 10);
            Assert.Equal(37, host.SharedCount(definition.ItemId));
            Assert.Null(await cargo.ReadSlotAsync(0, default));
        }
        Assert.Equal(11, (await host.Shared().ReadAllAsync(default)).Count);

        Task Move(BusinessSlot from, BusinessSlot into, int count) =>
            WarehouseStorageTests.InTransaction(host, async () =>
                Assert.IsType<ItemMoveOutcome.Moved>(await movement.MoveAsync(host.Session, from, into, count, default)));
    }
}
