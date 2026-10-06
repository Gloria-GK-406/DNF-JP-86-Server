using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Outworld;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentOutworldDomainTests
{
    private static readonly BusinessSlot Target = new(BusinessInventorySpace.CarriedEquipment, 0);
    private static readonly BusinessSlot Material = new(BusinessInventorySpace.Material, 0);

    [Theory]
    [InlineData(1183, 0x80, 0, 0, 0, 1)] // purify rolls vitality with fixed seed
    [InlineData(10000408, 0x80, 0, 0, 0, 0)] // clear removes unidentified vigor
    public async Task Purify_and_clear_consume_one_and_preserve_uid(
        int materialId, byte oldType, ushort oldValue, byte selected, byte action, byte expectedType)
    {
        using var shop = Fixture();
        var original = Piece() with { AmplifyType = oldType, AmplifyValue = oldValue,
            EnchantCardId = 567, RandomOptionState = 3 };
        await shop.SeedCore(9, original);
        await shop.Seed(121, materialId, 2);
        var uid = shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' "
            + "AND owner_id=1 AND list_type=0 AND slot_index=9");

        var result = await Apply(shop, (EquipmentOutworldAction)action, selected, materialId);

        Assert.Equal(EquipmentOutworldRejection.None, result.Rejection);
        var changed = Assert.IsType<InventoryItemCoreState>(await Read(shop));
        Assert.Equal(original with { AmplifyType = expectedType,
            AmplifyValue = expectedType == 0 ? (ushort)0 : (ushort)5 }, changed);
        Assert.Equal(1, shop.Count(materialId));
        Assert.Equal(uid, shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' "
            + "AND owner_id=1 AND list_type=0 AND slot_index=9"));
        shop.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(shop.Session.CharacterId);
        shop.Select(1);
        Assert.Equal(changed, await Read(shop));
    }

    [Theory]
    [InlineData(EquipmentOutworldAction.Invest, 1184, 0, 3, 3)]
    [InlineData(EquipmentOutworldAction.Twist, 1188, 1, 3, 3)]
    public async Task Invest_and_twist_use_pvf_material_mapping(
        EquipmentOutworldAction action, int materialId, byte oldType, byte selected, byte expectedType)
    {
        using var shop = Fixture();
        var original = Piece() with { AmplifyType = oldType, AmplifyValue = oldType == 0 ? (ushort)0 : (ushort)5,
            Attribute = 0xA0, EnchantUpgradeCount = 7 };
        await shop.SeedCore(9, original);
        await shop.Seed(121, materialId, 1);

        var result = await Apply(shop, action, selected, materialId);

        Assert.Equal(EquipmentOutworldRejection.None, result.Rejection);
        Assert.Equal(original with { AmplifyType = expectedType, AmplifyValue = 5 }, await Read(shop));
        Assert.Equal(0, shop.Count(materialId));
    }

    [Theory]
    [InlineData(EquipmentOutworldAction.Invest, 1184, 0x80, 3, EquipmentOutworldRejection.AlreadyHasAmplifyOption)]
    [InlineData(EquipmentOutworldAction.Twist, 1188, 0, 3, EquipmentOutworldRejection.NoAmplifyOption)]
    [InlineData(EquipmentOutworldAction.Twist, 1188, 3, 3, EquipmentOutworldRejection.SameOption)]
    [InlineData(EquipmentOutworldAction.Purify, 1183, 3, 0, EquipmentOutworldRejection.InvalidState)]
    public async Task Invalid_state_cannot_spend(EquipmentOutworldAction action,
        int materialId, byte oldType, byte selected, EquipmentOutworldRejection rejection)
    {
        using var shop = Fixture();
        var original = Piece() with { AmplifyType = oldType };
        await shop.SeedCore(9, original);
        await shop.Seed(121, materialId, 1);
        var result = await Apply(shop, action, selected, materialId);
        Assert.Equal(rejection, result.Rejection);
        Assert.Equal(original, await Read(shop));
        Assert.Equal(1, shop.Count(materialId));
    }

    [Fact]
    public async Task All_option_material_uses_selected_attribute_but_rejects_zero()
    {
        using var shop = Fixture();
        var original = Piece();
        await shop.SeedCore(9, original);
        await shop.Seed(121, 1286, 2);
        var invalid = await Apply(shop, EquipmentOutworldAction.Invest, 0, 1286);
        Assert.Equal(EquipmentOutworldRejection.InvalidRequest, invalid.Rejection);
        Assert.Equal(original, await Read(shop));
        Assert.Equal(2, shop.Count(1286));

        var valid = await Apply(shop, EquipmentOutworldAction.Invest, 2, 1286);
        Assert.Equal(EquipmentOutworldRejection.None, valid.Rejection);
        Assert.Equal(original with { AmplifyType = 2, AmplifyValue = 5 }, await Read(shop));
        Assert.Equal(1, shop.Count(1286));
    }

    [Fact]
    public async Task Upgrade_level_blocks_twist_without_debit()
    {
        using var shop = Fixture();
        var original = Piece() with { AmplifyType = 1, AmplifyValue = 5,
            Attribute = 1 };
        await shop.SeedCore(9, original);
        await shop.Seed(121, 1188, 1);
        var result = await Apply(shop, EquipmentOutworldAction.Twist, 3, 1188);
        Assert.Equal(EquipmentOutworldRejection.AlreadyUpgraded, result.Rejection);
        Assert.Equal(original, await Read(shop));
        Assert.Equal(1, shop.Count(1188));
    }

    [Fact]
    public async Task Database_failure_rolls_back_material_and_target()
    {
        using var shop = Fixture();
        var original = Piece() with { AmplifyType = 0x80 };
        await shop.SeedCore(9, original);
        await shop.Seed(121, 1183, 2);
        shop.Sql("CREATE TRIGGER refuse_outworld BEFORE UPDATE ON items "
            + "WHEN NEW.item_core <> OLD.item_core AND NEW.item_kind=1 "
            + "BEGIN SELECT RAISE(ABORT,'forced outworld failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Apply(shop, EquipmentOutworldAction.Purify,
            0, 1183).AsTask());
        Assert.Equal(original, await Read(shop));
        Assert.Equal(2, shop.Count(1183));
    }

    [Fact]
    public async Task Wrong_template_and_insufficient_material_cannot_mutate_target()
    {
        using var shop = Fixture();
        var original = Piece() with { AmplifyType = 0x80 };
        await shop.SeedCore(9, original);
        await shop.Seed(121, 1183, 1);
        var service = shop.Services.GetRequiredService<IEquipmentOutworldDomainService>();
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using (var tx = scope.Begin())
        {
            var wrongTarget = await service.ApplyAsync(shop.Session,
                EquipmentOutworldAction.Purify, Target, 3001, Material, 1183, 0, default);
            var wrongMaterial = await service.ApplyAsync(shop.Session,
                EquipmentOutworldAction.Purify, Target, 3000, Material, 1184, 0, default);
            Assert.Equal(EquipmentOutworldRejection.WrongItem, wrongTarget.Rejection);
            Assert.Equal(EquipmentOutworldRejection.MaterialUnavailable, wrongMaterial.Rejection);
        }
        Assert.Equal(original, await Read(shop));
        Assert.Equal(1, shop.Count(1183));
        await shop.Seed(121, 1183, 0);
        Assert.Equal(EquipmentOutworldRejection.MaterialUnavailable,
            (await Apply(shop, EquipmentOutworldAction.Purify, 0, 1183)).Rejection);
        Assert.Equal(original, await Read(shop));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public async Task Locked_or_expired_target_is_rejected(byte lockId, int expired)
    {
        using var shop = Fixture();
        var original = Piece() with { AmplifyType = 0x80, EquipmentLockId = lockId,
            ExpireTime = expired == 0 ? 0
                : checked((int)shop.Clock.GetUtcNow().ToUnixTimeSeconds() - 1) };
        await shop.SeedCore(9, original);
        await shop.Seed(121, 1183, 1);
        var result = await Apply(shop, EquipmentOutworldAction.Purify, 0, 1183);
        Assert.Equal(lockId == 0 ? EquipmentOutworldRejection.ItemUnavailable
            : EquipmentOutworldRejection.Locked, result.Rejection);
        Assert.Equal(original, await Read(shop));
        Assert.Equal(1, shop.Count(1183));
    }

    [Fact]
    public async Task Old_character_session_cannot_spend_after_character_switch()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece() with { AmplifyType = 0x80 });
        await shop.Seed(121, 1183, 1);
        var old = shop.Session;
        shop.Select(2);
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using var tx = scope.Begin();
        var result = await shop.Services.GetRequiredService<IEquipmentOutworldDomainService>()
            .ApplyAsync(old, EquipmentOutworldAction.Purify, Target, 3000,
                Material, 1183, 0, default);
        Assert.Equal(EquipmentOutworldRejection.NoSession, result.Rejection);
        Assert.Equal(1, shop.Scalar("SELECT COALESCE(SUM(count),0) FROM item_slots "
            + "WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=121"));
    }

    [Fact]
    public async Task Completion_uses_current_authoritative_slots_even_when_old_result_is_replayed()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece() with { AmplifyType = 0x80 });
        await shop.Seed(121, 1183, 2);
        var result = await Apply(shop, EquipmentOutworldAction.Purify, 0, 1183);
        var receipt = Assert.IsType<EquipmentOutworldReceipt>(result.Receipt);
        await shop.Seed(121, 1183, 7);
        await shop.SeedCore(9, Piece() with { AmplifyType = 4, AmplifyValue = 5 });
        var handler = ActivatorUtilities.CreateInstance<OutworldCompletedHandler>(shop.Services);
        var messages = await handler.ComposeAsync(
            new NotificationRecipient(shop.Session.CharacterId, shop.Connection.ConnectionId,
                shop.Session), new OutworldCompleted(receipt, true, 0), default);
        Assert.Equal(3, messages.Count);
        var response = Assert.IsType<PurifyItemResponse>(messages[0]);
        Assert.Equal(7, response.MaterialRemaining);
        Assert.Equal((byte)4, response.AmplifyType);
    }

    private static ShopFixture Fixture() => new(services =>
    {
        services.AddSingleton<IEquipmentOutworldContentSource>(new Content());
        services.AddSingleton<IEquipmentReinforcementDefinitionSource>(new Equipment());
        services.AddSingleton<IEquipmentDefinitionSource>(new Equipment());
        services.AddSingleton<IEquipmentOutworldSeedSource>(new Seed());
        services.AddSingleton<IEquipmentOutworldDomainService, EquipmentOutworldDomainService>();
    });

    private static InventoryItemCoreState Piece() => StackableItemCore.Create(1, 3000, 1234)
        with { Durability = 45 };

    private static async ValueTask<InventoryItemCoreState?> Read(ShopFixture shop)
    {
        var bound = shop.Services.GetRequiredService<IBusinessInventorySlotDomainService>()
            .Get(shop.Session.CharacterId, Target);
        return (await bound.Container.GetAsync(bound.Slot.Index, default))?.Snapshot.ToCore();
    }

    private static async ValueTask<EquipmentOutworldResult> Apply(ShopFixture shop,
        EquipmentOutworldAction action, byte selected, int materialId)
    {
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using var tx = scope.Begin();
        var result = await shop.Services.GetRequiredService<IEquipmentOutworldDomainService>()
            .ApplyAsync(shop.Session, action, Target, 3000, Material, materialId, selected, default);
        if (result.Rejection == EquipmentOutworldRejection.None) await tx.CommitAsync(default);
        return result;
    }

    private sealed class Content : IEquipmentOutworldContentSource
    {
        public ValueTask<EquipmentOutworldTable?> GetAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentOutworldTable?>(new(55,
                new Dictionary<int, int> { [1183] = 1 },
                new Dictionary<int, int> { [10000408] = 1 },
                new Dictionary<int, EquipmentOutworldMaterialDefinition>
                    { [1184] = new(3, 1), [1286] = new(0, 1) },
                new Dictionary<int, EquipmentOutworldMaterialDefinition> { [1188] = new(3, 1) },
                new Dictionary<int, IReadOnlyDictionary<byte, ushort>>
                    { [2] = new Dictionary<byte, ushort> { [1] = 5, [2] = 5, [3] = 5, [4] = 5 } },
                [new(1, 1_000_000), new(2, 1), new(3, 1), new(4, 1)]));
    }

    private sealed class Equipment : IEquipmentReinforcementDefinitionSource, IEquipmentDefinitionSource
    {
        public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(0);

        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(
            int itemId, CancellationToken ct) => ValueTask.FromResult<EquipmentDefinition?>(
                itemId == 3000 ? new(75, 1, 2, "weapon", 45) : null);
        public ValueTask<EquipmentReinforcementTable?> GetAmplificationTableAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementTable?>(null);

        public ValueTask<EquipmentReinforcementTable?> GetNormalTableAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementTable?>(null);
        public ValueTask<EquipmentReinforcementProtectionDefinition?> GetProtectionAsync(
            int itemId, CancellationToken ct, EquipmentUpgradeKind kind = EquipmentUpgradeKind.Reinforcement) =>
            ValueTask.FromResult<EquipmentReinforcementProtectionDefinition?>(null);
    }

    private sealed class Seed : IEquipmentOutworldSeedSource
    {
        public int NextSeed() => 0;
    }
}
