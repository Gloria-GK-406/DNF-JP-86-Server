using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class NpcShopRuleTests
{
    [Theory]
    [InlineData(3000, 0, NpcShopRejection.None)]
    [InlineData(3000, 1, NpcShopRejection.None)]
    [InlineData(3000, 2, NpcShopRejection.InvalidCount)]
    [InlineData(2000, 0, NpcShopRejection.InvalidCount)]
    [InlineData(2000, -1, NpcShopRejection.InvalidCount)]
    [InlineData(2000, int.MaxValue, NpcShopRejection.InvalidCount)]
    public async Task Only_a_single_equipment_request_uses_the_zero_sentinel(int item, int count, NpcShopRejection rejection)
    {
        var result = await new NpcShopTestContent().Rules().QuoteAsync(2, 15, 12, item, count, CancellationToken.None);
        Assert.Equal(rejection, result.Rejection);
        if (rejection == NpcShopRejection.None)
            Assert.Equal(1, result.Quote!.Count);
    }

    [Fact]
    public async Task Both_the_shop_binding_and_the_offered_item_are_required()
    {
        var rules = new NpcShopTestContent().Rules();
        Assert.Equal(NpcShopRejection.ShopUnavailable, (await rules.QuoteAsync(302, 15, 12, 2000, 1, default)).Rejection);
        Assert.Equal(NpcShopRejection.ShopUnavailable, (await rules.QuoteAsync(2, 87, 12, 2000, 1, default)).Rejection);
        Assert.Equal(NpcShopRejection.ItemNotSold, (await rules.QuoteAsync(2, 15, 12, 9999, 1, default)).Rejection);
    }

    [Fact]
    public async Task Missing_gold_price_on_a_material_exchange_is_not_a_selling_value_fallback()
    {
        var quote = (await new NpcShopTestContent().Rules().QuoteAsync(2, 15, 12, 2000, 3, default)).Quote!;
        Assert.Equal(0, quote.GoldCost);
        Assert.Equal(new NpcShopMaterialCost(1000, 3), Assert.Single(quote.Materials));
    }

    [Fact]
    public async Task A_mixed_payment_can_consume_two_piles_and_fill_two_product_slots()
    {
        var rules = new NpcShopTestContent().Rules();
        var quote = (await rules.QuoteAsync(2, 15, 12, 5000, 3, default)).Quote!;
        var plan = rules.Plan(quote, [Pile(121, 1000, 7, 3), Pile(122, 1000, 8, 3), Pile(3, 5000, 9)], 1000, 42);
        Assert.Equal(NpcShopRejection.None, plan.Rejection);
        Assert.Equal(300, quote.GoldCost);
        Assert.Equal(new[] { 121, 122 }, plan.Plan!.MaterialChanges.Select(change => change.SlotIndex));
        Assert.All(plan.Plan.MaterialChanges, change => Assert.Null(change.Core));
        Assert.Equal(new[] { (3, 10, 1), (4, 2, 2) }, plan.Plan.GrantedSlots
            .Select(slot => (slot.Item.SlotIndex, slot.Item.Core.Value, slot.AddedCount)));
    }

    [Fact]
    public async Task No_room_rejects_the_whole_plan_even_when_payment_would_free_another_band()
    {
        var rules = new NpcShopTestContent().Rules();
        var quote = (await rules.QuoteAsync(2, 15, 12, 5000, 1, default)).Quote!;
        var held = new[] { 3, 4, 5, 6, 7, 8, 65, 66 }.Select(slot => Pile(slot, 5000, 10)).ToList();
        held.Add(Pile(121, 1000, 5, 3));
        var result = rules.Plan(quote, held, 1000, 42);
        Assert.Equal(NpcShopRejection.NoRoom, result.Rejection);
        Assert.Null(result.Plan);
        Assert.Equal(5, held.Single(item => item.ItemId == 1000).Count);
    }

    [Fact]
    public async Task A_missing_balance_produces_no_partial_plan()
    {
        var rules = new NpcShopTestContent().Rules();
        var quote = (await rules.QuoteAsync(2, 15, 12, 5000, 1, default)).Quote!;
        Assert.Equal(NpcShopRejection.InsufficientGold, rules.Plan(quote, [], 99, 42).Rejection);
        Assert.Equal(NpcShopRejection.InsufficientMaterials, rules.Plan(quote, [Pile(121, 1000, 4, 3)], 100, 42).Rejection);
    }

    [Fact]
    public void A_limit_only_changes_period_when_the_archive_declares_daily_reset()
    {
        var rules = new NpcShopTestContent().Rules();
        var limit = new NpcShopPurchaseLimit(NpcShopLimitScope.Account, 1, false);
        var now = new DateTimeOffset(2026, 9, 8, 16, 0, 0, TimeSpan.Zero);
        Assert.Equal(rules.PurchasePeriod(limit, now), rules.PurchasePeriod(limit, now.AddDays(1)));
        Assert.NotEqual(rules.PurchasePeriod(limit with { ResetsDaily = true }, now),
            rules.PurchasePeriod(limit with { ResetsDaily = true }, now.AddDays(1)));
    }

    internal static ContainerSlotItem Pile(int slot, int item, int count, byte kind = 2) =>
        new(slot, StackableItemCore.Create(kind, item, count), ItemWorldClass.Interchangeable);
}

internal sealed class NpcShopTestContent : INpcShopDefinitionSource, IItemPlacementDefinitionSource
{
    public Dictionary<int, NpcShopItemDefinition> Items { get; } = new()
    {
        [2000] = new(2000, null, 0, [new(1000, 1)], null, 0, false, true),
        [3000] = new(3000, 18000, 0, [], null, 45, false, true),
        [4000] = new(4000, null, 0, [new(1000, 35)], new(NpcShopLimitScope.Account, 1, false), 0, false, true),
        [5000] = new(5000, 100, 0, [new(1000, 5)], null, 0, false, true),
    };

    public NpcShopRuleService Rules() => new(this,
        new ItemPlacementRuleService(this, new NoWearables()), new ItemArrivalRuleService(), new DailyRefillRuleService());

    public ValueTask<NpcShopDefinition?> GetShopAsync(int npcId, CancellationToken ct) =>
        ValueTask.FromResult<NpcShopDefinition?>(npcId == 2 ? new(2, 15, [new(null, Items.Keys.ToArray())]) : null);
    public ValueTask<NpcShopItemDefinition?> GetItemAsync(int itemId, CancellationToken ct) =>
        ValueTask.FromResult(Items.GetValueOrDefault(itemId));
    public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken ct) =>
        ValueTask.FromResult<ItemPlacement?>(itemId switch
        {
            1000 => new(3, 1000, new(BusinessInventorySpace.Material, 0, 1)),
            >= 3257 and <= 3260 => new(3, 1000, new(BusinessInventorySpace.Material, 0, 1)),
            3000 => new(1, 1, new(BusinessInventorySpace.CarriedEquipment, 0, 1))
                { WorldClass = ItemWorldClass.Equipment },
            100310669 => new(1, 1, new(BusinessInventorySpace.CarriedEquipment, 0, 47))
                { WorldClass = ItemWorldClass.Equipment },
            4000 => new(2, 1, new(BusinessInventorySpace.Consumable, 0, 1)),
            _ => new(2, 10, new(BusinessInventorySpace.Consumable, 0, 1)),
        });

    private sealed class NoWearables : IEquipmentSlotDefinitionSource
    {
        public ValueTask<byte?> GetAsync(int itemId, CancellationToken ct) => ValueTask.FromResult<byte?>(null);
    }
}
