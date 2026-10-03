using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

internal sealed class CeraShopTestContent : ICeraShopDefinitionSource, IItemPlacementDefinitionSource
{
    public Dictionary<int, CeraShopProductDefinition> Products { get; } = [];
    public Dictionary<int, CeraShopItemDefinition> Items { get; } = [];
    public Dictionary<int, ItemPlacement> Placements { get; } = [];
    public static DateTimeOffset Now { get; } = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);

    public CeraShopTestContent()
    {
        Add(110000, 3, CeraShopItemKind.Stackable, 3600);
        Add(110001, 6000, CeraShopItemKind.Stackable, 20, 25);
        Add(120000, 39032, CeraShopItemKind.Avatar, 300);
        Items[39032] = Items[39032] with { Avatar = new("[cap avatar]", [], [0, 1, 2]) };
        Products[120000] = Products[120000] with { Price = new(0, 0, 300, 7, []) };
        Products[120002] = Products[120000] with { ProductId = 120002, Price = new(0, 0, 1980, 0, []) };
        Add(105016, 63022, CeraShopItemKind.Creature, 19800);
        Items[63022] = Items[63022] with { Creature = new(20, 1) };
        Add(101075, 2747175, CeraShopItemKind.CreatureEquipment, 1580);
        Add(200003, 3000, CeraShopItemKind.Equipment, 2000);
        Items[3000] = Items[3000] with { Durability = 45, Sealed = true };
        Add(102805, 2683477, CeraShopItemKind.Package, 5300);
        Add(900001, 2683470, CeraShopItemKind.Stackable, 1);
        Add(900002, 2683473, CeraShopItemKind.Stackable, 1);
        Items[2683477] = Items[2683477] with { Components = [new(2683470, 1, 0), new(2683473, 1, 0)], MinimumPurchaseLevel = 50 };
        Add(900003, 33, CeraShopItemKind.Service, 100);
        Add(103945, 490700700, CeraShopItemKind.Package, 1888);
        Items[490700700] = Items[490700700] with { Components = [new(3, 1, 0), new(33, 1, 0)] };
    }

    public void Add(int product, int item, CeraShopItemKind kind, int price, int count = 1)
    {
        Products[product] = new(product, item, count, "[item]", new(0, 0, price, 0, []),
            kind == CeraShopItemKind.Service, false, false, [], "fixture");
        Items[item] = new(item, "fixture", kind, 0, false, null, [], [], null, null, []);
        Placements[item] = kind switch
        {
            CeraShopItemKind.Avatar => new(8, 1,
                new(BusinessInventorySpace.Avatar, 0, 209))
                { WorldClass = ItemWorldClass.Avatar },
            CeraShopItemKind.Creature => new(5, 1,
                new(BusinessInventorySpace.CreatureInventory, 0, 139))
                { WorldClass = ItemWorldClass.Creature },
            CeraShopItemKind.CreatureEquipment => new(6, 1,
                new(BusinessInventorySpace.CreatureEquipment, 0, 48))
                { WorldClass = ItemWorldClass.Equipment },
            CeraShopItemKind.Equipment => new(1, 1,
                new(BusinessInventorySpace.CarriedEquipment, 0, 47))
                { WorldClass = ItemWorldClass.Equipment },
            _ => new(2, 10, new(BusinessInventorySpace.Consumable, 0, 1)),
        };
    }
    public CeraShopRuleService Rules() => new(this, new ItemPlacementRuleService(this, new NoWearables()), new ItemArrivalRuleService());
    public ValueTask<CeraShopProductDefinition?> GetProductAsync(int id, CancellationToken ct) => ValueTask.FromResult(Products.GetValueOrDefault(id));
    public ValueTask<CeraShopItemDefinition?> GetItemAsync(int id, CancellationToken ct) => ValueTask.FromResult(Items.GetValueOrDefault(id));
    public ValueTask<ItemPlacement?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult(Placements.GetValueOrDefault(id));
    public static CeraShopPurchaseEntry Entry(int product = 110000, byte ability = 255) => new(0, ability, 255, product, [], []);
    public static CeraShopPurchaseInput Input(params CeraShopPurchaseEntry[] entries) => new(0, 0, 0, entries.ToImmutableArray());
    public static Dictionary<BusinessInventorySpace, IReadOnlyList<ItemReference>> EmptyStock() => new()
    {
        [BusinessInventorySpace.QuickSlot] = [], [BusinessInventorySpace.Consumable] = [],
        [BusinessInventorySpace.CarriedEquipment] = [], [BusinessInventorySpace.Avatar] = [],
        [BusinessInventorySpace.CreatureInventory] = [], [BusinessInventorySpace.CreatureEquipment] = [],
        [BusinessInventorySpace.CreatureConsumable] = [],
    };
    private sealed class NoWearables : IEquipmentSlotDefinitionSource
    { public ValueTask<BusinessSlot?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<BusinessSlot?>(null); }
}
