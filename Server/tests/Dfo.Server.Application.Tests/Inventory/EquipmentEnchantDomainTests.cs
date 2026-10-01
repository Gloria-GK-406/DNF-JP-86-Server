using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentEnchantDomainTests
{
    private static readonly BusinessSlot Carried = new(BusinessInventorySpace.CarriedEquipment, 0);
    private static readonly BusinessSlot Worn = new(BusinessInventorySpace.WornEquipment, 0);
    private static readonly BusinessSlot Bead = new(BusinessInventorySpace.Material, 0);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bead_commits_original_uid_and_only_card_field_for_carried_or_worn_equipment(bool worn)
    {
        using var shop = Fixture();
        var original = Piece() with { EnchantCardId = 42, Attribute = 0xA7,
            EnchantUpgradeCount = 9, RandomOptionState = 7 };
        await shop.SeedCore(worn ? 12 : 9, original,
            worn ? InventoryContainerType.Equipment : InventoryContainerType.Main);
        await shop.Seed(121, 1000, 2, 3);
        var uid = shop.Scalar($"SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type={(worn ? 3 : 0)} AND slot_index={(worn ? 12 : 9)}");

        var result = await Enchant(shop, worn ? Worn : Carried);

        Assert.Equal(EquipmentEnchantRejection.None, result.Rejection);
        Assert.Equal(original with { EnchantCardId = 3600 }, await Read(shop, worn ? Worn : Carried));
        Assert.Equal(1, shop.Count(1000));
        Assert.Equal(uid, shop.Scalar($"SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type={(worn ? 3 : 0)} AND slot_index={(worn ? 12 : 9)}"));
        shop.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(shop.Session.CharacterId);
        shop.Select(1);
        Assert.Equal(original with { EnchantCardId = 3600 }, await Read(shop, worn ? Worn : Carried));
    }

    [Theory]
    [InlineData(1, 0, EquipmentEnchantRejection.Locked)]
    [InlineData(0, 1, EquipmentEnchantRejection.UnsupportedContent)]
    public async Task Invalid_target_or_bead_leaves_both_assets_unchanged(
        byte lockId, byte beadUpgrade, EquipmentEnchantRejection rejection)
    {
        using var shop = Fixture();
        var target = Piece() with { EquipmentLockId = lockId };
        await shop.SeedCore(9, target);
        await shop.SeedCore(121, StackableItemCore.Create(3, 1000, 2)
            with { EnchantUpgradeCount = beadUpgrade });

        var result = await Enchant(shop, Carried);

        Assert.Equal(rejection, result.Rejection);
        Assert.Equal(target, await Read(shop, Carried));
        Assert.Equal(2, shop.Count(1000));
    }

    [Fact]
    public async Task Pet_target_is_rejected_without_spending()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 2, 3);
        var result = await Enchant(shop, new(BusinessInventorySpace.WornCreature, 0));
        Assert.Equal(EquipmentEnchantRejection.InvalidRequest, result.Rejection);
        Assert.Equal(2, shop.Count(1000));
    }

    [Fact]
    public async Task Missing_or_wrong_material_cannot_change_target()
    {
        using var shop = Fixture();
        var target = Piece();
        await shop.SeedCore(9, target);
        var missing = await Enchant(shop, Carried);
        Assert.Equal(EquipmentEnchantRejection.MaterialUnavailable, missing.Rejection);
        await shop.Seed(121, 4000, 2);
        var wrong = await Enchant(shop, Carried);
        Assert.Equal(EquipmentEnchantRejection.UnsupportedContent, wrong.Rejection);
        Assert.Equal(target, await Read(shop, Carried));
        Assert.Equal(2, shop.Count(4000));
    }

    [Fact]
    public async Task Expired_rental_equipment_cannot_be_enchanted()
    {
        using var shop = Fixture();
        var target = Piece() with
        {
            ExpireTime = checked((int)shop.Clock.GetUtcNow().ToUnixTimeSeconds() - 1),
        };
        await shop.SeedCore(9, target);
        await shop.Seed(121, 1000, 2, 3);

        var result = await Enchant(shop, Carried);

        Assert.Equal(EquipmentEnchantRejection.ItemUnavailable, result.Rejection);
        Assert.Equal(target, await Read(shop, Carried));
        Assert.Equal(2, shop.Count(1000));
    }

    [Fact]
    public async Task Old_character_session_cannot_spend_after_character_switch()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 2, 3);
        var old = shop.Session;
        shop.Select(2);
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using var tx = scope.Begin();
        var result = await shop.Services.GetRequiredService<IEquipmentEnchantDomainService>()
            .EnchantAsync(old, Bead, Carried, default);
        Assert.Equal(EquipmentEnchantRejection.NoSession, result.Rejection);
        Assert.Equal(2, shop.Scalar("SELECT COALESCE(SUM(count),0) FROM item_slots "
            + "WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=121"));
    }

    [Fact]
    public async Task Failed_equipment_commit_rolls_back_bead_debit()
    {
        using var shop = Fixture();
        var original = Piece();
        await shop.SeedCore(9, original);
        await shop.Seed(121, 1000, 2, 3);
        shop.Sql("CREATE TRIGGER refuse_enchant BEFORE UPDATE ON items "
            + "WHEN NEW.item_core <> OLD.item_core BEGIN SELECT RAISE(ABORT,'forced enchant failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Enchant(shop, Carried).AsTask());
        Assert.Equal(original, await Read(shop, Carried));
        Assert.Equal(2, shop.Count(1000));
    }

    private static ShopFixture Fixture() => new(services =>
    {
        services.AddSingleton<IEquipmentEnchantContentSource>(new Content());
        services.AddSingleton<IEquipmentEnchantDomainService, EquipmentEnchantDomainService>();
    });

    private static InventoryItemCoreState Piece() => StackableItemCore.Create(1, 3000, 1234)
        with { Durability = 45 };

    private static async ValueTask<InventoryItemCoreState?> Read(ShopFixture shop, BusinessSlot slot)
    {
        var bound = shop.Services.GetRequiredService<IBusinessInventorySlotDomainService>()
            .Get(shop.Session.CharacterId, slot);
        return (await bound.Container.ReadSlotAsync(bound.Slot.Index, default))?.Core;
    }

    private static async ValueTask<EquipmentEnchantResult> Enchant(ShopFixture shop, BusinessSlot target)
    {
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using var tx = scope.Begin();
        var result = await shop.Services.GetRequiredService<IEquipmentEnchantDomainService>()
            .EnchantAsync(shop.Session, Bead, target, default);
        if (result.Rejection == EquipmentEnchantRejection.None)
            await tx.CommitAsync(default);
        return result;
    }

    private sealed class Content : IEquipmentEnchantContentSource
    {
        public ValueTask<EquipmentEnchantDefinition?> ResolveAsync(int beadItemId,
            int equipmentItemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentEnchantDefinition?>(
                beadItemId == 1000 && equipmentItemId == 3000
                    ? new(beadItemId, 3600) : null);
    }
}
