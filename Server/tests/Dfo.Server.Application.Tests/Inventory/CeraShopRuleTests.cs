using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class CeraShopRuleTests
{
    [Theory]
    [InlineData(false, false, 100, 60, 0, 0, false)]
    [InlineData(true, false, 100, 0, 40, 20, false)]
    [InlineData(false, true, 50, 100, 10, 0, false)]
    [InlineData(false, true, 100, 0, 0, 0, true)]
    public async Task Payment_uses_the_declared_currency_set_and_reference_priority(bool ceraOnly, bool pointsOnly,
        int price, int cera, int token, int happy, bool refused)
    {
        var content = new CeraShopTestContent();
        content.Products[110000] = content.Products[110000] with
            { BuyOnlyCera = ceraOnly, BuyOnlyCeraPoint = pointsOnly, Price = new(0, 0, price, 0, []) };
        var rules = content.Rules();
        var quote = await rules.QuoteAsync(CeraShopTestContent.Input(CeraShopTestContent.Entry()), 85, CeraShopTestContent.Now, default);
        var result = rules.Plan(quote.Quote!, CeraShopTestContent.EmptyStock(), 0, new(100, 40, 20), 1);
        if (refused) { Assert.Equal(CeraShopRejection.InsufficientFunds, result.Rejection); Assert.Null(result.Plan); }
        else Assert.Equal(new(cera, token, happy), result.Plan!.Balances);
    }

    [Fact]
    public async Task Stacking_splits_across_slots_and_only_returns_final_slot_values()
    {
        var rules = new CeraShopTestContent().Rules();
        var quote = await rules.QuoteAsync(CeraShopTestContent.Input(CeraShopTestContent.Entry(110001), CeraShopTestContent.Entry(110001)),
            85, CeraShopTestContent.Now, default);
        var stock = CeraShopTestContent.EmptyStock();
        stock[InventoryContainerType.Main] = [new(3, StackableItemCore.Create(2, 6000, 9), ItemWorldClass.Interchangeable)];
        var plan = rules.Plan(quote.Quote!, stock, 0, new(100000, 0, 0), 1).Plan!;
        Assert.Equal(99960, plan.Balances.Cera);
        Assert.Equal(new[] { 10, 10, 10, 10, 10, 9 }, plan.Writes.Select(write => write.Core.Value));
        Assert.Equal(9, stock[InventoryContainerType.Main][0].Count);
    }

    [Fact]
    public async Task Full_container_returns_no_plan_even_when_the_first_product_would_fit()
    {
        var rules = new CeraShopTestContent().Rules();
        var quote = await rules.QuoteAsync(CeraShopTestContent.Input(CeraShopTestContent.Entry(110000), CeraShopTestContent.Entry(110001)),
            85, CeraShopTestContent.Now, default);
        var stock = CeraShopTestContent.EmptyStock();
        stock[InventoryContainerType.Main] = new[] { 3, 4, 5, 6, 7, 8, 65, 66 }
            .Select(slot => new ContainerSlotItem(slot, StackableItemCore.Create(2, 3, slot == 3 ? 9 : 10), ItemWorldClass.Interchangeable)).ToArray();
        var result = rules.Plan(quote.Quote!, stock, 0, new(100000, 0, 0), 1);
        Assert.Equal(CeraShopRejection.NoRoom, result.Rejection);
        Assert.Null(result.Plan);
        Assert.Equal(9, stock[InventoryContainerType.Main][0].Count);
    }

    [Theory]
    [InlineData(900003)]
    [InlineData(103945)]
    [InlineData(999999)]
    public async Task Unsupported_later_product_never_produces_a_partial_quote(int product)
    {
        var result = await new CeraShopTestContent().Rules().QuoteAsync(CeraShopTestContent.Input(
            CeraShopTestContent.Entry(), CeraShopTestContent.Entry(product)), 85, CeraShopTestContent.Now, default);
        Assert.Null(result.Quote);
        Assert.NotEqual(CeraShopRejection.None, result.Rejection);
    }

    [Fact]
    public async Task Avatar_ability_and_seven_day_expiration_are_preserved_and_unknown_ability_is_refused()
    {
        var rules = new CeraShopTestContent().Rules();
        var input = CeraShopTestContent.Input(CeraShopTestContent.Entry(120000, 1));
        var quote = await rules.QuoteAsync(input, 85, CeraShopTestContent.Now, default);
        var plan = rules.Plan(quote.Quote!, CeraShopTestContent.EmptyStock(), 0, new(100000, 0, 0), 1).Plan!;
        var write = Assert.Single(plan.Writes);
        Assert.Equal(InventoryContainerType.Avatar, write.Container);
        Assert.Equal(1, write.Core.Attribute);
        Assert.Equal(CeraShopTestContent.Now.AddDays(7).ToUnixTimeSeconds(), write.CreationFacts!.Avatar!.ExpireDate);
        Assert.Equal(30, write.CreationFacts.Avatar.JewelSocket.Length);
        var bad = await rules.QuoteAsync(CeraShopTestContent.Input(CeraShopTestContent.Entry(120000, 7)), 85, CeraShopTestContent.Now, default);
        Assert.Equal(CeraShopRejection.InvalidChoice, bad.Rejection);
    }

    [Fact]
    public async Task Creature_and_artifact_use_distinct_pet_bands_and_creation_facts()
    {
        var rules = new CeraShopTestContent().Rules();
        var quote = await rules.QuoteAsync(CeraShopTestContent.Input(CeraShopTestContent.Entry(105016), CeraShopTestContent.Entry(101075)),
            85, CeraShopTestContent.Now, default);
        var plan = rules.Plan(quote.Quote!, CeraShopTestContent.EmptyStock(), 0, new(100000, 0, 0), 1).Plan!;
        Assert.Equal(new[] { (0, (byte)5), (140, (byte)6) }, plan.Writes.Select(write => (write.SlotIndex, write.Core.ItemKind)));
        Assert.All(plan.Writes, write => Assert.Equal(InventoryContainerType.Pet, write.Container));
        Assert.Equal(new(100, 0, 0, 0, 0, 1, [], 0), plan.Writes[0].CreationFacts!.Creature! with { CreatureText = [] });
    }

    [Fact]
    public async Task Package_selection_is_zero_based_complete_and_cannot_target_an_unrelated_package()
    {
        var content = new CeraShopTestContent();
        content.Items[2683477] = content.Items[2683477] with
            { Components = [new(3, 1, 0)], SelectionGroups = [new([new(2683470, 2, 0), new(2683473, 3, 0)])] };
        var entry = CeraShopTestContent.Entry(102805) with { Selections = [new(2683477, 0, 1)] };
        var rules = content.Rules();
        var result = await rules.QuoteAsync(CeraShopTestContent.Input(entry), 85, CeraShopTestContent.Now, default);
        Assert.Equal(new[] { (3, 1), (2683473, 3) }, result.Quote!.Grants.Select(grant => (grant.Item.ItemId, grant.Count)));
        foreach (var selections in new System.Collections.Immutable.ImmutableArray<CeraShopSelection>[]
            { [], [new(2683477, 0, 2)], [new(999, 0, 0)], [new(2683477, 0, 0), new(2683477, 0, 1)] })
            Assert.Equal(CeraShopRejection.InvalidChoice, (await rules.QuoteAsync(CeraShopTestContent.Input(entry with { Selections = selections }),
                85, CeraShopTestContent.Now, default)).Rejection);
    }

    [Fact]
    public async Task Cycles_overflow_and_purchase_level_refuse_before_any_plan()
    {
        var content = new CeraShopTestContent();
        var rules = content.Rules();
        var input = CeraShopTestContent.Input(CeraShopTestContent.Entry(102805));
        Assert.Equal(CeraShopRejection.PurchaseLevel, (await rules.QuoteAsync(input, 49, CeraShopTestContent.Now, default)).Rejection);
        content.Items[2683477] = content.Items[2683477] with { Components = [new(2683477, 1, 0)] };
        Assert.Equal(CeraShopRejection.UnsupportedProduct, (await rules.QuoteAsync(input, 85, CeraShopTestContent.Now, default)).Rejection);
        content.Items[2683477] = content.Items[2683477] with { Components = [new(3, 2, 0)] };
        content.Products[102805] = content.Products[102805] with { Count = int.MaxValue };
        Assert.Equal(CeraShopRejection.UnsupportedProduct, (await rules.QuoteAsync(input, 85, CeraShopTestContent.Now, default)).Rejection);
    }

    [Fact]
    public void Water_purchase_plans_96400_cera_and_one_item_without_mutating_current_stock()
    {
        var rules = new CeraShopRuleService(null!, null!, new ItemArrivalRuleService());
        var item = new CeraShopItemDefinition(3, "stackable/cash/river_lethe.stk", CeraShopItemKind.Stackable,
            0, false, null, [], [], null, null, []);
        var quote = new CeraShopQuote([110000], [new(110000, 0, 3600, CeraShopPaymentMode.Ordinary)],
            [new(item, new ItemPlacement(2, 100, 65, 120), 1, 0, 0)]);
        var held = new Dictionary<InventoryContainerType, IReadOnlyList<ContainerSlotItem>>
            { [InventoryContainerType.Main] = [] };

        var result = rules.Plan(quote, held, 1000, new(100000, 0, 0), 7700);

        Assert.NotNull(result.Plan);
        Assert.Equal(96400, result.Plan.Balances.Cera);
        var write = Assert.Single(result.Plan.Writes);
        Assert.Equal((3, 1), (write.Core.ItemId, write.Core.Value));
        Assert.Equal(InventoryContainerType.Main, write.Container);
        Assert.Empty(held[InventoryContainerType.Main]);
    }
}
