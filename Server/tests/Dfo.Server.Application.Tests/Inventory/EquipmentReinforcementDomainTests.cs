using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentReinforcementDomainTests
{
    [Fact]
    public async Task Success_commits_one_level_materials_gold_and_preserves_other_instance_fields()
    {
        using var shop = Fixture(failureWeight: 0);
        var original = Piece(0) with { EnchantCardId = 42, RandomOptionState = 7 };
        await shop.SeedCore(9, original);
        await shop.Seed(121, 1000, 20, 3);
        var originalUid = shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9");

        var result = await Reinforce(shop);

        Assert.Equal(EquipmentReinforcementRejection.None, result.Rejection);
        Assert.Equal(EquipmentReinforcementOutcome.Succeeded, result.Receipt!.Outcome);
        shop.Services.GetRequiredService<ICharacterContainerDomainService>()
            .Clear(shop.Session.CharacterId);
        shop.Select(1);
        Assert.Equal(originalUid, shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9"));
        Assert.Equal(1, shop.Scalar("SELECT hex(substr(i.item_core,18,1)) FROM items i JOIN item_slots s ON s.core_uid=i.item_uid WHERE s.owner_scope='character' AND s.owner_id=1 AND s.list_type=0 AND s.slot_index=9"));
        Assert.Equal(1, (await Target(shop))!.EnchantUpgradeCount);
        Assert.Equal(original with { EnchantUpgradeCount = 1 }, await Target(shop));
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
    }

    [Theory]
    [InlineData(1, 0, 4, 4, EquipmentReinforcementOutcome.Retained)]
    [InlineData(2, 1, 4, 3, EquipmentReinforcementOutcome.Downgraded)]
    public async Task Ordinary_failure_consumes_material_and_gold_but_applies_the_declared_penalty(
        int penaltyType, int penaltyValue, byte oldLevel, byte newLevel,
        EquipmentReinforcementOutcome outcome)
    {
        using var shop = Fixture(100_000, penaltyType, penaltyValue, oldLevel + 1);
        await shop.SeedCore(9, Piece(oldLevel));
        await shop.Seed(121, 1000, 20, 3);

        var result = await Reinforce(shop);

        Assert.Equal(outcome, result.Receipt!.Outcome);
        Assert.Equal(newLevel, (await Target(shop))!.EnchantUpgradeCount);
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
    }

    [Fact]
    public async Task Missing_material_refuses_without_any_asset_change()
    {
        using var shop = Fixture(0);
        await shop.SeedCore(9, Piece(0));
        await shop.Seed(121, 1000, 9, 3);

        var result = await Reinforce(shop);

        Assert.Equal(EquipmentReinforcementRejection.InsufficientMaterial, result.Rejection);
        Assert.Equal((byte)0, (await Target(shop))!.EnchantUpgradeCount);
        Assert.Equal(9, shop.Count(1000));
        Assert.Equal(100_000, shop.Gold);
    }

    [Theory]
    [InlineData((byte)0x80)]
    [InlineData((byte)3)]
    public async Task Unidentified_or_identified_outworld_equipment_is_not_ordinary_reinforcement(byte amplifyType)
    {
        using var shop = Fixture(0);
        var original = Piece(0) with { AmplifyType = amplifyType };
        await shop.SeedCore(9, original);
        await shop.Seed(121, 1000, 20, 3);

        var result = await Reinforce(shop);

        Assert.Equal(EquipmentReinforcementRejection.CannotReinforce, result.Rejection);
        Assert.Equal(original, await Target(shop));
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(100_000, shop.Gold);
    }

    [Fact]
    public async Task Level_above_upstream_admission_ceiling_refuses_without_spending()
    {
        using var shop = Fixture(0, targetLevel: 32);
        await shop.SeedCore(9, Piece(31));
        await shop.Seed(121, 1000, 20, 3);

        var result = await Reinforce(shop);

        Assert.Equal(EquipmentReinforcementRejection.CannotReinforce, result.Rejection);
        Assert.Equal((byte)31, (await Target(shop))!.EnchantUpgradeCount);
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(100_000, shop.Gold);
    }

    [Fact]
    public async Task Cost_weight_missing_above_nineteen_uses_upstream_neutral_factor()
    {
        using var shop = Fixture(0, targetLevel: 20, omitCostWeight: true);
        await shop.SeedCore(9, Piece(19));
        await shop.Seed(121, 1000, 20, 3);

        var result = await Reinforce(shop);

        Assert.Equal(EquipmentReinforcementRejection.None, result.Rejection);
        Assert.Equal((byte)20, (await Target(shop))!.EnchantUpgradeCount);
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
    }

    [Theory]
    [InlineData(true, false, EquipmentReinforcementRejection.Locked)]
    [InlineData(false, true, EquipmentReinforcementRejection.Durability)]
    public async Task Locked_or_damaged_equipment_is_rejected_without_asset_changes(
        bool locked, bool damaged, EquipmentReinforcementRejection expected)
    {
        using var shop = Fixture(0);
        var target = Piece(0) with
        {
            EquipmentLockId = (byte)(locked ? 1 : 0),
            Durability = damaged ? (ushort)44 : (ushort)45,
        };
        await shop.SeedCore(9, target);
        await shop.Seed(121, 1000, 20, 3);

        var result = await Reinforce(shop);

        Assert.Equal(expected, result.Rejection);
        Assert.Equal(target, await Target(shop));
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(100_000, shop.Gold);
    }

    [Fact]
    public async Task Insufficient_gold_is_rejected_before_material_consumption()
    {
        using var shop = Fixture(0);
        await shop.SeedCore(9, Piece(0));
        await shop.Seed(121, 1000, 20, 3);
        shop.Sql("UPDATE character_stats SET carried_gold=99 WHERE character_id=1");

        var result = await Reinforce(shop);

        Assert.Equal(EquipmentReinforcementRejection.InsufficientGold, result.Rejection);
        Assert.Equal((byte)0, (await Target(shop))!.EnchantUpgradeCount);
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(99, shop.Gold);
    }

    [Fact]
    public async Task Gold_commit_failure_rolls_back_equipment_and_material()
    {
        using var shop = Fixture(0);
        await shop.SeedCore(9, Piece(0));
        await shop.Seed(121, 1000, 20, 3);
        shop.Sql("CREATE TRIGGER refuse_reinforcement_gold BEFORE UPDATE OF carried_gold ON character_stats "
            + "WHEN NEW.carried_gold <> OLD.carried_gold BEGIN SELECT RAISE(ABORT,'forced gold failure'); END;");

        await Assert.ThrowsAsync<SqliteException>(() => Reinforce(shop).AsTask());

        Assert.Equal((byte)0, (await Target(shop))!.EnchantUpgradeCount);
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(100_000, shop.Gold);
    }

    [Fact]
    public async Task Destructive_failure_grants_the_planned_bonus_in_the_same_transaction()
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 12);
        await shop.SeedCore(9, Piece(11));
        await shop.Seed(121, 1000, 20, 3);

        var result = await Reinforce(shop);

        Assert.Equal(EquipmentReinforcementOutcome.Destroyed, result.Receipt!.Outcome);
        Assert.Null(await Target(shop));
        Assert.Equal(4, shop.Count(3243));
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
    }

    [Fact]
    public async Task No_space_for_destroy_bonus_refuses_before_any_asset_change()
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 12);
        await shop.SeedCore(9, Piece(11));
        await shop.Seed(121, 1000, 20, 3);
        for (var slot = 3; slot <= 8; slot++) await shop.Seed(slot, 4001, 1);
        await shop.Seed(65, 4001, 1);
        await shop.Seed(66, 4001, 1);

        var result = await Reinforce(shop);

        Assert.Equal(EquipmentReinforcementRejection.NoRoom, result.Rejection);
        Assert.Equal((byte)11, (await Target(shop))!.EnchantUpgradeCount);
        Assert.Equal(0, shop.Count(3243));
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(100_000, shop.Gold);
    }

    [Fact]
    public async Task Super_protection_prevents_destruction_and_is_only_consumed_on_failure()
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 12);
        await shop.SeedCore(9, Piece(11));
        await shop.Seed(121, 1000, 20, 3);
        await shop.Seed(65, 4000, 2);

        var result = await Reinforce(shop, new(BusinessInventorySpace.Consumable, 0));

        Assert.Equal(EquipmentReinforcementOutcome.Downgraded, result.Receipt!.Outcome);
        Assert.Equal((byte)10, (await Target(shop))!.EnchantUpgradeCount);
        Assert.Equal(1, shop.Count(4000));
        Assert.Equal(0, shop.Count(3243));
        Assert.Equal(10, shop.Count(1000));
    }

    [Fact]
    public async Task Protection_ticket_is_not_consumed_when_reinforcement_succeeds()
    {
        using var shop = Fixture(0, penaltyType: 3, targetLevel: 12);
        await shop.SeedCore(9, Piece(11));
        await shop.Seed(121, 1000, 20, 3);
        await shop.Seed(65, 4000, 2);

        var result = await Reinforce(shop, new(BusinessInventorySpace.Consumable, 0));

        Assert.Equal(EquipmentReinforcementOutcome.Succeeded, result.Receipt!.Outcome);
        Assert.Equal((byte)12, (await Target(shop))!.EnchantUpgradeCount);
        Assert.Equal(2, shop.Count(4000));
    }

    [Fact]
    public async Task Destruction_attempt_automatically_uses_a_declared_protection_ticket()
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 12);
        await shop.SeedCore(9, Piece(11));
        await shop.Seed(121, 1000, 20, 3);
        await shop.Seed(3, 4000, 2);

        var result = await Reinforce(shop);

        Assert.Equal(EquipmentReinforcementOutcome.Downgraded, result.Receipt!.Outcome);
        Assert.Equal((byte)10, (await Target(shop))!.EnchantUpgradeCount);
        Assert.Equal(1, shop.Count(4000));
        Assert.Equal(0, shop.Count(3243));
    }

    [Fact]
    public async Task Account_virtual_cube_is_consumed_without_a_physical_material_pile()
    {
        using var shop = Fixture(0, materialId: 3037);
        await shop.SeedCore(9, Piece(0));
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<
            Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor>();
        await using (var seed = scope.Begin())
        {
            await shop.Services.GetRequiredService<IAccountMaterialBagDomainService>()
                .Get(shop.Session.AccountId)
                .AddAsync(new Dictionary<AccountMaterialKind, int>
                    { [AccountMaterialKind.ClearCube] = 20 }, default);
            await seed.CommitAsync(default);
        }

        var result = await Reinforce(shop, accountMaterialItemId: 3037);

        Assert.Equal(EquipmentReinforcementRejection.None, result.Rejection);
        Assert.Equal(10, result.Receipt!.MaterialRemaining);
        Assert.Equal(10, shop.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
        Assert.Equal(0, shop.Count(3037));
        Assert.Equal((byte)1, (await Target(shop))!.EnchantUpgradeCount);
    }

    [Fact]
    public async Task Wrong_virtual_material_type_cannot_spend_the_actual_cube_balance()
    {
        using var shop = Fixture(0, materialId: 3037);
        await shop.SeedCore(9, Piece(0));
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<
            Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor>();
        await using (var seed = scope.Begin())
        {
            await shop.Services.GetRequiredService<IAccountMaterialBagDomainService>()
                .Get(shop.Session.AccountId)
                .AddAsync(new Dictionary<AccountMaterialKind, int>
                    { [AccountMaterialKind.ClearCube] = 20 }, default);
            await seed.CommitAsync(default);
        }

        var result = await Reinforce(shop, accountMaterialItemId: 3033);

        Assert.Equal(EquipmentReinforcementRejection.InsufficientMaterial, result.Rejection);
        Assert.Equal(20, shop.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
        Assert.Equal((byte)0, (await Target(shop))!.EnchantUpgradeCount);
        Assert.Equal(100_000, shop.Gold);
    }

    private static ShopFixture Fixture(int failureWeight, int penaltyType = 1,
        int penaltyValue = 0, int targetLevel = 1, int materialId = 1000,
        bool omitCostWeight = false) => new(services =>
    {
        services.AddSingleton<IEquipmentReinforcementDefinitionSource>(
            new Content(failureWeight, penaltyType, penaltyValue, targetLevel,
                materialId, omitCostWeight));
        services.AddSingleton<IEquipmentReinforcementRollSource>(new FixedRoll(0));
        services.AddSingleton<IEquipmentReinforcementDomainService, EquipmentReinforcementDomainService>();
    });

    private static InventoryItemCoreState Piece(byte level) =>
        StackableItemCore.Create(1, 3000, 1234) with
        {
            Durability = 45,
            EnchantUpgradeCount = level,
        };

    private static async ValueTask<InventoryItemCoreState?> Target(ShopFixture shop)
    {
        var bound = shop.Services.GetRequiredService<IBusinessInventorySlotDomainService>()
            .Get(shop.Session.CharacterId, new(BusinessInventorySpace.CarriedEquipment, 0));
        return (await bound.Container.ReadSlotAsync(bound.Slot.Index, default))?.Core;
    }

    private static async ValueTask<EquipmentReinforcementResult> Reinforce(
        ShopFixture shop, BusinessSlot? protection = null, int? accountMaterialItemId = null)
    {
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<
            Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor>();
        await using var transaction = scope.Begin();
        var result = await shop.Services.GetRequiredService<IEquipmentReinforcementDomainService>()
            .ReinforceAsync(shop.Session, new(BusinessInventorySpace.CarriedEquipment, 0), 3000,
                accountMaterialItemId is null ? new(BusinessInventorySpace.Material, 0) : null,
                accountMaterialItemId, protection, default);
        if (result.Rejection == EquipmentReinforcementRejection.None)
            await transaction.CommitAsync(default);
        return result;
    }

    private sealed class FixedRoll(int value) : IEquipmentReinforcementRollSource
    {
        public int Next(int exclusiveUpperBound) => value;
    }

    private sealed class Content(int failureWeight, int penaltyType, int penaltyValue,
        int targetLevel, int materialId, bool omitCostWeight) : IEquipmentReinforcementDefinitionSource
    {
        public ValueTask<EquipmentReinforcementTable?> GetNormalTableAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementTable?>(new(
                [new(targetLevel, new double[19], failureWeight, penaltyType,
                    penaltyValue, materialId, 10)],
                [0, 100], [1], [1],
                omitCostWeight ? new Dictionary<int, double>()
                    : new Dictionary<int, double> { [targetLevel - 1] = 1 },
                new Dictionary<int, int>(), new Dictionary<int, int>(),
                new(3243, 40, 10, new Dictionary<int, double> { [0] = 1 },
                    new Dictionary<int, int>()), 12));

        public ValueTask<EquipmentReinforcementEquipmentDefinition?> GetEquipmentAsync(
            int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementEquipmentDefinition?>(
                itemId == 3000 ? new(1, 50, 0, 0, 45, true) : null);

        public ValueTask<EquipmentReinforcementProtectionDefinition?> GetProtectionAsync(
            int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementProtectionDefinition?>(
                itemId == 4000
                    ? new(EquipmentReinforcementProtectionKind.RetainLevel, 10)
                    : null);
    }
}
