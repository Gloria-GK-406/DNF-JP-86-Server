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
        Assert.Equal((byte)0xC1, shop.StoredCore(9)[9]);
        Assert.Equal((byte)9, shop.StoredCore(9)[17]);
        Assert.Equal(1, (await Target(shop))!.ReinforcementLevel);
        Assert.Equal(original with { Attribute = 0xC1 }, await Target(shop));
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
        Assert.Equal(newLevel, (await Target(shop))!.ReinforcementLevel);
        Assert.Equal((byte)(0xC0 | newLevel), shop.StoredCore(9)[9]);
        Assert.Equal((byte)9, shop.StoredCore(9)[17]);
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
        Assert.Equal((byte)0, (await Target(shop))!.ReinforcementLevel);
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
        Assert.Equal((byte)31, (await Target(shop))!.ReinforcementLevel);
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(100_000, shop.Gold);
    }

    [Fact]
    public async Task Current_level_thirty_can_reach_maximum_encoded_level_without_affecting_reseal_or_enchant()
    {
        using var shop = Fixture(0, targetLevel: 31);
        await shop.SeedCore(9, Piece(30));
        await shop.Seed(121, 1000, 20, 3);

        var result = await Reinforce(shop);

        Assert.Equal(EquipmentReinforcementRejection.None, result.Rejection);
        Assert.Equal((byte)30, result.Receipt!.OldLevel);
        Assert.Equal((byte)31, result.Receipt.NewLevel);
        Assert.Equal((byte)0xDF, shop.StoredCore(9)[9]);
        Assert.Equal((byte)9, shop.StoredCore(9)[17]);
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
    }

    [Fact]
    public async Task Cost_weight_missing_above_nineteen_uses_upstream_neutral_factor()
    {
        using var shop = Fixture(0, targetLevel: 20, omitCostWeight: true);
        await shop.SeedCore(9, Piece(19));
        await shop.Seed(121, 1000, 20, 3);

        var result = await Reinforce(shop);

        Assert.Equal(EquipmentReinforcementRejection.None, result.Rejection);
        Assert.Equal((byte)20, (await Target(shop))!.ReinforcementLevel);
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
        Assert.Equal((byte)0, (await Target(shop))!.ReinforcementLevel);
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

        Assert.Equal((byte)0, (await Target(shop))!.ReinforcementLevel);
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
        Assert.Equal(1, shop.Count(3243));
        Assert.Equal(5, shop.Count(3166));
        Assert.Equal(1, shop.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
        Assert.Equal(new[] { 3037, 3166, 3243 }, result.Receipt.DestroyRewards.Select(reward => reward.ItemId));
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
    }

    [Fact]
    public async Task White_destruction_grants_base_clear_cubes_as_account_material()
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 12, white: true);
        await shop.SeedCore(9, Piece(11));
        await shop.Seed(121, 1000, 20, 3);
        var result = await Reinforce(shop);
        Assert.Equal(EquipmentReinforcementOutcome.Destroyed, result.Receipt!.Outcome);
        Assert.Null(await Target(shop));
        Assert.Equal(33, shop.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
        Assert.Equal(0, shop.Count(3243));
        Assert.Equal(0, shop.Scalar("SELECT soul_common FROM accounts WHERE account_id=1"));
        Assert.Contains(result.Receipt.AccountMaterials, change => change.ItemId == 3037 && change.AddedCount > 0);
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
        Assert.Equal((byte)11, (await Target(shop))!.ReinforcementLevel);
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
        Assert.Equal((byte)10, (await Target(shop))!.ReinforcementLevel);
        Assert.Equal((byte)0xCA, shop.StoredCore(9)[9]);
        Assert.Equal((byte)9, shop.StoredCore(9)[17]);
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
        Assert.Equal((byte)12, (await Target(shop))!.ReinforcementLevel);
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
        Assert.Equal((byte)10, (await Target(shop))!.ReinforcementLevel);
        Assert.Equal((byte)0xCA, shop.StoredCore(9)[9]);
        Assert.Equal((byte)9, shop.StoredCore(9)[17]);
        Assert.Equal(1, shop.Count(4000));
        Assert.Equal(0, shop.Count(3243));
    }

    [Theory]
    [InlineData(7804, 0, 3, false)]
    [InlineData(7804, 0, 121, false)]
    [InlineData(7804, 0, 3, true)]
    [InlineData(10007840, 10, 3, false)]
    [InlineData(10007840, 10, 121, false)]
    [InlineData(10007840, 10, 3, true)]
    public async Task Protection_failure_preserves_uid_and_other_core_bytes_and_consumes_exactly_one_ticket(
        int ticketId, byte expectedLevel, int ticketSlot, bool explicitTicket)
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 12);
        var original = Piece(11) with { EnchantCardId = 42, RandomOptionState = 7 };
        await shop.SeedCore(9, original);
        await shop.Seed(122, 1000, 20, 3);
        await shop.Seed(ticketSlot, ticketId, 2, ticketSlot >= 121 ? (byte)3 : (byte)2);
        var uid = EquipmentUid(shop);
        var before = shop.StoredCore(9);
        var ticket = new BusinessSlot(ticketSlot >= 121 ? BusinessInventorySpace.Material
            : BusinessInventorySpace.QuickSlot, ticketSlot >= 121 ? ticketSlot - 121 : ticketSlot - 3);

        var result = await Reinforce(shop, explicitTicket ? ticket : null,
            material: new(BusinessInventorySpace.Material, 1));

        Assert.Equal(EquipmentReinforcementOutcome.Downgraded, result.Receipt!.Outcome);
        Assert.Equal(ticket, result.Receipt.ProtectionSlot);
        Assert.Equal(expectedLevel, result.Receipt.NewLevel);
        before[9] = (byte)(0xC0 | expectedLevel);
        Assert.Equal(before, shop.StoredCore(9));
        Assert.Equal(uid, EquipmentUid(shop));
        Assert.Equal(1, shop.Count(ticketId));
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
        Assert.Equal(0, shop.Count(3243));
        shop.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(shop.Session.CharacterId);
        shop.Select(1);
        Assert.Equal(original with { Attribute = (byte)(0xC0 | expectedLevel) }, await Target(shop));
    }

    [Theory]
    [InlineData(3, 121)]
    [InlineData(121, 3)]
    public async Task Automatic_super_ticket_has_priority_over_ordinary_ticket_in_either_inventory_order(
        int normalSlot, int superSlot)
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 12);
        await shop.SeedCore(9, Piece(11));
        await shop.Seed(122, 1000, 20, 3);
        await shop.Seed(normalSlot, 7804, 2, normalSlot >= 121 ? (byte)3 : (byte)2);
        await shop.Seed(superSlot, 10007840, 2, superSlot >= 121 ? (byte)3 : (byte)2);

        var result = await Reinforce(shop, material: new(BusinessInventorySpace.Material, 1));

        Assert.Equal((byte)10, result.Receipt!.NewLevel);
        Assert.Equal(2, shop.Count(7804));
        Assert.Equal(1, shop.Count(10007840));
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
    }

    [Theory]
    [InlineData(7804, false, 0, 3, 0, 12)]
    [InlineData(10007840, false, 0, 3, 0, 12)]
    [InlineData(7804, true, 0, 3, 0, 12)]
    [InlineData(10007840, true, 0, 3, 0, 12)]
    [InlineData(7804, true, 100_000, 1, 0, 11)]
    [InlineData(10007840, true, 100_000, 1, 0, 11)]
    [InlineData(7804, true, 100_000, 2, 1, 10)]
    [InlineData(10007840, true, 100_000, 2, 1, 10)]
    public async Task Success_or_non_destructive_failure_never_consumes_a_valid_protection_ticket(
        int ticketId, bool explicitTicket, int failureWeight, int penaltyType, int penaltyValue,
        byte expectedLevel)
    {
        using var shop = Fixture(failureWeight, penaltyType, penaltyValue, targetLevel: 12);
        await shop.SeedCore(9, Piece(11));
        await shop.Seed(121, 1000, 20, 3);
        await shop.Seed(3, ticketId, 2);

        var result = await Reinforce(shop,
            explicitTicket ? new(BusinessInventorySpace.QuickSlot, 0) : null);

        Assert.Null(result.Receipt!.ProtectionSlot);
        Assert.Equal(expectedLevel, (await Target(shop))!.ReinforcementLevel);
        Assert.Equal(2, shop.Count(ticketId));
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
    }

    [Theory]
    [InlineData(7804, -1)]
    [InlineData(10007840, 0)]
    [InlineData(10004511, 1)]
    [InlineData(10007841, 1)]
    public async Task Explicit_expired_or_wrong_action_ticket_rejects_without_any_asset_change(
        int ticketId, int expirationOffset)
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 12);
        var original = Piece(11);
        await shop.SeedCore(9, original);
        await shop.Seed(121, 1000, 20, 3);
        await shop.SeedCore(3, StackableItemCore.Create(2, ticketId, 2) with
        {
            ExpireTime = checked((int)shop.Clock.GetUtcNow().ToUnixTimeSeconds()) + expirationOffset,
        });

        var result = await Reinforce(shop, new(BusinessInventorySpace.QuickSlot, 0));

        Assert.Equal(EquipmentReinforcementRejection.InvalidRequest, result.Rejection);
        Assert.Equal(original, await Target(shop));
        Assert.Equal(2, shop.Count(ticketId));
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(100_000, shop.Gold);
    }

    [Fact]
    public async Task Automatic_selection_skips_expired_super_and_amplification_tickets_and_uses_valid_ordinary()
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 12);
        await shop.SeedCore(9, Piece(11));
        await shop.Seed(121, 1000, 20, 3);
        await shop.SeedCore(3, StackableItemCore.Create(2, 10007840, 2) with
        {
            ExpireTime = checked((int)shop.Clock.GetUtcNow().ToUnixTimeSeconds()),
        });
        await shop.Seed(4, 10004511, 2);
        await shop.Seed(5, 10007841, 2);
        await shop.Seed(6, 7804, 2);

        var result = await Reinforce(shop);

        Assert.Equal((byte)0, result.Receipt!.NewLevel);
        Assert.Equal(new BusinessSlot(BusinessInventorySpace.QuickSlot, 3), result.Receipt.ProtectionSlot);
        Assert.Equal(1, shop.Count(7804));
        Assert.Equal(2, shop.Count(10007840));
        Assert.Equal(2, shop.Count(10004511));
        Assert.Equal(2, shop.Count(10007841));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Super_ticket_is_not_applicable_at_its_retained_level_and_cannot_replace_ordinary_protection(
        bool hasOrdinary)
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 11);
        await shop.SeedCore(9, Piece(10));
        await shop.Seed(121, 1000, 20, 3);
        await shop.Seed(3, 10007840, 2);
        if (hasOrdinary) await shop.Seed(4, 7804, 2);

        var result = await Reinforce(shop);

        Assert.Equal(hasOrdinary ? EquipmentReinforcementOutcome.Downgraded
            : EquipmentReinforcementOutcome.Destroyed, result.Receipt!.Outcome);
        Assert.Equal(2, shop.Count(10007840));
        Assert.Equal(hasOrdinary ? 1 : 0, shop.Count(7804));
        if (hasOrdinary) Assert.Equal((byte)0, (await Target(shop))!.ReinforcementLevel);
        else Assert.Null(await Target(shop));
    }

    [Fact]
    public async Task Explicit_super_ticket_at_its_retained_level_does_not_protect_or_consume()
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 11);
        await shop.SeedCore(9, Piece(10));
        await shop.Seed(121, 1000, 20, 3);
        await shop.Seed(3, 10007840, 2);

        var result = await Reinforce(shop, new(BusinessInventorySpace.QuickSlot, 0));

        Assert.Equal(EquipmentReinforcementOutcome.Destroyed, result.Receipt!.Outcome);
        Assert.Null(result.Receipt.ProtectionSlot);
        Assert.Null(await Target(shop));
        Assert.Equal(2, shop.Count(10007840));
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
    }

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(1, 10, 1)]
    [InlineData(-1, 0, 2)]
    public async Task Automatic_protection_uses_only_unexpired_instance_tickets(
        int expirationOffset, byte expectedLevel, int expectedTickets)
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 12);
        await shop.SeedCore(9, Piece(11));
        await shop.Seed(121, 1000, 20, 3);
        await shop.SeedCore(3, StackableItemCore.Create(2, 10007840, 2) with
        {
            ExpireTime = expirationOffset == 0 ? 0
                : checked((int)shop.Clock.GetUtcNow().ToUnixTimeSeconds()) + expirationOffset,
        });

        var result = await Reinforce(shop);

        Assert.Equal(expectedLevel, result.Receipt!.NewLevel);
        Assert.Equal(expectedTickets, shop.Count(10007840));
        Assert.Equal(expirationOffset < 0 ? EquipmentReinforcementOutcome.Destroyed
            : EquipmentReinforcementOutcome.Downgraded, result.Receipt.Outcome);
    }

    [Fact]
    public async Task Explicit_ticket_slot_does_not_borrow_another_characters_ticket()
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 12);
        var containers = shop.Services.GetRequiredService<ICharacterContainerDomainService>();
        containers.Clear(shop.Session.CharacterId);
        shop.Select(2);
        await shop.Seed(3, 7804, 2);
        containers.Clear(shop.Session.CharacterId);
        shop.Select(1);
        var original = Piece(11);
        await shop.SeedCore(9, original);
        await shop.Seed(121, 1000, 20, 3);

        var result = await Reinforce(shop, new(BusinessInventorySpace.QuickSlot, 0));

        Assert.Equal(EquipmentReinforcementRejection.InvalidRequest, result.Rejection);
        Assert.Equal(original, await Target(shop));
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(100_000, shop.Gold);
        containers.Clear(shop.Session.CharacterId);
        shop.Select(2);
        Assert.Equal(2, shop.Count(7804));
    }

    [Theory]
    [InlineData(BusinessInventorySpace.QuickSlot, 5)]
    [InlineData(BusinessInventorySpace.PersonalCargo, 0)]
    [InlineData(BusinessInventorySpace.CarriedEquipment, 0)]
    [InlineData(BusinessInventorySpace.Material, 0)]
    public async Task Missing_non_carried_or_conflicting_ticket_slot_cannot_spend_any_asset(
        BusinessInventorySpace space, int index)
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 12);
        var original = Piece(11);
        await shop.SeedCore(9, original);
        await shop.Seed(121, 1000, 20, 3);
        await shop.Seed(3, 7804, 2);

        var result = await Reinforce(shop, new(space, index));

        Assert.Equal(EquipmentReinforcementRejection.InvalidRequest, result.Rejection);
        Assert.Equal(original, await Target(shop));
        Assert.Equal(2, shop.Count(7804));
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(100_000, shop.Gold);
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
        Assert.Equal((byte)1, (await Target(shop))!.ReinforcementLevel);
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
        Assert.Equal((byte)0, (await Target(shop))!.ReinforcementLevel);
        Assert.Equal(100_000, shop.Gold);
    }

    [Fact]
    public async Task Distinct_rewards_compete_for_the_last_slot_and_refuse_the_whole_operation()
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 12);
        await shop.SeedCore(9, Piece(11));
        await shop.Seed(121, 1000, 20, 3);
        for (var slot = 3; slot <= 7; slot++) await shop.Seed(slot, 4001, 1);
        await shop.Seed(65, 4001, 1);
        await shop.Seed(66, 4001, 1);
        var result = await Reinforce(shop);
        Assert.Equal(EquipmentReinforcementRejection.NoRoom, result.Rejection);
        Assert.Equal((byte)11, (await Target(shop))!.ReinforcementLevel);
        Assert.Equal(0, shop.Count(3166));
        Assert.Equal(0, shop.Count(3243));
        Assert.Equal(0, shop.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(100_000, shop.Gold);
    }

    [Theory]
    [InlineData(20, false, 43)]
    [InlineData(2147483624, false, 2147483647)]
    [InlineData(2147483625, true, 2147483625)]
    public async Task Virtual_reward_capacity_uses_the_balance_after_material_debit(
        int initial, bool rejected, int expected)
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 12, materialId: 3037, white: true);
        await shop.SeedCore(9, Piece(11));
        await SeedClearCubes(shop, initial);
        var result = await Reinforce(shop, accountMaterialItemId: 3037);
        Assert.Equal(rejected ? EquipmentReinforcementRejection.NoRoom : EquipmentReinforcementRejection.None, result.Rejection);
        Assert.Equal(expected, shop.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
        Assert.Equal(rejected ? 100_000 : 99_900, shop.Gold);
        if (rejected) Assert.Equal((byte)11, (await Target(shop))!.ReinforcementLevel);
        else
        {
            Assert.Null(await Target(shop));
            Assert.Equal(expected, result.Receipt!.MaterialRemaining);
        }
    }

    [Fact]
    public async Task Missing_destruction_content_refuses_without_spending_or_destroying()
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 12,
            configure: services => services.AddSingleton<IEquipmentDisjointDefinitionSource>(new MissingDestruction()));
        await shop.SeedCore(9, Piece(11));
        await shop.Seed(121, 1000, 20, 3);
        var result = await Reinforce(shop);
        Assert.Equal(EquipmentReinforcementRejection.UnsupportedOutcome, result.Rejection);
        Assert.Equal((byte)11, (await Target(shop))!.ReinforcementLevel);
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(100_000, shop.Gold);
    }

    [Fact]
    public async Task Later_reward_staging_failure_rolls_back_prior_rewards_destruction_and_all_debits()
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 12,
            configure: services => services.AddSingleton<IItemPlacementDefinitionSource>(new FailLaterReward()));
        await shop.SeedCore(9, Piece(11));
        await shop.Seed(121, 1000, 20, 3);
        var uid = EquipmentUid(shop);
        await Assert.ThrowsAsync<ContainerAdditionException>(() => Reinforce(shop).AsTask());
        Assert.Equal((byte)11, (await Target(shop))!.ReinforcementLevel);
        Assert.Equal(uid, EquipmentUid(shop));
        Assert.Equal(0, shop.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
        Assert.Equal(0, shop.Count(3166));
        Assert.Equal(0, shop.Count(3243));
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(100_000, shop.Gold);
    }

    [Fact]
    public async Task Failure_service_instance_adapter_is_read_only_and_uses_the_original_level()
    {
        using var shop = Fixture(100_000, penaltyType: 3, targetLevel: 12);
        await shop.SeedCore(9, Piece(11));
        await shop.Seed(121, 1000, 20, 3);
        var before = shop.StoredCore(9);
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor>();
        await using var transaction = scope.Begin();
        var instance = await shop.Services.GetRequiredService<IEquipmentDomainService>().Of(shop.Session.CharacterId)
            .FindAtAsync(new(BusinessInventorySpace.CarriedEquipment, 0), default);
        var result = await shop.Services.GetRequiredService<IEquipmentReinforcementFailureRuleService>()
            .GetResultAsync(instance!, new(null, 0, 0), default);
        Assert.Equal(1, Assert.Single(result.Rewards, reward => reward.ItemId == 3243).Count);
        Assert.Equal((byte)11, (await instance!.ReadAsync(default)).Core.ReinforcementLevel);
        Assert.Equal(before, shop.StoredCore(9));
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(100_000, shop.Gold);
    }

    private static async Task SeedClearCubes(ShopFixture shop, int count)
    {
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor>();
        await using var seed = scope.Begin();
        await shop.Services.GetRequiredService<IAccountMaterialBagDomainService>().Get(shop.Session.AccountId)
            .AddAsync(new Dictionary<AccountMaterialKind, int> { [AccountMaterialKind.ClearCube] = count }, default);
        await seed.CommitAsync(default);
    }

    private sealed class MissingDestruction : IEquipmentDisjointDefinitionSource
    {
        public ValueTask<EquipmentDisjointDefinition?> GetEquipmentAsync(int itemId, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<PortableDisjointMachineDefinition?> GetPortableMachineAsync(int itemId, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<EquipmentDisjointTable?> GetSystemTableAsync(CancellationToken ct) => ValueTask.FromResult<EquipmentDisjointTable?>(null);
    }

    private sealed class FailLaterReward : IItemPlacementDefinitionSource
    {
        private readonly NpcShopTestContent inner = new();
        private int bonusReads;
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken ct) =>
            itemId == 3243 && ++bonusReads > 1
                ? ValueTask.FromResult<ItemPlacement?>(null) : inner.GetAsync(itemId, ct);
    }

    private static ShopFixture Fixture(int failureWeight, int penaltyType = 1,
        int penaltyValue = 0, int targetLevel = 1, int materialId = 1000,
        bool omitCostWeight = false, bool white = false, Action<IServiceCollection>? configure = null) => new(services =>
    {
        services.AddSingleton<IEquipmentReinforcementDefinitionSource>(
            new Content(failureWeight, penaltyType, penaltyValue, targetLevel,
                materialId, omitCostWeight, white));
        services.AddSingleton<IEquipmentDisjointDefinitionSource>(new ReinforcementDestructionTestContent(white ? 0 : 2, white ? 57 : 50, white ? 20520 : 1000));
        services.AddSingleton<IEquipmentReinforcementRollSource>(new FixedRoll(0));
        services.AddSingleton<IEquipmentReinforcementDomainService, EquipmentReinforcementDomainService>();
        configure?.Invoke(services);
    });

    private static InventoryItemCoreState Piece(byte level) =>
        StackableItemCore.Create(1, 3000, 1234) with
        {
            Durability = 45,
            Attribute = (byte)(0xC0 | level),
            EnchantUpgradeCount = 9,
        };

    private static async ValueTask<InventoryItemCoreState?> Target(ShopFixture shop)
    {
        var bound = shop.Services.GetRequiredService<IBusinessInventorySlotDomainService>()
            .Get(shop.Session.CharacterId, new(BusinessInventorySpace.CarriedEquipment, 0));
        return (await bound.Container.ReadSlotAsync(bound.Slot.Index, default))?.Core;
    }

    private static async ValueTask<EquipmentReinforcementResult> Reinforce(
        ShopFixture shop, BusinessSlot? protection = null, int? accountMaterialItemId = null,
        BusinessSlot? material = null)
    {
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<
            Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor>();
        await using var transaction = scope.Begin();
        var result = await shop.Services.GetRequiredService<IEquipmentReinforcementDomainService>()
            .ReinforceAsync(shop.Session, new(BusinessInventorySpace.CarriedEquipment, 0), 3000,
                accountMaterialItemId is null ? material ?? new(BusinessInventorySpace.Material, 0) : null,
                accountMaterialItemId, protection, default);
        if (result.Rejection == EquipmentReinforcementRejection.None)
            await transaction.CommitAsync(default);
        return result;
    }

    private static int EquipmentUid(ShopFixture shop) => shop.Scalar(
        "SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=9");

    private sealed class FixedRoll(int value) : IEquipmentReinforcementRollSource
    {
        public int Next(int exclusiveUpperBound) => value;
    }

    private sealed class Content(int failureWeight, int penaltyType, int penaltyValue,
        int targetLevel, int materialId, bool omitCostWeight, bool white) : IEquipmentReinforcementDefinitionSource
    {
        public ValueTask<EquipmentReinforcementTable?> GetNormalTableAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementTable?>(new(
                [new(targetLevel, new double[19], failureWeight, penaltyType,
                    penaltyValue, materialId, 10)],
                [0, 100, 100, 100], [1, 1, 1], [1],
                omitCostWeight ? new Dictionary<int, double>()
                    : new Dictionary<int, double> { [targetLevel - 1] = 1 },
                new Dictionary<int, int>(), new Dictionary<int, int>(),
                new(3243, 40, 10, new Dictionary<int, double> { [2] = 1 },
                    new Dictionary<int, int>()), 12));

        public ValueTask<EquipmentReinforcementEquipmentDefinition?> GetEquipmentAsync(
            int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementEquipmentDefinition?>(
                itemId == 3000 ? new(1, white ? 57 : 50, white ? 0 : 2, 0, 45, true) : null);

        public ValueTask<EquipmentReinforcementProtectionDefinition?> GetProtectionAsync(
            int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementProtectionDefinition?>(
                itemId switch
                {
                    7804 => new(EquipmentReinforcementProtectionKind.ResetToZero, 0),
                    4000 or 10007840 => new(EquipmentReinforcementProtectionKind.RetainLevel, 10),
                    _ => null,
                });
    }
}
