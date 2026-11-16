using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed partial class OpeningQualityPreparationTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData((byte)0, 999999993)]
    [InlineData((byte)1, 999999996)]
    [InlineData((byte)2, 999999999)]
    [InlineData((byte)3, 999999995)]
    [InlineData((byte)4, 999999998)]
    [InlineData(null, null)]
    public async Task Preparation_resolves_fixed_quality_and_consumes_the_declaration(byte? tier, int? value)
    {
        var input = new OpeningReward(3000, 2) { QualityTier = tier };
        var grant = Assert.Single(await Rules().PrepareAsync([input], DateTimeOffset.UtcNow, default));
        Assert.Equal(value, grant.FixedInstanceValue);
        Assert.Null(grant.Reward.QualityTier);
        Assert.Equal(tier, input.QualityTier);
        Assert.Equal(2, grant.Reward.Count);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(3)]
    [InlineData(39032)]
    [InlineData(63022)]
    public async Task Non_equipment_discards_equipment_modifiers_before_resolving_quality(int id)
    {
        var grant = Assert.Single(await Rules().PrepareAsync(
            [new(id, 1) { QualityTier = 255, Upgrade = 255, Forging = 255 }], DateTimeOffset.UtcNow, default));
        Assert.Null(grant.FixedInstanceValue);
        Assert.Null(grant.Reward.QualityTier);
        Assert.Equal((0, 0), ((int)grant.Reward.Upgrade, (int)grant.Reward.Forging));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task An_undeclared_equipment_quality_is_rejected_during_preparation()
    {
        var error = await Assert.ThrowsAsync<OpeningRejectedException>(async () =>
            await Rules().PrepareAsync([new(3000, 1) { QualityTier = 5 }], DateTimeOffset.UtcNow, default));
        Assert.Equal(OpeningRejection.UnsupportedContent, error.Reason);
    }

    [Theory]
    [InlineData(null, ItemDeliveryMode.InventoryOnly)]
    [InlineData(null, ItemDeliveryMode.InventoryThenMail)]
    [InlineData(null, ItemDeliveryMode.MailOnly)]
    [InlineData((byte)4, ItemDeliveryMode.InventoryOnly)]
    [InlineData((byte)4, ItemDeliveryMode.InventoryThenMail)]
    [InlineData((byte)4, ItemDeliveryMode.MailOnly)]
    public async Task Preview_never_draws_and_execution_draws_once_per_unspecified_instance(byte? tier, ItemDeliveryMode mode)
    {
        var seeds = new TrackingSeeds();
        using var f = Fixture(s => s.AddSingleton<IEquipmentQualitySeedSource>(seeds));
        await f.Host.Seed(80, 1001, 2);
        var count = mode == ItemDeliveryMode.InventoryOnly ? 1 : 2;
        var grant = Assert.Single(await f.Host.Services.GetRequiredService<IPackageOpeningRuleService>()
            .PrepareAsync([new(3000, count) { QualityTier = tier }], f.Host.Clock.GetUtcNow(), default));
        DomainEffect[] plan = [new ConsumeFromContainerEffect(new(BusinessInventorySpace.Consumable, 15), 1001, 1),
            new GrantPreparedItemEffect(grant, mode)];
        Assert.Equal(0, seeds.Calls);
        Assert.Equal(InventoryEffectRejection.None, (await Preview(f, plan)).Rejection);
        Assert.Equal(InventoryEffectRejection.None, (await Preview(f, plan)).Rejection);
        Assert.Equal(0, seeds.Calls);
        Assert.Equal(2, f.Host.Count(1001));
        var rewards = Assert.Single((await Execute(f, plan)).Changes<PreparedItemGrantChange>()).Rewards;
        Assert.Equal(tier is null ? count : 0, seeds.Calls);
        Assert.Equal(tier is null ? Enumerable.Range(1, count).Select(i => 123000 + i) : Enumerable.Repeat(999999998, count),
            rewards.Select(reward => reward.Snapshot!.InstanceValue).Order());
    }

    [Theory]
    [InlineData(null)]
    [InlineData((byte)4)]
    public async Task A_refused_preview_neither_consumes_the_box_nor_draws_quality(byte? tier)
    {
        var seeds = new TrackingSeeds();
        using var f = Fixture(s => s.AddSingleton<IEquipmentQualitySeedSource>(seeds));
        await f.Host.Seed(80, 1001, 2);
        var grant = Assert.Single(await f.Host.Services.GetRequiredService<IPackageOpeningRuleService>()
            .PrepareAsync([new(3000, 2) { QualityTier = tier }], f.Host.Clock.GetUtcNow(), default));
        var error = await Assert.ThrowsAsync<InventoryEffectException>(() => Execute(f,
            [new ConsumeFromContainerEffect(new(BusinessInventorySpace.Consumable, 15), 1001, 1),
                new GrantPreparedItemEffect(grant, ItemDeliveryMode.InventoryOnly)]));
        Assert.Equal(InventoryEffectRejection.NoRoom, error.Rejection);
        Assert.Equal(0, seeds.Calls);
        Assert.Equal(2, f.Host.Count(1001));
        Assert.Equal(0, f.Host.Count(3000));
    }

    [Theory]
    [InlineData(null)]
    [InlineData((byte)4)]
    public async Task Mail_failure_rolls_back_the_box_and_instances_without_rewinding_the_random_source(byte? tier)
    {
        var seeds = new TrackingSeeds();
        using var f = Fixture(s => s.AddSingleton<IEquipmentQualitySeedSource>(seeds));
        await f.Host.Seed(80, 1001, 2);
        var grant = Assert.Single(await f.Host.Services.GetRequiredService<IPackageOpeningRuleService>()
            .PrepareAsync([new(3000, 2) { QualityTier = tier }], f.Host.Clock.GetUtcNow(), default));
        DomainEffect[] plan = [new ConsumeFromContainerEffect(new(BusinessInventorySpace.Consumable, 15), 1001, 1),
            new GrantPreparedItemEffect(grant, ItemDeliveryMode.InventoryThenMail)];
        var before = f.Host.Scalar("SELECT COUNT(*) FROM items");
        f.Host.Sql("CREATE TRIGGER fail_quality_mail BEFORE INSERT ON mailbox_attachments BEGIN SELECT RAISE(ABORT,'forced quality failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Execute(f, plan));
        Assert.Equal(tier is null ? 2 : 0, seeds.Calls);
        Assert.Equal(2, f.Host.Count(1001));
        Assert.Equal(0, f.Host.Count(3000));
        Assert.Equal(before, f.Host.Scalar("SELECT COUNT(*) FROM items"));
        Assert.Equal(0, f.Host.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
        Assert.Equal(0, f.Host.Scalar("SELECT COUNT(*) FROM mailbox_attachments"));
        f.Host.Sql("DROP TRIGGER fail_quality_mail");
        var rewards = Assert.Single((await Execute(f, plan)).Changes<PreparedItemGrantChange>()).Rewards;
        Assert.Equal(tier is null ? 4 : 0, seeds.Calls);
        Assert.Equal(tier is null ? [123003, 123004] : new[] { 999999998, 999999998 },
            rewards.Select(reward => reward.Snapshot!.InstanceValue).Order());
        Assert.Equal(1, f.Host.Count(1001));
    }

    private sealed class TrackingSeeds : IEquipmentQualitySeedSource
    {
        public int Calls { get; private set; }
        public int Next() => 123000 + ++Calls;
    }

    private static PackageOpeningRuleService Rules() => new(new OpeningContent(),
        TestRequired.Absent<IItemPlacementRuleService>(), TestRequired.NoExpiration, TestRequired.Absent<IServiceItemRewardRuleService>());

    private static Task<InventoryEffectPreviewResult> Preview(OpeningFixture f, IReadOnlyList<DomainEffect> plan) =>
        TestGameplay.RunAsync(f.Host.Services, async () =>
        {
            await using var transaction = ((DomainTransactionScope)f.Host.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
            return await Dfo.Server.Application.Tests.Support.TestEffectFootprints.Run(f.Host.Session, footprint =>
                f.Host.Services.GetRequiredService<IInventoryEffectPreflightDomainService>().PreviewAsync(
                    new(f.Host.Session.AccountId, f.Host.Session.CharacterId, f.Host.Clock.GetUtcNow(), footprint, f.Host.Session), plan, default));
        });
}
