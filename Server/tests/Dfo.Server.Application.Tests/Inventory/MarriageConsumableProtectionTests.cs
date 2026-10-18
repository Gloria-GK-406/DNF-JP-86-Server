using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class MarriageConsumableProtectionTests
{
    [Theory]
    [InlineData(7001, "[love point coupon]")]
    [InlineData(7002, "[upgrade wedding ring]")]
    public async Task Unsupported_marriage_actions_are_refused_without_effects(int itemId, string action)
    {
        using var services = Provider(itemId, action);
        var plans = services.GetRequiredService<IItemUseEffectRuleService>();
        var rule = await plans.RuleForAsync(itemId, default);

        var result = plans.Build(rule, new(BusinessInventorySpace.Consumable, 0), Item(itemId),
            Candidate, null, DateTimeOffset.UtcNow);

        Assert.Equal(ItemUsability.NotAUsableItem, result.Status);
        Assert.Empty(result.Effects);
    }

    [Fact]
    public async Task An_unrecognized_action_keeps_the_ordinary_item_use_path()
    {
        const int itemId = 7003;
        using var services = Provider(itemId, "[future action]");
        var plans = services.GetRequiredService<IItemUseEffectRuleService>();
        var rule = await plans.RuleForAsync(itemId, default);

        var result = plans.Build(rule, new(BusinessInventorySpace.Consumable, 0), Item(itemId),
            Candidate, null, DateTimeOffset.UtcNow);

        Assert.Equal(ItemUsability.Allowed, result.Status);
        Assert.Collection(result.Effects,
            effect => Assert.Equal(
                new ConsumeFromContainerEffect(new(BusinessInventorySpace.Consumable, 0), itemId, 1),
                effect));
    }

    private static readonly ItemUseCandidate Candidate = new(
        60, QuestJobRequirement.Swordman, ItemUsePlace.Village, false);

    private static ServiceProvider Provider(int itemId, string action)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IItemUseRuleDefinitionSource>(
            new FakeItemUseRules((itemId,
                ItemUseRules.Potion(cooldownMilliseconds: 0) with { Action = action })));
        new ApplicationModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    private static ItemReference Item(int itemId) => TestItemReferences.FromCore(0,
        StackableItemCore.Create(2, itemId, 2), ItemWorldClass.Interchangeable);

}
