using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentMagicSealDomainTests
{
    private static readonly BusinessSlot Target = new(BusinessInventorySpace.CarriedEquipment, 0);
    private static readonly BusinessSlot Scroll = new(BusinessInventorySpace.Material, 0);

    [Fact]
    public async Task Unseal_changes_only_random_options_and_gold_and_persists_the_original_uid()
    {
        using var shop = Fixture();
        var original = StackableItemCore.Create(1, 3000, 1234) with
        {
            EnchantCardId = 3600,
            Attribute = 0xA3,
            EnchantUpgradeCount = 8,
            SealFlag = 1,
        };
        await shop.SeedCore(9, original);
        var uid = shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' "
            + "AND owner_id=1 AND list_type=0 AND slot_index=9");

        var result = await Unseal(shop);

        Assert.Equal(EquipmentMagicSealRejection.None, result.Rejection);
        Assert.Equal(original with { RandomOption0 = new(15, 2, 2) }, await Read(shop));
        Assert.Equal(99500, shop.Gold);
        Assert.Equal(uid, shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' "
            + "AND owner_id=1 AND list_type=0 AND slot_index=9"));
        shop.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(shop.Session.CharacterId);
        shop.Select(1);
        Assert.Equal(original with { RandomOption0 = new(15, 2, 2) }, await Read(shop));
    }

    [Fact]
    public async Task Change_replaces_only_selected_option_and_keeps_uid_and_other_fields()
    {
        using var shop = Fixture();
        var original = StackableItemCore.Create(1, 3000, 1234) with
        {
            RandomOption0 = new(15, 2, 2),
            EnchantCardId = 3600,
            SealFlag = 1,
        };
        await shop.SeedCore(9, original);
        var uid = shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' "
            + "AND owner_id=1 AND list_type=0 AND slot_index=9");

        var result = await Apply(shop, true, null, 0);

        Assert.Equal(EquipmentMagicSealRejection.None, result.Rejection);
        Assert.Equal(original with { RandomOption0 = new(16, 4, 4),
            RandomOptionChangedIndex = byte.MaxValue }, await Read(shop));
        Assert.Equal(99800, shop.Gold);
        Assert.Equal(uid, shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' "
            + "AND owner_id=1 AND list_type=0 AND slot_index=9"));
    }

    [Fact]
    public async Task Rejects_wrong_owner_locked_state_and_insufficient_gold_without_debit()
    {
        using var shop = Fixture();
        var original = StackableItemCore.Create(1, 3000, 1234);
        await shop.SeedCore(9, original);
        shop.Services.GetRequiredService<ICharacterContainerDomainService>()
            .Clear(shop.Session.CharacterId);
        shop.Select(2);
        Assert.Equal(EquipmentMagicSealRejection.ItemUnavailable,
            (await Unseal(shop)).Rejection);
        Assert.Equal(100000, shop.Gold);
        shop.Services.GetRequiredService<ICharacterContainerDomainService>()
            .Clear(shop.Session.CharacterId);
        shop.Select(1);
        await shop.SeedCore(9, original with { EquipmentLockId = 1 });
        Assert.Equal(EquipmentMagicSealRejection.Locked,
            (await Unseal(shop)).Rejection);
        await shop.SeedCore(9, original);
        shop.Sql("UPDATE character_stats SET carried_gold=1 WHERE character_id=1");
        Assert.Equal(EquipmentMagicSealRejection.InsufficientGold,
            (await Unseal(shop)).Rejection);
        Assert.Equal(original, await Read(shop));
        Assert.Equal(1, shop.Gold);
    }

    [Fact]
    public async Task State_and_index_rejections_keep_existing_options_and_gold()
    {
        using var shop = Fixture();
        var original = StackableItemCore.Create(1, 3000, 1234);
        await shop.SeedCore(9, original);
        Assert.Equal(EquipmentMagicSealRejection.NotUnsealed,
            (await Apply(shop, true, null, 0)).Rejection);
        await shop.SeedCore(9, original with { RandomOption0 = new(15, 2, 2) });
        Assert.Equal(EquipmentMagicSealRejection.AlreadyUnsealed,
            (await Unseal(shop)).Rejection);
        Assert.Equal(EquipmentMagicSealRejection.InvalidOption,
            (await Apply(shop, true, null, 1)).Rejection);
        Assert.Equal(100000, shop.Gold);
    }

    [Fact]
    public async Task Expired_or_wrong_business_slot_cannot_unseal()
    {
        using var shop = Fixture();
        var expired = StackableItemCore.Create(1, 3000, 1234) with
        {
            ExpireTime = checked((int)shop.Clock.GetUtcNow().ToUnixTimeSeconds() - 1),
        };
        await shop.SeedCore(9, expired);
        Assert.Equal(EquipmentMagicSealRejection.ItemUnavailable,
            (await Unseal(shop)).Rejection);
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using var tx = scope.Begin();
        Assert.Equal(EquipmentMagicSealRejection.InvalidRequest,
            (await shop.Services.GetRequiredService<IEquipmentMagicSealDomainService>()
                .UnsealAsync(shop.Session,
                    new(BusinessInventorySpace.WornCreature, 0), null, default)).Rejection);
        Assert.Equal(expired, await Read(shop));
        Assert.Equal(100000, shop.Gold);
    }

    [Fact]
    public async Task Dropped_transaction_does_not_persist_gold_or_equipment()
    {
        using var shop = Fixture();
        var original = StackableItemCore.Create(1, 3000, 1234);
        await shop.SeedCore(9, original);
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using (var tx = scope.Begin())
        {
            var result = await shop.Services.GetRequiredService<IEquipmentMagicSealDomainService>()
                .UnsealAsync(shop.Session, Target, null, default);
            Assert.Equal(EquipmentMagicSealRejection.None, result.Rejection);
        }
        Assert.Equal(original, await Read(shop));
        Assert.Equal(100000, shop.Gold);
    }

    [Fact]
    public async Task Unproven_second_field_cannot_consume_a_scroll()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, StackableItemCore.Create(1, 3000, 1234));
        await shop.Seed(121, 4000, 2, 3);

        Assert.Equal(EquipmentMagicSealRejection.InvalidRequest,
            (await Apply(shop, false, Scroll, 0)).Rejection);
        Assert.Equal(2, shop.Count(4000));
        Assert.Equal(100000, shop.Gold);
    }

    [Fact]
    public async Task Forced_sqlite_failure_rolls_back_gold_and_target()
    {
        using var shop = Fixture();
        var original = StackableItemCore.Create(1, 3000, 1234);
        await shop.SeedCore(9, original);
        shop.Sql("CREATE TRIGGER refuse_magic_seal BEFORE UPDATE ON items "
            + "WHEN NEW.item_core <> OLD.item_core BEGIN SELECT RAISE(ABORT,'forced seal failure'); END;");

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
            () => Apply(shop, false, null, 0).AsTask());

        Assert.Equal(original, await Read(shop));
        Assert.Equal(100000, shop.Gold);
    }

    [Fact]
    public async Task Same_explicit_seed_reproduces_the_same_rule_decision()
    {
        var rules = new EquipmentMagicSealRuleService(new Content());
        var empty = new RandomOptionState[] { new(0, 0, 0), new(0, 0, 0), new(0, 0, 0) };
        var first = await rules.DecideAsync(3000, empty, null, 12345, default);
        var second = await rules.DecideAsync(3000, empty, null, 12345, default);
        Assert.Equal(first.Rejection, second.Rejection);
        Assert.Equal(first.GoldCost, second.GoldCost);
        Assert.Equal(first.Options, second.Options);
        Assert.Equal(12345, first.Seed);
    }

    private static ShopFixture Fixture() => new(services =>
    {
        services.AddSingleton<IEquipmentMagicSealContentSource>(new Content());
        services.AddSingleton<IEquipmentMagicSealSeedSource>(new Seed());
        services.AddSingleton<IEquipmentMagicSealDomainService, EquipmentMagicSealDomainService>();
    });

    private static async ValueTask<InventoryItemCoreState?> Read(ShopFixture shop)
    {
        var bound = shop.Services.GetRequiredService<IBusinessInventorySlotDomainService>()
            .Get(shop.Session.CharacterId, Target);
        return (await bound.Container.GetAsync(bound.Slot.Index, default))?.Snapshot.ToCore();
    }

    private static async ValueTask<EquipmentMagicSealResult> Unseal(ShopFixture shop)
        => await Apply(shop, false, null, 0);

    private static async ValueTask<EquipmentMagicSealResult> Apply(
        ShopFixture shop, bool change, BusinessSlot? material, byte index)
    {
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using var tx = scope.Begin();
        var service = shop.Services.GetRequiredService<IEquipmentMagicSealDomainService>();
        var result = change
            ? await service.ChangeAsync(shop.Session, Target, material, index, default)
            : await service.UnsealAsync(shop.Session, Target, material, default);
        if (result.Rejection == EquipmentMagicSealRejection.None)
            await tx.CommitAsync(default);
        return result;
    }

    private sealed class Content : IEquipmentMagicSealContentSource
    {
        public ValueTask<EquipmentMagicSealDefinition?> GetEquipmentAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentMagicSealDefinition?>(itemId == 3000
                ? new(3000, 2, 75, 500, 200,
                    [new(1, 1)],
                    [[new(15, 2, 3, 1)]],
                    [[new(16, 4, 5, 1)]], [new(2, 2, 1)], [new(4, 4, 1)]) : null);

        public ValueTask<MagicSealScrollDefinition?> GetScrollAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<MagicSealScrollDefinition?>(itemId switch
            {
                4000 => new(4000, 30),
                4001 => new(4001, 99),
                _ => null,
            });
    }

    private sealed class Seed : IEquipmentMagicSealSeedSource
    {
        public int NextSeed() => 12345;
    }
}
