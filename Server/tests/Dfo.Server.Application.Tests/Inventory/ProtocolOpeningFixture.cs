using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

// Failure-boundary wire tests need a known pot/coin/reward, not the archive's catalogue.
// Keep the real opening rule services, weighted vending selection, inventory effects and SQLite.
internal static class ProtocolOpeningFixture
{
    internal static CeraShopFixture Create(Action<IServiceCollection> configure)
    {
        var content = new OpeningContent();
        content.Definitions[7110] = OpeningContent.Package(7110, []) with
        {
            Kind = OpeningPackageKind.Lottery,
            Pools = [new(1, [new(new(20002, 1), 1)])],
            CumulativeWeights = [1],
        };
        content.Items[20002] = new(20002,
            new(1, 1, new(BusinessInventorySpace.CarriedEquipment, 0, 47)) { WorldClass = ItemWorldClass.Equipment },
            35, true, [], null, true);
        content.Items[7463] = new(7463, new(2, 10000, new(BusinessInventorySpace.Consumable, 0, 55)), 0, false, [], null, true);
        content.Items[7454] = new(7454, new(2, 10000, new(BusinessInventorySpace.Material, 0, 55)), 0, false, [], null, true);
        return new CeraShopFixture(configure: services =>
        {
            services.AddSingleton<IPackageOpeningDefinitionSource>(content);
            services.AddSingleton<IItemPlacementDefinitionSource>(content);
            services.AddSingleton<IItemUseRuleDefinitionSource>(content);
            services.AddSingleton<ILotteryDefinitionCache>(new LotteryCache(content));
            services.AddSingleton<IVendingMachineDefinitionSource, Machine>();
            services.AddSingleton<ILotteryRandomSource, FirstTicket>();
            configure(services);
        });
    }

    private sealed class LotteryCache(OpeningContent content) : ILotteryDefinitionCache
    {
        public ValueTask<int> InitializeAsync(CancellationToken ct = default) => ValueTask.FromResult(1);
        public OpeningPackageDefinition GetRequired(int id) => content.Definitions.TryGetValue(id, out var definition)
            ? definition : throw new OpeningRejectedException(OpeningRejection.WrongItem, "Undeclared fixture pot.");
    }
    private sealed class Machine : IVendingMachineDefinitionSource
    {
        public int Initialize() => 1;
        public VendingMachineDefinition GetRequired(int machine, int group) => machine == 1 && group == 1
            ? new(1, 1, 7454, 1, [new(new(7463, 4), 1, false)])
            : throw new OpeningRejectedException(OpeningRejection.InvalidRequest, "Undeclared fixture machine.");
    }
    private sealed class FirstTicket : ILotteryRandomSource
    {
        public long NextInt64(long exclusiveUpperBound) => 0;
    }
}
