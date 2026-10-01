using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentForgingDomainTests
{
    private static readonly BusinessSlot Target = new(BusinessInventorySpace.CarriedEquipment, 0);
    private static readonly BusinessSlot Material = new(BusinessInventorySpace.Material, 0);

    [Fact]
    public async Task Success_consumes_pvf_material_and_only_increments_genuine_upgrade()
    {
        using var shop = Fixture(0);
        var original = Piece() with { Attribute = 0xA7, EnchantUpgradeCount = 9, AmplifyType = 3,
            AmplifyValue = 5, EnchantCardId = 567, RandomOptionState = 3 };
        await shop.SeedCore(9, original);
        await shop.Seed(121, 3326, 50);
        var uid = shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' "
            + "AND owner_id=1 AND list_type=0 AND slot_index=9");

        var result = await Apply(shop);

        Assert.Equal(EquipmentForgingRejection.None, result.Rejection);
        Assert.True(Assert.IsType<EquipmentForgingReceipt>(result.Receipt).Succeeded);
        Assert.Equal(original with { GenuineUpgrade = 1 }, await Read(shop));
        Assert.Equal(11, shop.Count(3326));
        Assert.Equal(100000, shop.Gold);
        Assert.Equal(uid, shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' "
            + "AND owner_id=1 AND list_type=0 AND slot_index=9"));
        shop.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(shop.Session.CharacterId);
        shop.Select(1);
        Assert.Equal(original with { GenuineUpgrade = 1 }, await Read(shop));
    }

    [Fact]
    public async Task Probability_failure_consumes_material_but_preserves_all_upgrade_fields()
    {
        using var shop = Fixture(5000);
        var original = Piece() with { GenuineUpgrade = 1, Attribute = 0xA4, EnchantUpgradeCount = 7 };
        await shop.SeedCore(9, original);
        await shop.Seed(121, 3326, 50);

        var result = await Apply(shop);

        Assert.Equal(EquipmentForgingRejection.None, result.Rejection);
        var receipt = Assert.IsType<EquipmentForgingReceipt>(result.Receipt);
        Assert.False(receipt.Succeeded);
        Assert.Equal((byte)1, receipt.OldLevel);
        Assert.Equal((byte)1, receipt.NewLevel);
        Assert.Equal(7, shop.Count(3326));
        Assert.Equal(original, await Read(shop));
        Assert.Equal(100000, shop.Gold);
    }

    [Theory]
    [InlineData(0, 0, 8, EquipmentForgingRejection.MaxLevel)]
    [InlineData(1, 0, 0, EquipmentForgingRejection.NotWeapon)]
    [InlineData(0, 44, 0, EquipmentForgingRejection.Durability)]
    public async Task Invalid_state_does_not_spend(
        int equipmentType, int durability, int oldLevel, EquipmentForgingRejection rejection)
    {
        using var shop = Fixture(0, equipmentType);
        var original = Piece() with { GenuineUpgrade = (byte)oldLevel,
            Durability = durability == 0 ? (ushort)45 : (ushort)durability };
        await shop.SeedCore(9, original);
        await shop.Seed(121, 3326, 50);
        Assert.Equal(rejection, (await Apply(shop)).Rejection);
        Assert.Equal(original, await Read(shop));
        Assert.Equal(50, shop.Count(3326));
    }

    [Theory]
    [InlineData(1, false, EquipmentForgingRejection.Locked)]
    [InlineData(0, true, EquipmentForgingRejection.Expired)]
    public async Task Locked_or_expired_target_is_rejected(
        byte lockId, bool expired, EquipmentForgingRejection rejection)
    {
        using var shop = Fixture(0);
        var original = Piece() with { EquipmentLockId = lockId,
            ExpireTime = expired ? checked((int)shop.Clock.GetUtcNow().ToUnixTimeSeconds() - 1) : 0 };
        await shop.SeedCore(9, original);
        await shop.Seed(121, 3326, 50);
        Assert.Equal(rejection, (await Apply(shop)).Rejection);
        Assert.Equal(original, await Read(shop));
        Assert.Equal(50, shop.Count(3326));
    }

    [Fact]
    public async Task Wrong_target_or_material_or_insufficient_count_cannot_spend()
    {
        using var shop = Fixture(0);
        var original = Piece();
        await shop.SeedCore(9, original);
        await shop.Seed(121, 3333, 50);
        Assert.Equal(EquipmentForgingRejection.MaterialUnavailable,
            (await Apply(shop)).Rejection);
        Assert.Equal(original, await Read(shop));
        Assert.Equal(50, shop.Count(3333));
        await shop.Seed(121, 3326, 10);
        Assert.Equal(EquipmentForgingRejection.MaterialUnavailable,
            (await Apply(shop)).Rejection);
        Assert.Equal(10, shop.Count(3326));
        Assert.Equal(EquipmentForgingRejection.WrongItem,
            (await Apply(shop, expectedItemId: 3001)).Rejection);
        Assert.Equal(original, await Read(shop));
    }

    [Fact]
    public async Task Same_target_and_material_slot_cannot_spend()
    {
        using var shop = Fixture(0);
        var original = Piece();
        await shop.SeedCore(9, original);
        await shop.Seed(121, 3326, 50);

        Assert.Equal(EquipmentForgingRejection.InvalidRequest,
            (await Apply(shop, materialSlot: Target)).Rejection);
        Assert.Equal(original, await Read(shop));
        Assert.Equal(50, shop.Count(3326));
        Assert.Equal(100000, shop.Gold);
    }

    [Fact]
    public async Task Wrong_container_and_invalid_slot_cannot_spend()
    {
        using var shop = Fixture(0);
        var original = Piece();
        await shop.SeedCore(9, original);
        await shop.Seed(121, 3326, 50);

        Assert.Equal(EquipmentForgingRejection.InvalidRequest,
            (await Apply(shop, targetSlot: Material)).Rejection);
        Assert.Equal(EquipmentForgingRejection.InvalidRequest,
            (await Apply(shop, materialSlot: new(BusinessInventorySpace.Material, -1))).Rejection);
        Assert.Equal(original, await Read(shop));
        Assert.Equal(50, shop.Count(3326));
        Assert.Equal(100000, shop.Gold);
    }

    [Fact]
    public async Task Database_failure_rolls_back_material_and_target()
    {
        using var shop = Fixture(0);
        var original = Piece();
        await shop.SeedCore(9, original);
        await shop.Seed(121, 3326, 50);
        shop.Sql("CREATE TRIGGER refuse_forging BEFORE UPDATE ON items "
            + "WHEN NEW.item_core <> OLD.item_core AND NEW.item_kind=1 "
            + "BEGIN SELECT RAISE(ABORT,'forced forging failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Apply(shop).AsTask());
        Assert.Equal(original, await Read(shop));
        Assert.Equal(50, shop.Count(3326));
    }

    [Fact]
    public async Task Old_character_session_cannot_spend_after_switch()
    {
        using var shop = Fixture(0);
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 3326, 50);
        var old = shop.Session;
        shop.Select(2);
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using var tx = scope.Begin();
        var result = await shop.Services.GetRequiredService<IEquipmentForgingDomainService>()
            .ForgeAsync(old, Target, 3000, Material, default);
        Assert.Equal(EquipmentForgingRejection.NoSession, result.Rejection);
        Assert.Equal(50, shop.Scalar("SELECT COALESCE(SUM(count),0) FROM item_slots "
            + "WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=121"));
    }

    private static ShopFixture Fixture(int roll, int equipmentType = 0) => new(services =>
    {
        services.AddSingleton<IEquipmentForgingContentSource>(new Content());
        services.AddSingleton<IEquipmentReinforcementDefinitionSource>(new Equipment(equipmentType));
        services.AddSingleton<IEquipmentForgingRollSource>(new Roll(roll));
        services.AddSingleton<IEquipmentForgingRuleService, EquipmentForgingRuleService>();
        services.AddSingleton<IEquipmentForgingDomainService, EquipmentForgingDomainService>();
    });

    private static InventoryItemCoreState Piece() => StackableItemCore.Create(1, 3000, 1234)
        with { Durability = 45 };

    private static async ValueTask<InventoryItemCoreState?> Read(ShopFixture shop)
    {
        var bound = shop.Services.GetRequiredService<IBusinessInventorySlotDomainService>()
            .Get(shop.Session.CharacterId, Target);
        return (await bound.Container.ReadSlotAsync(bound.Slot.Index, default))?.Core;
    }

    private static async ValueTask<EquipmentForgingResult> Apply(
        ShopFixture shop, int expectedItemId = 3000,
        BusinessSlot? targetSlot = null, BusinessSlot? materialSlot = null)
    {
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using var tx = scope.Begin();
        var result = await shop.Services.GetRequiredService<IEquipmentForgingDomainService>()
            .ForgeAsync(shop.Session, targetSlot ?? Target, expectedItemId,
                materialSlot ?? Material, default);
        if (result.Rejection == EquipmentForgingRejection.None) await tx.CommitAsync(default);
        return result;
    }

    private sealed class Content : IEquipmentForgingContentSource
    {
        public ValueTask<EquipmentForgingTable?> GetAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentForgingTable?>(new(8,
                [new(1, 10000, 1), new(2, 5000, 1.11),
                 new(3, 5000, 1.21), new(4, 3000, 1.32),
                 new(5, 3000, 1.42), new(6, 1500, 1.53),
                 new(7, 1500, 1.63), new(8, 750, 1.74)],
                new Dictionary<int, EquipmentForgingMaterial> { [77] = new(3326, 39) },
                [0.4, 0.7, 1, 1.25, 1.4, 1.1, 1.3]));
    }

    private sealed class Equipment(int type) : IEquipmentReinforcementDefinitionSource
    {
        public ValueTask<EquipmentReinforcementEquipmentDefinition?> GetEquipmentAsync(
            int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementEquipmentDefinition?>(
                itemId == 3000 ? new(75, 77, 2, type, 45, true) : null);
        public ValueTask<EquipmentReinforcementTable?> GetNormalTableAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementTable?>(null);
        public ValueTask<EquipmentReinforcementProtectionDefinition?> GetProtectionAsync(
            int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementProtectionDefinition?>(null);
    }

    private sealed class Roll(int value) : IEquipmentForgingRollSource
    {
        public int Next() => value;
    }
}
