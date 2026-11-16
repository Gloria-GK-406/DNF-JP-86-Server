using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed partial class OpeningQualityPreparationTests
{
    public static IEnumerable<object?[]> QualityRoutes()
    {
        int[] fixedValues = [999999993, 999999996, 999999999, 999999995, 999999998];
        foreach (var mode in Enum.GetValues<ItemDeliveryMode>())
        {
            for (byte tier = 0; tier < fixedValues.Length; tier++) yield return [tier, fixedValues[tier], mode];
            yield return [null, null, mode];
        }
    }

    [Theory]
    [MemberData(nameof(QualityRoutes))]
    public async Task Prepared_quality_is_preserved_when_delivered_to_inventory_overflow_or_mail(
        byte? tier, int? fixedValue, ItemDeliveryMode mode)
    {
        using var f = Fixture();
        await f.Host.Seed(80, 1001, 2);
        var count = mode == ItemDeliveryMode.InventoryOnly ? 1 : 2;
        var grant = Assert.Single(await f.Host.Services.GetRequiredService<IPackageOpeningRuleService>()
            .PrepareAsync([new(3000, count) { QualityTier = tier }], f.Host.Clock.GetUtcNow(), default));
        var applied = await Execute(f,
            [new ConsumeFromContainerEffect(new(BusinessInventorySpace.Consumable, 15), 1001, 1), new GrantPreparedItemEffect(grant, mode)]);
        var rewards = Assert.Single(applied.Changes<PreparedItemGrantChange>()).Rewards;
        Assert.Equal(count, rewards.Length);
        Assert.All(rewards, reward =>
        {
            Assert.NotNull(reward.Snapshot);
            if (fixedValue is { } expected) Assert.Equal(expected, reward.Snapshot.InstanceValue);
            else
            {
                Assert.True(reward.Snapshot.InstanceValue > 0);
                Assert.False(EquipmentQualitySeeds.IsFixed(reward.Snapshot.InstanceValue));
            }
        });
        var inBag = mode == ItemDeliveryMode.MailOnly ? 0 : 1;
        Assert.Equal(inBag, f.Host.Count(3000));
        Assert.Equal(count - inBag, f.Host.Scalar("SELECT COUNT(*) FROM mailbox_attachments WHERE item_template_id=3000"));
        Assert.Equal(1, f.Host.Count(1001));
    }

    private static OpeningFixture Fixture(Action<IServiceCollection>? configure = null)
    {
        var f = new OpeningFixture(configure);
        f.Content.Items[3000] = f.Content.Items[3000] with
        {
            Placement = f.Content.Items[3000].Placement with { Destination = new(BusinessInventorySpace.CarriedEquipment, 0, 0) }
        };
        return f;
    }

    private static Task<ApplyEffectsResult> Execute(OpeningFixture f, IReadOnlyList<DomainEffect> plan) =>
        TestGameplay.RunAsync(f.Host.Services, async () =>
        {
            await using var transaction = ((DomainTransactionScope)f.Host.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
            var applied = await Dfo.Server.Application.Tests.Support.TestEffectFootprints.Run(f.Host.Session, footprint =>
                f.Host.Services.GetRequiredService<IApplyEffectsDomainService>().ApplyAsync(
                    new(f.Host.Session.AccountId, f.Host.Session.CharacterId, f.Host.Clock.GetUtcNow(), footprint, f.Host.Session), plan, default));
            await transaction.CommitAsync(default);
            return applied;
        });
}
