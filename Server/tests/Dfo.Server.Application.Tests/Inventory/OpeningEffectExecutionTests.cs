using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class OpeningEffectExecutionTests
{
    [Fact]
    public async Task Inventory_only_prepared_destination_refuses_before_any_payment()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = new OpeningFixture();
            f.Content.Items[3000] = f.Content.Items[3000] with { Placement = f.Content.Items[3000].Placement with
                { Destination = new(BusinessInventorySpace.CarriedEquipment, 0, 0) } };
            await f.Host.Seed(80, 1001, 1);
            await f.Host.SeedCore(9, StackableItemCore.Create(1, 3000, 1234));
            var grants = await f.Host.Services.GetRequiredService<IPackageOpeningRuleService>().PrepareAsync([new(3000, 1)], f.Host.Clock.GetUtcNow(), default);
            await using var tx = Scope(f).Begin();
            var refusal = await Assert.ThrowsAsync<InventoryEffectException>(() => Apply(f,
                [new SpendGoldEffect(100), new ConsumeFromContainerEffect(new(BusinessInventorySpace.Consumable, 15), 1001, 1),
                    new SetOpeningPremiumEffect((PremiumServiceType)31, 0, f.Host.Clock.GetUtcNow().AddDays(1).ToUnixTimeSeconds(), new(30, 1)),
                    new GrantPreparedItemEffect(grants.Single(), DeliveryMode: ItemDeliveryMode.InventoryOnly)]).AsTask());
            Assert.Equal(InventoryEffectRejection.NoRoom, refusal.Rejection);
            await tx.CommitAsync(default);
            Assert.Equal(100000, f.Host.Gold); Assert.Equal(1, f.Host.Count(1001));
            Assert.Equal(0, f.Host.Scalar("SELECT count(*) FROM account_premiums WHERE account_id=1 AND premium_type=31"));
        });
    }

    [Fact]
    public async Task Repeated_premium_effects_use_the_prior_decided_expiry_and_apply_without_initial_state_rejection()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = new OpeningFixture();
            var at = f.Host.Clock.GetUtcNow();
            var first = at.AddDays(1).ToUnixTimeSeconds();
            var final = at.AddDays(3).ToUnixTimeSeconds();
            await using var tx = Scope(f).Begin();
            var applied = await Apply(f, [new SetOpeningPremiumEffect((PremiumServiceType)31, 0, first, new(30, 1)),
                new SetOpeningPremiumEffect((PremiumServiceType)31, first, final, new(30, 2))]);
            Assert.Equal([first, final], applied.Changes<OpeningPremiumChange>().Select(x => x.Premium.ExpiresAtUnixSeconds));
            await tx.CommitAsync(default);
            Assert.Equal(final, f.Host.Scalar("SELECT end_time FROM account_premiums WHERE account_id=1 AND premium_type=31"));
        });
    }

    [Fact]
    public async Task Virtual_reward_overflow_accumulates_in_the_same_preview_and_writes_nothing()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = new OpeningFixture();
            await f.Host.Seed(80, 1001, 1);
            await f.Host.SeedCore(1, StackableItemCore.Create(0, 1, int.MaxValue - 1));
            await using var tx = Scope(f).Begin();
            var refusal = await Assert.ThrowsAsync<InventoryEffectException>(() => Apply(f,
                [new ConsumeFromContainerEffect(new(BusinessInventorySpace.Consumable, 15), 1001, 1),
                    new GrantOpeningVirtualEffect(1, 1, 0, new(42, 1)), new GrantOpeningVirtualEffect(1, 1, 0, new(42, 1))]).AsTask());
            Assert.Equal(InventoryEffectRejection.NoRoom, refusal.Rejection);
            await tx.CommitAsync(default);
            Assert.Equal(1, f.Host.Count(1001)); Assert.Equal(int.MaxValue - 1, f.Host.Count(1));
        });
    }

    private static DomainTransactionScope Scope(OpeningFixture f) => (DomainTransactionScope)f.Host.Services.GetRequiredService<IDomainTransactionAccessor>();
    private static ValueTask<ApplyEffectsResult> Apply(OpeningFixture f, IReadOnlyList<DomainEffect> effects) =>
        f.Host.Services.GetRequiredService<IApplyEffectsDomainService>().ApplyAsync(
            new(f.Host.Session.AccountId, f.Host.Session.CharacterId, f.Host.Clock.GetUtcNow(), f.Host.Session), effects, default);
}
