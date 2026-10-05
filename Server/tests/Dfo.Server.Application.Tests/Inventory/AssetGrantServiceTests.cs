using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class AssetGrantServiceTests
{
    [Fact]
    public async Task Zero_gold_reward_still_rejects_a_missing_character()
    {
        using var fixture = new CeraShopFixture();
        var effects = fixture.Host.Services.GetRequiredService<IApplyEffectsDomainService>();
        await using var tx = Scope(fixture).Begin();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await effects.ApplyAsync(new(new(1), new(999), fixture.Host.Clock.GetUtcNow()),
                [new SetQuestRewardGoldEffect(0, 0)], default));
    }

    [Theory]
    [InlineData(AccountCurrency.Cera)]
    [InlineData(AccountCurrency.TokenCera)]
    [InlineData(AccountCurrency.HappyTokenCera)]
    public async Task Wallet_add_and_spend_preserve_other_currencies_and_reject_stale_decisions(AccountCurrency currency)
    {
        using var fixture = new CeraShopFixture();
        var wallet = fixture.Host.Services.GetRequiredService<IAccountWalletDomainService>();
        var before = (await wallet.ReadAsync(new(1), default))!;
        await using (var tx = Scope(fixture).Begin())
        {
            var added = await wallet.AddAsync(new(1), currency, 90, default);
            Assert.Equal(before, added.Before);
            Assert.False(await wallet.TryApplyAsync(new(1), before, before, default));
            var spent = await wallet.SpendAsync(new(1), currency, 90, default);
            Assert.Equal(added.After, spent.Before);
            Assert.Equal(before, spent.After);
            await tx.CommitAsync(default);
        }
        Assert.Equal(before, await wallet.ReadAsync(new(1), default));
    }

    [Fact]
    public async Task Wallet_rejects_invalid_amounts_limits_missing_accounts_and_writes_outside_transactions()
    {
        using var fixture = new CeraShopFixture();
        var wallet = fixture.Host.Services.GetRequiredService<IAccountWalletDomainService>();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await wallet.AddAsync(new(1), AccountCurrency.Cera, 1, default));
        await using var tx = Scope(fixture).Begin();
        Assert.Equal(WalletRejection.InvalidAmount, (await Assert.ThrowsAsync<WalletException>(async () =>
            await wallet.AddAsync(new(1), AccountCurrency.Cera, 0, default))).Rejection);
        Assert.Equal(WalletRejection.BalanceLimit, (await Assert.ThrowsAsync<WalletException>(async () =>
            await wallet.AddAsync(new(1), AccountCurrency.Cera, int.MaxValue, default))).Rejection);
        Assert.Equal(WalletRejection.InsufficientFunds, (await Assert.ThrowsAsync<WalletException>(async () =>
            await wallet.SpendAsync(new(1), AccountCurrency.TokenCera, 1, default))).Rejection);
        Assert.Equal(WalletRejection.MissingAccount, (await Assert.ThrowsAsync<WalletException>(async () =>
            await wallet.AddAsync(new(999), AccountCurrency.Cera, 1, default))).Rejection);
        Assert.Equal(new AccountCeraBalances(100000, 0, 0), await wallet.ReadAsync(new(1), default));
    }

    [Fact]
    public async Task A_later_asset_failure_rolls_back_wallet_and_warehouse_together()
    {
        using var fixture = new CeraShopFixture();
        var wallet = fixture.Host.Services.GetRequiredService<IAccountWalletDomainService>();
        var levels = fixture.Host.Services.GetRequiredService<IInventoryLevelDomainService>();
        var cargo = ContainerId.ForCharacter(new(1), InventoryContainerType.PersonalCargo);
        await Assert.ThrowsAsync<InventoryExpansionException>(async () =>
        {
            await using var tx = Scope(fixture).Begin();
            await wallet.AddAsync(new(1), AccountCurrency.Cera, 1000, default);
            await levels.SetLevelAsync(cargo, 24, default);
            await levels.SetLevelAsync(cargo, 23, default);
            await tx.CommitAsync(default);
        });
        Assert.Equal(100000, fixture.Cera);
        Assert.Equal(8, await levels.GetLevelAsync(cargo, default));
    }

    [Fact]
    public async Task Warehouse_can_be_set_offline_but_rejects_occupied_slots_and_stale_levels()
    {
        using var fixture = new CeraShopFixture();
        var levels = fixture.Host.Services.GetRequiredService<IInventoryLevelDomainService>();
        var cargo = ContainerId.ForCharacter(new(1), InventoryContainerType.PersonalCargo);
        var grants = fixture.Host.Services.GetRequiredService<IItemGrantDomainService>();
        fixture.Host.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(new(1));
        await using (var tx = Scope(fixture).Begin())
        {
            await levels.SetLevelAsync(cargo, 24, default);
            await levels.SetLevelAsync(cargo, 8, default);
            await levels.SetLevelAsync(cargo, 24, default);
            await grants.PutNewAsync(cargo, 20, new ItemSnapshot { ItemId = 3, LegacyKind = 2,
                Type = ItemTraits.Consumable, IsPile = true, Count = 1 }, default);
            await tx.CommitAsync(default);
        }
        await using (var tx = Scope(fixture).Begin())
        {
            Assert.Equal(InventoryExpansionRejection.StateChanged, (await Assert.ThrowsAsync<InventoryExpansionException>(async () =>
                await levels.SetLevelAsync(cargo, 40, default, expectedLevel: 8))).Rejection);
            Assert.Equal(InventoryExpansionRejection.OccupiedSlots, (await Assert.ThrowsAsync<InventoryExpansionException>(async () =>
                await levels.SetLevelAsync(cargo, 8, default))).Rejection);
        }
        Assert.Equal(24, await levels.GetLevelAsync(cargo, default));
    }

    [PvfFact]
    public async Task ExperienceSetterClampsBothBoundsDerivesLevelsAndNeverCreatesCapsuleRewards()
    {
        using var fixture = new CeraShopFixture(useArchive: true);
        fixture.Host.Sql("UPDATE characters SET job=0,level=1,exp=0 WHERE character_id=1");
        var character = fixture.Host.Services.GetRequiredService<ICharacterDomainService>().Get(new(1));
        var scale = fixture.Host.Services.GetRequiredService<IExperienceRuleService>().CharacterScale();
        await using (var tx = Scope(fixture).Begin())
        {
            Assert.Equal(new CharacterProgressionState(86, scale.MaximumExperience), await character.SetExpAsync(long.MaxValue, default));
            Assert.Equal(0, fixture.Host.Scalar("SELECT growth_capsule_exp FROM accounts WHERE account_id=1"));
            await character.SetLevelAsync(30, default);
            Assert.Equal(29, (await character.SetExpAsync(scale.ExperienceForLevel(30) - 1, default)).Level);
            Assert.Equal(new CharacterProgressionState(1, 0), await character.SetExpAsync(-1, default));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await character.SetLevelAsync(87, default));
            await tx.CommitAsync(default);
        }
        Assert.Equal(new CharacterProgressionState(1, 0), await character.ProgressAsync(default));
    }

    [PvfFact]
    public async Task Level_setter_works_offline_in_both_directions_and_projects_current_stats_and_skills()
    {
        using var fixture = new CeraShopFixture(useArchive: true, configure: services =>
            services.AddSingleton<ICharacterSkillPointDomainService, CharacterSkillPointDomainService>());
        fixture.Host.Sql("UPDATE characters SET job=0,level=1,exp=0 WHERE character_id=1");
        fixture.Host.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(new(1));
        var levels = fixture.Host.Services.GetRequiredService<ICharacterDomainService>().Get(new(1));
        var character = fixture.Host.Services.GetRequiredService<ICharacterDomainService>().Get(new(1));
        var initial = await character.BaseStatsAsync(default);
        var thresholds = fixture.Host.Services.GetRequiredService<IExperienceRuleService>().Progression().LevelThresholds;
        await using (var tx = Scope(fixture).Begin())
        {
            Assert.Equal(new CharacterProgressionState(30, thresholds[28]), await levels.SetLevelAsync(30, default));
            Assert.Equal(30, (await character.ProgressAsync(default))!.Level);
            Assert.NotEqual(initial, await character.BaseStatsAsync(default));
            await tx.CommitAsync(default);
        }
        await using (var tx = Scope(fixture).Begin())
        {
            Assert.Equal(new CharacterProgressionState(1, 0), await levels.SetLevelAsync(1, default));
            Assert.Equal(initial, await character.BaseStatsAsync(default));
            var ledger = (await character.SkillPointLedgerAsync(default))!;
            Assert.True(ledger.IsComplete);
            Assert.True(ledger.For(new(0)).Sp >= 0);
            Assert.True(ledger.For(new(1)).Sp >= 0);
            await tx.CommitAsync(default);
        }
        Assert.Equal(new CharacterProgressionState(1, 0), await character.ProgressAsync(default));
    }

    [PvfFact]
    public async Task Contract_add_days_restarts_expired_contracts_and_extends_active_contracts()
    {
        using var fixture = new CeraShopFixture(useArchive: true);
        var contracts = fixture.Host.Services.GetRequiredService<IPremiumServiceDomainService>().For(new(1));
        fixture.Host.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,97,1)");
        var now = fixture.Host.Clock.GetUtcNow().ToUnixTimeSeconds();
        await using (var tx = Scope(fixture).Begin())
        {
            Assert.Equal(now + 86400, (await contracts.AddDaysAsync(PremiumServiceType.CrystalContract, 1, default)).ExpiresAtUnixSeconds);
            Assert.Equal(now + 3 * 86400, (await contracts.AddDaysAsync(PremiumServiceType.CrystalContract, 2, default)).ExpiresAtUnixSeconds);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await contracts.AddDaysAsync(PremiumServiceType.CrystalContract, 0, default));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await contracts.AddDaysAsync(PremiumServiceType.BlackDiamond, 1, default));
            await tx.CommitAsync(default);
        }
        Assert.Equal(now + 3 * 86400, fixture.Host.Scalar("SELECT end_time FROM account_premiums WHERE premium_type=97"));
    }

    private static DomainTransactionScope Scope(CeraShopFixture fixture) =>
        (DomainTransactionScope)fixture.Host.Services.GetRequiredService<IDomainTransactionAccessor>();

    [Fact]
    public async Task Account_warehouse_rejects_gold_above_the_target_tier_limit()
    {
        using var fixture = new CeraShopFixture(configure: services => services.AddSingleton<IInventoryExpansionDefinitionSource>(new CargoTiers()));
        var levels = fixture.Host.Services.GetRequiredService<IInventoryLevelDomainService>();
        var cargo = ContainerId.ForAccountCargo(new(1));
        await using (var tx = Scope(fixture).Begin())
        {
            await levels.SetLevelAsync(cargo, 8, default);
            await tx.CommitAsync(default);
        }
        fixture.Host.Sql("UPDATE account_cargo_state SET value32=101 WHERE account_id=1");
        await using (var tx = Scope(fixture).Begin())
        {
            Assert.Equal(InventoryExpansionRejection.GoldLimit, (await Assert.ThrowsAsync<InventoryExpansionException>(async () =>
                await levels.SetLevelAsync(cargo, 1, default))).Rejection);
            await Assert.ThrowsAsync<KeyNotFoundException>(async () => await levels.SetLevelAsync(ContainerId.ForAccountCargo(new(999)), 8, default));
        }
        Assert.Equal(8, await levels.GetLevelAsync(cargo, default));
        fixture.Host.Sql("UPDATE account_cargo_state SET value32=100 WHERE account_id=1");
        await using (var tx = Scope(fixture).Begin())
        {
            await levels.SetLevelAsync(cargo, 1, default);
            await tx.CommitAsync(default);
        }
        Assert.Equal(1, await levels.GetLevelAsync(cargo, default));
    }

    private sealed class CargoTiers : IInventoryExpansionDefinitionSource
    {
        public ValueTask<AccountCargoDefinition> AccountCargoAsync(CancellationToken ct) =>
            ValueTask.FromResult(new AccountCargoDefinition(0, [new(1, 100, 0, 0, 0, 0), new(8, 1000, 0, 0, 0, 0)]));
        public ValueTask<InventoryExpansionGrant?> ItemAsync(int itemId, CancellationToken ct) => ValueTask.FromResult<InventoryExpansionGrant?>(null);
        public ValueTask<InventoryExpansionGrant?> ProductAsync(int productId, int itemId, CancellationToken ct) => ValueTask.FromResult<InventoryExpansionGrant?>(null);
    }

    [PvfFact]
    public async Task SetLevel_respects_active_expert_contract_and_removes_unavailable_learning_after_expiry()
    {
        using var fixture = new CeraShopFixture(useArchive: true);
        fixture.Host.Sql("UPDATE characters SET job=0,level=10,bonus_sp=10000 WHERE character_id=1");
        var contracts = fixture.Host.Services.GetRequiredService<IPremiumServiceDomainService>().For(new(1));
        var rules = fixture.Host.Services.GetRequiredService<ICharacterSkillRuleService>();
        var allowance = fixture.Host.Services.GetRequiredService<IPremiumServiceRuleService>().LevelAllowance(PremiumBenefit.LearnSkillsEarly);
        var career = new CharacterCareer(0);
        var candidate = rules.GetAll(0).First(r => r.GetFreeLevel(career, 1) == 0 && (r.GetMaxLevel(career, 1) ?? 0) == 0
            && r.CanLearn(career, 1 + allowance, 1, new Dictionary<int, int>()));
        var skills = fixture.Host.Services.GetRequiredService<ICharacterSkillTable>();
        var levels = fixture.Host.Services.GetRequiredService<ICharacterDomainService>().Get(new(1));
        await using (var tx = Scope(fixture).Begin())
        {
            await contracts.AddDaysAsync(PremiumServiceType.ExpertSkillContract, 1, default);
            await skills.InsertAsync(new(1), 0, candidate.FindAvailableSlot(new HashSet<int>())!.Value, candidate.SkillId, 1, default);
            await levels.SetLevelAsync(1, default);
            Assert.Contains(await skills.ReadEntryAsync(new(1), default), s => s.SkillId == candidate.SkillId);
            await tx.CommitAsync(default);
        }
        fixture.Host.Sql("UPDATE account_premiums SET end_time=1 WHERE premium_type=27");
        await using (var tx = Scope(fixture).Begin())
        {
            await levels.SetLevelAsync(1, default);
            Assert.DoesNotContain(await skills.ReadEntryAsync(new(1), default), s => s.SkillId == candidate.SkillId);
            await tx.CommitAsync(default);
        }
    }
}
