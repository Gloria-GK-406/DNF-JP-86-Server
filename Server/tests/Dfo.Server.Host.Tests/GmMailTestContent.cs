using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

// Business/transaction tests use declared content on every machine. Tests of the actual
// archive opt out explicitly; missing local PVF must not turn rollback checks into 500s.
internal sealed class GmMailTestContent : IPackageOpeningDefinitionSource, IItemPlacementDefinitionSource,
    IItemCatalogSource, IEquipmentDefinitionSource, IEquipmentReinforcementDefinitionSource,
    IEquipmentOutworldContentSource, IGoldCarryLimitSource, IInventoryExpansionDefinitionSource, IEntryLifecycleContentSource
{
    private static readonly ItemCatalogEntry[] Items = [
        new(3, "测试消耗品", "consumable", null, 1, 1),
        new(3033, "测试黑色材料", "material", null, 1, 1),
        new(3034, "测试白色材料", "material", null, 1, 1),
        new(3035, "测试红色材料", "material", null, 1, 1),
        new(3037, "测试无色材料", "material", null, 1, 1),
        new(27312, "测试武器", "equipment", "weapon", 80, 2),
        new(100310498, "测试手镯", "equipment", "wrist", 80, 2),
    ];

    internal static void Register(IServiceCollection services)
    {
        var content = new GmMailTestContent();
        services.AddSingleton<IPackageOpeningDefinitionSource>(content);
        services.AddSingleton<IItemPlacementDefinitionSource>(content);
        services.AddSingleton<IItemCatalogSource>(content);
        services.AddSingleton<IEquipmentDefinitionSource>(content);
        services.AddSingleton<IEquipmentReinforcementDefinitionSource>(content);
        services.AddSingleton<IEquipmentOutworldContentSource>(content);
        services.AddSingleton<IGoldCarryLimitSource>(content);
        services.AddSingleton<IInventoryExpansionDefinitionSource>(content);
        services.AddSingleton<IEntryLifecycleContentSource>(content);
        services.AddSingleton<IDungeonExperienceProgressionPolicySource, Dfo.Server.Application.Tests.Support.TestProgressionSource>();
    }

    public ValueTask<ItemCatalogEntry?> FindAsync(int itemId, CancellationToken token) =>
        ValueTask.FromResult(Items.SingleOrDefault(item => item.ItemId == itemId));

    public ValueTask<IReadOnlyList<ItemCatalogEntry>> SearchAsync(ItemCatalogFilter filter, CancellationToken token) =>
        ValueTask.FromResult<IReadOnlyList<ItemCatalogEntry>>(Items.Where(item =>
            (filter.ItemId is null || item.ItemId == filter.ItemId)
            && (filter.Q is null || item.Name.Contains(filter.Q, StringComparison.OrdinalIgnoreCase))
            && (filter.Category is null || item.Category == filter.Category)
            && (filter.EquipmentType is null || item.EquipmentType == filter.EquipmentType)
            && (filter.MinimumLevel is null || item.Level >= filter.MinimumLevel)
            && (filter.MaximumLevel is null || item.Level <= filter.MaximumLevel)
            && (filter.Rarity is null || item.Rarity == filter.Rarity)).ToArray());

    private static ItemPlacement? Placement(int itemId) => itemId switch
    {
        27312 or 100310498 => new(1, 1, new(BusinessInventorySpace.CarriedEquipment, 0, 55)) { WorldClass = ItemWorldClass.Equipment },
        3 => new(2, 1000, new(BusinessInventorySpace.Consumable, 0, 47)),
        3033 or 3034 or 3035 or 3037 => new(11, 1000, new(BusinessInventorySpace.Material, 0, 55)),
        1 => new(2, 1000, new(BusinessInventorySpace.VirtualResource, 0, 0)),
        33 or 42 => new(2, 1000, new(BusinessInventorySpace.Consumable, 0, 47)),
        39032 => new(8, 1, new(BusinessInventorySpace.Avatar, 0, 55)) { WorldClass = ItemWorldClass.Avatar },
        63022 => new(5, 1, new(BusinessInventorySpace.CreatureInventory, 0, 55)) { WorldClass = ItemWorldClass.Creature },
        2747175 => new(6, 1, new(BusinessInventorySpace.CreatureEquipment, 0, 55)),
        _ => null,
    };

    ValueTask<ItemPlacement?> IItemPlacementDefinitionSource.GetAsync(int itemId, CancellationToken token) => ValueTask.FromResult(Placement(itemId));
    ValueTask<OpeningPackageDefinition?> IPackageOpeningDefinitionSource.GetAsync(int itemId, CancellationToken token) =>
        ValueTask.FromResult<OpeningPackageDefinition?>(null);
    public ValueTask<OpeningRewardItemDefinition?> GetRewardAsync(int itemId, CancellationToken token) =>
        ValueTask.FromResult<OpeningRewardItemDefinition?>(Placement(itemId) is { } placement
            ? new(itemId, placement, itemId == 27312 ? (ushort)45 : (ushort)0,
                placement.WorldClass == ItemWorldClass.Equipment, [], itemId == 63022 ? 1 : null, true,
                DefaultPeriodDays: itemId == 42 ? 1 : 0, PremiumType: itemId == 33 ? 1 : 0) : null);

    public ValueTask<IReadOnlyList<DailyRefillRule>> GetDailyRefillRulesAsync(DateTimeOffset at, CancellationToken token) => ValueTask.FromResult<IReadOnlyList<DailyRefillRule>>([]);
    public ValueTask<AccountCargoDefinition> AccountCargoAsync(CancellationToken token) => ValueTask.FromResult(new AccountCargoDefinition(60, [new(8, 100000000, -1, 0, 0, -1)]));
    public ValueTask<InventoryExpansionGrant?> ItemAsync(int id, CancellationToken token) => ValueTask.FromResult<InventoryExpansionGrant?>(null);
    public ValueTask<InventoryExpansionGrant?> ProductAsync(int product, int item, CancellationToken token) => ValueTask.FromResult<InventoryExpansionGrant?>(null);

    public int BaseLimitFor(int characterLevel) => 400_000_000;
    public ValueTask<int> InitializeAsync(CancellationToken token) => ValueTask.FromResult(2);
    public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int itemId, CancellationToken token) =>
        ValueTask.FromResult<EquipmentDefinition?>(itemId switch
        {
            27312 => new(80, 82, 2, "weapon", 45),
            100310498 => new(80, 82, 2, "wrist", null),
            _ => null,
        });

    public ValueTask<EquipmentReinforcementTable?> GetNormalTableAsync(CancellationToken token) => ValueTask.FromResult<EquipmentReinforcementTable?>(UpgradeTable);
    public ValueTask<EquipmentReinforcementTable?> GetAmplificationTableAsync(CancellationToken token) => ValueTask.FromResult<EquipmentReinforcementTable?>(UpgradeTable);
    public ValueTask<EquipmentReinforcementProtectionDefinition?> GetProtectionAsync(int itemId, CancellationToken token,
        EquipmentUpgradeKind kind = EquipmentUpgradeKind.Reinforcement) => ValueTask.FromResult<EquipmentReinforcementProtectionDefinition?>(null);

    private static readonly EquipmentReinforcementTable UpgradeTable = new(
        Enumerable.Range(1, 31).Select(level => new EquipmentReinforcementRow(level, new double[19], 0, 1, 0, 3037, 1)).ToArray(),
        Enumerable.Repeat(100, 100).ToArray(), [1, 1, 1], Enumerable.Repeat(1d, 12).ToArray(),
        new Dictionary<int, double>(), new Dictionary<int, int>(), new Dictionary<int, int>(),
        new(3037, 40, 10, new Dictionary<int, double>(), new Dictionary<int, int>()), 12);

    ValueTask<EquipmentOutworldTable?> IEquipmentOutworldContentSource.GetAsync(CancellationToken token) =>
        ValueTask.FromResult<EquipmentOutworldTable?>(new(55, new Dictionary<int, int>(), new Dictionary<int, int>(),
            new Dictionary<int, EquipmentOutworldMaterialDefinition>(), new Dictionary<int, EquipmentOutworldMaterialDefinition>(),
            new Dictionary<int, IReadOnlyDictionary<byte, ushort>> { [2] = new Dictionary<byte, ushort> { [1] = 5, [2] = 5, [3] = 5, [4] = 5 } }, []));
}
