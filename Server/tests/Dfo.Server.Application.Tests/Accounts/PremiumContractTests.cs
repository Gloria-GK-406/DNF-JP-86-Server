using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Accounts;

public sealed class PremiumContractTests
{
    [Fact]
    public async Task Boss_equipment_bonus_counts_only_valid_holders_at_generation_time()
    {
        await using var f = new LearningFixture(configure: services =>
            services.AddSingleton<IPremiumServiceDefinitionSource>(new Levels()));
        await f.SeedAsync();
        var service = f.Services.GetRequiredService<IPremiumServiceDomainService>();
        Assert.Equal(0, await service.GetBossEquipmentDropBonusPercentAsync([new(1)], default));
        f.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,84,4000000000)");
        Assert.Equal(20, await service.GetBossEquipmentDropBonusPercentAsync([new(1),new(2)], default));
        // Count participants, even when more than one character belongs to one account.
        Assert.Equal(23, await service.GetBossEquipmentDropBonusPercentAsync([new(1),new(1)], default));
        Assert.Equal(27, await service.GetBossEquipmentDropBonusPercentAsync([new(1),new(1),new(1)], default));
        Assert.Equal(30, await service.GetBossEquipmentDropBonusPercentAsync([new(1),new(1),new(1),new(1)], default));
        f.Sql("UPDATE account_premiums SET end_time=1");
        Assert.Equal(0, await service.GetBossEquipmentDropBonusPercentAsync([new(1)], default));
    }

    [Fact]
    public async Task Growth_rate_is_owned_by_account_service_and_uses_rules_only_while_active()
    {
        await using var f = new LearningFixture(configure: services =>
            services.AddSingleton<IPremiumServiceDefinitionSource>(new Levels()));
        await f.SeedAsync();
        var account = f.Services.GetRequiredService<IPremiumServiceDomainService>().For(new(1));
        Assert.Equal(0, await account.GetExperienceBonusRateAsync(default));
        f.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,84,4000000000)");
        Assert.Equal(.2, await account.GetExperienceBonusRateAsync(default));
        Assert.Equal(0, await f.Services.GetRequiredService<IPremiumServiceDomainService>().For(new(2)).GetExperienceBonusRateAsync(default));
        f.Sql("UPDATE account_premiums SET end_time=0");
        Assert.Equal(0, await account.GetExperienceBonusRateAsync(default));
        f.Sql("UPDATE account_premiums SET end_time=1");
        Assert.Equal(0, await account.GetExperienceBonusRateAsync(default));
    }

    [Fact]
    public async Task Extension_preserves_remaining_time_is_visible_in_transaction_and_rolls_back()
    {
        await using var f = new LearningFixture(); await f.SeedAsync();
        var account = f.Services.GetRequiredService<IPremiumServiceDomainService>().For(new(1));
        Assert.False(await account.IsActiveAsync(PremiumServiceType.ExpertSkillContract, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => account.ExtendAsync(PremiumServiceType.ExpertSkillContract, TimeSpan.FromMinutes(60), default).AsTask());
        await using (var tx = f.Scope.Begin())
        {
            var first = await account.ExtendAsync(PremiumServiceType.ExpertSkillContract, TimeSpan.FromHours(1), default);
            var next = await account.ExtendAsync(PremiumServiceType.ExpertSkillContract, TimeSpan.FromHours(1), default);
            Assert.Equal(first.ExpiresAtUnixSeconds + 3600, next.ExpiresAtUnixSeconds);
            Assert.True(await account.HasBenefitAsync(PremiumBenefit.LearnSkillsEarly, default));
            Assert.False(await account.HasBenefitAsync(PremiumBenefit.EquipItemsEarly, default));
        }
        Assert.False(await account.IsActiveAsync(PremiumServiceType.ExpertSkillContract, default));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM account_premiums"));
    }

    [Fact]
    public async Task Expired_zero_and_other_accounts_do_not_grant_benefits_and_commit_survives_reload()
    {
        await using var f = new LearningFixture(); await f.SeedAsync();
        f.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,27,0),(1,22,1),(2,27,4000000000)");
        var account = f.Services.GetRequiredService<IPremiumServiceDomainService>().For(new(1));
        Assert.False(await account.HasBenefitAsync(PremiumBenefit.LearnSkillsEarly, default));
        Assert.False(await account.HasBenefitAsync(PremiumBenefit.EquipItemsEarly, default));
        await using (var tx = f.Scope.Begin())
        {
            var expiry = await account.ExtendAsync(PremiumServiceType.ExpertSkillContract, TimeSpan.FromMinutes(60), default);
            Assert.InRange(expiry.ExpiresAtUnixSeconds - DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 3598, 3600);
            await tx.CommitAsync(default);
        }
        await using var reload = new LearningFixture(f.Path);
        Assert.True(await reload.Services.GetRequiredService<IPremiumServiceDomainService>().For(new(1))
            .HasBenefitAsync(PremiumBenefit.LearnSkillsEarly, default));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(4000000000, true)]
    public async Task Expert_contract_only_raises_learning_limit_not_point_budget(long expiry, bool allowed)
    {
        await using var f = new LearningFixture(configure: services => services.AddSingleton<IPremiumServiceDefinitionSource>(new Levels()));
        await f.SeedAsync();
        f.Sql($"INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,27,{expiry})");
        var result = await f.LearnAsync([new(5, 0, 1)]);
        Assert.Equal(allowed, result.Receipt is not null);
        if (allowed) Assert.Equal(1005 - 30 - 20, result.Receipt!.Ledger.For(new(0)).Sp);
    }

    [Theory]
    [InlineData(90, ItemWorldClass.Equipment, true)]
    [InlineData(95, ItemWorldClass.Avatar, true)]
    [InlineData(96, ItemWorldClass.Equipment, false)]
    [InlineData(90, ItemWorldClass.Creature, false)]
    public async Task Over_equipment_checks_current_term_and_only_applies_to_equipment_and_avatars(int required, ItemWorldClass kind, bool allowed)
    {
        await using var f = new LearningFixture(); await f.SeedAsync();
        var eligibility = new EquipmentWearDomainService(f.Services.GetRequiredService<ICharacterTable>(),
            new Requirement(required), new Placement(kind), f.Services.GetRequiredService<IPremiumServiceDomainService>(),
            new PremiumServiceRuleService(new Levels()));
        Assert.False(await eligibility.CanWearAsync(new(71), 1, default));
        f.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,22,4000000000)");
        Assert.Equal(allowed, await eligibility.CanWearAsync(new(71), 1, default));
        f.Sql("UPDATE account_premiums SET end_time=1");
        Assert.False(await eligibility.CanWearAsync(new(71), 1, default));
        f.Sql("UPDATE characters SET level=100 WHERE character_id=71");
        Assert.True(await eligibility.CanWearAsync(new(71), 1, default));
    }

    private sealed class Levels : IPremiumServiceDefinitionSource
    {
        public int ReadFreeWeaknessDailyLimit() => 10;
        public int ReadFreeGoldCardDailyLimit() => 10;
        public int ReadDungeonBuffDailyLimit() => 10;
        public int ReadAutoRepairDailyLimit() => 6;
        public IReadOnlyList<int> ReadQuestHelperMinimumDropRates() => new[] { 100, 70, 30 };
        public DoublePotRules ReadDoublePotRules() => new(8, new HashSet<int>());
        public IReadOnlyList<int> ReadGrowthBossEquipmentBonusPercents() => new[] {20, 23, 27, 30};
        public double ReadBlackDiamondExperienceBonusRate() => .05;
        public double ReadGrowthExperienceBonusRate() => 0.2;
        public PremiumContractLevels ReadContractLevels() => new(5, 10);
    }
    private sealed class Requirement(int level) : IEquipmentWearRuleService
    {
        public Dfo.Server.Data.Inventory.ItemSnapshot UnsealForEquip(Dfo.Server.Data.Inventory.ItemSnapshot item) => item;

        public ValueTask<int?> MinimumLevelAsync(int itemId, CancellationToken cancellationToken) => ValueTask.FromResult<int?>(level);
    }
    private sealed class Placement(ItemWorldClass kind) : IItemPlacementRuleService
    {
        public ValueTask<ItemPlacement?> PlacementForAsync(int itemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BusinessSlot?> WornSlotForAsync(int itemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ItemWorldClass> ClassifyAsync(int itemId, CancellationToken cancellationToken) => ValueTask.FromResult(kind);
        public ValueTask<bool> IsEquipmentAsync(int itemId, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }
}
