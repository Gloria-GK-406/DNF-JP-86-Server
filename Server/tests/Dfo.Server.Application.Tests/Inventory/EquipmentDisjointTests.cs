using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.DisjointItem;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentDisjointTests
{
    [Theory]
    [InlineData(65)]
    [InlineData(3)]
    public async Task Exhausted_portable_machine_is_cleared_before_success_ack(short toolSlot)
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(toolSlot, 2600469, 1);

        await EffectRequestTransport.Send(shop, new DisjointItemRequest(new(1, 0x001A, 0, 90),
            new(InventoryContainerType.Main, 9), toolSlot, 0));

        var encoders = shop.Services.GetServices<ServerMessageEncoderRegistration>()
            .ToDictionary(registration => registration.MessageType);
        var frames = shop.Sender.Messages.SelectMany(message =>
            ((IServerMessageEncoder)shop.Services.GetRequiredService(encoders[message.GetType()].EncoderType))
                .Encode(message)).Select(frame => frame.ToArray()).ToArray();
        // NOTI 000E must clear the tool before CMD 001A checks the native window's tool pointer.
        Assert.Equal((byte)0, frames[0][0]);
        Assert.Equal((ushort)0x000E, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(frames[0].AsSpan(1)));
        Assert.Equal(toolSlot, System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(frames[0].AsSpan(18)));
        Assert.Equal(-1, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(frames[0].AsSpan(20)));
        Assert.Equal((byte)1, frames[1][0]);
        Assert.Equal((ushort)0x001A, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(frames[1].AsSpan(1)));
        Assert.Equal((short)9, System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(frames[2].AsSpan(18)));
        Assert.Single(frames, frame => frame[0] == 0
            && System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == 0x000E
            && System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(18)) == toolSlot);
        Assert.Equal(0, shop.Count(2600469));
        Assert.Equal(12, shop.Count(1000));
        Assert.Null(await shop.Durability(InventoryContainerType.Main, 9));
    }

    [Fact]
    public async Task Last_portable_machine_cannot_turn_a_later_request_into_free_npc_disjoint()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.SeedCore(10, Piece());
        await shop.Seed(65, 2600469, 1);

        await EffectRequestTransport.Send(shop, new DisjointItemRequest(new(1, 0x001A, 0, 80),
            new(InventoryContainerType.Main, 9), 65, 0));
        Assert.Null(await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(0, shop.Count(2600469));
        Assert.Equal(12, shop.Count(1000));

        // A21 clears the exhausted tool pointer but keeps the portable window's zero provider.
        await EffectRequestTransport.Send(shop, new DisjointItemRequest(new(1, 0x001A, 0, 81),
            new(InventoryContainerType.Main, 10), -1, 0));
        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 10));
        Assert.Equal(12, shop.Count(1000));
        Assert.Equal(0, shop.Count(2600469));
        var responses = shop.Sender.Messages.OfType<DisjointItemResponse>().ToArray();
        Assert.Equal(2, responses.Length);
        Assert.NotNull(responses[0].TargetSlot);
        Assert.Null(responses[1].TargetSlot);
        Assert.Equal(0x13, responses[1].ErrorCode);
        await Assert.ThrowsAsync<InvalidDataException>(() => EffectRequestTransport.Send(shop,
            new DisjointItemRequest(new(1, 0x001A, 0, 81), new(InventoryContainerType.Main, 10), -1, 0)).AsTask());
        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 10));
        Assert.Equal(12, shop.Count(1000));

        await EffectRequestTransport.Send(shop, new DisjointItemRequest(new(1, 0x001A, 0, 82),
            new(InventoryContainerType.Main, 10), -1, 1000));
        Assert.Null(await shop.Durability(InventoryContainerType.Main, 10));
        Assert.Equal(24, shop.Count(1000));
    }

    [Theory]
    [InlineData(-2, 0)]
    [InlineData(-1, 0)]
    [InlineData(-1, -1)]
    [InlineData(-1, int.MinValue)]
    [InlineData(65, 1000)]
    public async Task Missing_or_ambiguous_disjoint_provider_never_removes_equipment_or_grants_rewards(short toolSlot, int provider)
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(65, 2600469, 1);
        await Disjoint(shop, 9, provider, toolSlot);
        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(1, shop.Count(2600469));
        Assert.Equal(0, shop.Count(1000));
        var response = Assert.IsType<DisjointItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.Null(response.TargetSlot);
        Assert.Equal(0x13, response.ErrorCode);
    }

    [Fact]
    public async Task Last_portable_machine_with_a_stale_tool_slot_is_also_refused()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.SeedCore(10, Piece());
        await shop.Seed(65, 2600469, 1);
        await Disjoint(shop, 9, disjointToolSlot: 65);
        await Disjoint(shop, 10, disjointToolSlot: 65);
        Assert.Null(await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 10));
        Assert.Equal(0, shop.Count(2600469));
        Assert.Equal(12, shop.Count(1000));
        Assert.Equal(0x13, shop.Sender.Messages.OfType<DisjointItemResponse>().Last().ErrorCode);
    }

    [Fact]
    public async Task One_transaction_removes_the_piece_grants_materials_and_then_acknowledges()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        shop.Sender.BeforeSend = () =>
        {
            Assert.Null(shop.Durability(InventoryContainerType.Main, 9)
                .AsTask().GetAwaiter().GetResult());
            Assert.Equal(12, shop.Count(1000));
        };

        await Disjoint(shop, 9);

        var response = Assert.Single(shop.Sender.Messages.OfType<DisjointItemResponse>());
        Assert.Equal((9, InventoryContainerType.Main),
            (response.TargetSlot!.Value.Index, response.TargetSlot.Value.Space));
        Assert.Equal(new DisjointItemMaterial(
            new GameSlot(InventoryContainerType.Main, 121), 1000, 12),
            Assert.Single(response.Materials!));
    }

    [Fact]
    public async Task Cube_reward_updates_the_account_virtual_bag_instead_of_character_materials()
    {
        using var shop = Fixture(rewards: [new(3037, 12)]);
        await shop.SeedCore(9, Piece());

        await Disjoint(shop, 9);

        Assert.Null(await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(0, shop.Count(3037));
        Assert.Equal(12, shop.SharedCount(3037, 1));
        var response = Assert.Single(shop.Sender.Messages.OfType<DisjointItemResponse>());
        Assert.Equal(
            new DisjointItemMaterial(
                new GameSlot(InventoryContainerType.Main, 358), 3037, 12),
            Assert.Single(response.Materials!));
    }

    [Fact]
    public async Task Rarity_soul_reward_updates_the_account_virtual_bag()
    {
        using var shop = Fixture(rewards: [new(10100115, 2)]);
        await shop.SeedCore(9, Piece());

        await Disjoint(shop, 9);

        Assert.Equal(0, shop.Count(10100115));
        Assert.Equal(2, shop.SharedCount(10100115, 1));
        var response = Assert.Single(shop.Sender.Messages.OfType<DisjointItemResponse>());
        Assert.Equal(
            new DisjointItemMaterial(
                new GameSlot(InventoryContainerType.Main, 360), 10100115, 2),
            Assert.Single(response.Materials!));
    }

    [Fact]
    public async Task Universe_soul_remains_an_ordinary_character_material()
    {
        using var shop = Fixture(rewards: [new(3257, 3)]);
        await shop.SeedCore(9, Piece());

        await Disjoint(shop, 9);

        Assert.Equal(3, shop.Count(3257));
        Assert.Equal(0, shop.Scalar("SELECT soul_common+soul_advanced+soul_rare+soul_unique+soul_legendary "
            + "FROM accounts WHERE account_id=1"));
        var material = Assert.Single(
            Assert.Single(shop.Sender.Messages.OfType<DisjointItemResponse>()).Materials!);
        Assert.Equal(3257, material.ItemId);
        Assert.Equal(InventoryContainerType.Main, material.Slot.Space);
        Assert.InRange(material.Slot.Index, 121, 185);
    }

    [Fact]
    public async Task Mixed_rewards_settle_character_and_account_materials_together()
    {
        using var shop = Fixture(rewards:
        [
            new(1000, 5),
            new(3037, 12),
            new(10100115, 2),
            new(3257, 3),
        ]);
        await shop.SeedCore(9, Piece());

        await Disjoint(shop, 9);

        Assert.Equal(5, shop.Count(1000));
        Assert.Equal(3, shop.Count(3257));
        Assert.Equal(0, shop.Count(3037));
        Assert.Equal(0, shop.Count(10100115));
        Assert.Equal(12, shop.SharedCount(3037, 1));
        Assert.Equal(2, shop.SharedCount(10100115, 1));
        Assert.Equal(
            [1000, 3037, 3257, 10100115],
            Assert.Single(shop.Sender.Messages.OfType<DisjointItemResponse>()).Materials!
                .Select(material => material.ItemId)
                .OrderBy(itemId => itemId));
    }

    [Fact]
    public async Task Account_only_reward_does_not_need_a_free_character_material_slot()
    {
        using var shop = Fixture(rewards: [new(3037, 12)]);
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 1000, 3);
        await shop.Seed(122, 1000, 1000, 3);
        for (var slot = 3; slot <= 8; slot++)
            await shop.Seed(slot, 2000, 10);

        await Disjoint(shop, 9);

        Assert.Null(await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(12, shop.SharedCount(3037, 1));
    }

    [Fact]
    public async Task Account_material_failure_rolls_back_piece_and_account_balance()
    {
        using var shop = Fixture(rewards: [new(3037, 12)]);
        await shop.SeedCore(9, Piece());
        shop.Sql("CREATE TRIGGER refuse_account_material BEFORE INSERT ON item_slots "
            + "WHEN NEW.owner_scope='account' AND NEW.list_type=240 BEGIN SELECT RAISE(ABORT,'forced account material failure'); END;");

        await Assert.ThrowsAsync<SqliteException>(() => Disjoint(shop, 9).AsTask());

        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(0, shop.SharedCount(3037, 1));
        Assert.Empty(shop.Sender.Messages);
    }

    [Fact]
    public async Task Portable_account_material_failure_also_rolls_back_the_tool()
    {
        using var shop = Fixture(rewards: [new(3037, 12)]);
        await shop.SeedCore(9, Piece());
        await shop.Seed(65, 2600469, 2);
        shop.Sql("CREATE TRIGGER refuse_portable_account_material BEFORE INSERT ON item_slots "
            + "WHEN NEW.owner_scope='account' AND NEW.list_type=240 BEGIN SELECT RAISE(ABORT,'forced account material failure'); END;");

        await Assert.ThrowsAsync<SqliteException>(() =>
            Disjoint(shop, 9, disjointToolSlot: 65).AsTask());

        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(2, shop.Count(2600469));
        Assert.Equal(0, shop.SharedCount(3037, 1));
        Assert.Empty(shop.Sender.Messages);
    }

    [Fact]
    public async Task The_same_account_exposes_one_material_bag_to_both_characters()
    {
        using var shop = Fixture(rewards: [new(3037, 12)]);
        var first = shop.Shared();
        await shop.SeedCore(9, Piece());
        await Disjoint(shop, 9);

        shop.Select(2);
        var second = shop.Shared();

        Assert.Equal(first.StorageId, second.StorageId);
        Assert.Equal(12, (await second.GetAsync(4, default))!.Count);
    }

    [Fact]
    public async Task Full_material_band_keeps_the_source_and_returns_inventory_full()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 1000, 3);
        await shop.Seed(122, 1000, 1000, 3);
        for (var slot = 3; slot <= 8; slot++)
            await shop.Seed(slot, 2000, 10);

        await Disjoint(shop, 9);

        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(2000, shop.Count(1000));
        var response = Assert.IsType<DisjointItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.Null(response.TargetSlot);
        Assert.Equal(0x04, response.ErrorCode);
    }

    [Fact]
    public async Task Failed_material_commit_rolls_back_the_source_and_emits_no_ack()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        shop.Sql("CREATE TRIGGER refuse_disjoint_reward BEFORE INSERT ON item_slots WHEN NEW.id = 1000 BEGIN SELECT RAISE(ABORT,'forced disjoint failure'); END;");

        await Assert.ThrowsAsync<SqliteException>(() => Disjoint(shop, 9).AsTask());

        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(0, shop.Count(1000));
        Assert.Empty(shop.Sender.Messages);
    }

    [Fact]
    public async Task Batch_ui_sequence_is_two_independent_single_item_transactions()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.SeedCore(10, Piece());

        await Disjoint(shop, 9);
        await Disjoint(shop, 10);

        Assert.Null(await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Null(await shop.Durability(InventoryContainerType.Main, 10));
        Assert.Equal(24, shop.Count(1000));
        Assert.Equal(2, shop.Sender.Messages.OfType<DisjointItemResponse>().Count());
    }

    [Fact]
    public async Task Portable_disjoint_consumes_one_tool_and_settles_the_piece_together()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(65, 2600469, 2);

        await Disjoint(shop, 9, disjointToolSlot: 65);

        Assert.Null(await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(1, shop.Count(2600469));
        Assert.Equal(12, shop.Count(1000));
        Assert.NotNull(Assert.Single(shop.Sender.Messages.OfType<DisjointItemResponse>()).TargetSlot);
        Assert.IsType<DisjointItemResponse>(shop.Sender.Messages[0]);
        Assert.Equal(2, shop.Sender.Messages.Count(message =>
            message.MessageType == ServerMessageType.GameStateSynchronization));
    }

    [Fact]
    public async Task Portable_disjoint_rolls_back_tool_piece_and_rewards_when_settlement_fails()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(65, 2600469, 2);
        shop.Sql("CREATE TRIGGER refuse_portable_reward BEFORE INSERT ON item_slots "
            + "WHEN NEW.id = 1000 BEGIN SELECT RAISE(ABORT,'forced portable failure'); END;");

        await Assert.ThrowsAsync<SqliteException>(() =>
            Disjoint(shop, 9, disjointToolSlot: 65).AsTask());

        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(2, shop.Count(2600469));
        Assert.Equal(0, shop.Count(1000));
        Assert.Empty(shop.Sender.Messages);
    }

    [Fact]
    public async Task Portable_batch_consumes_one_tool_for_each_independent_target()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.SeedCore(10, Piece());
        await shop.Seed(65, 2600469, 3);

        await Disjoint(shop, 9, disjointToolSlot: 65);
        await Disjoint(shop, 10, disjointToolSlot: 65);

        Assert.Null(await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Null(await shop.Durability(InventoryContainerType.Main, 10));
        Assert.Equal(1, shop.Count(2600469));
        Assert.Equal(24, shop.Count(1000));
        Assert.Equal(2, shop.Sender.Messages.OfType<DisjointItemResponse>().Count());
    }

    [Fact]
    public async Task Portable_machine_level_limit_rejects_without_consuming_tool_or_piece()
    {
        using var shop = Fixture(equipmentLevel: 60);
        await shop.SeedCore(9, Piece());
        await shop.Seed(65, 2600469, 2);

        await Disjoint(shop, 9, disjointToolSlot: 65);

        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(2, shop.Count(2600469));
        Assert.Equal(0, shop.Count(1000));
        var response = Assert.IsType<DisjointItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.Null(response.TargetSlot);
        Assert.Equal(0x13, response.ErrorCode);
    }

    [Fact]
    public async Task Reported_provider_id_does_not_reject_system_disjoint()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());

        await Disjoint(shop, 9, disjointProviderId: 0x12345678);

        Assert.Null(await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(12, shop.Count(1000));
        Assert.NotNull(Assert.Single(shop.Sender.Messages.OfType<DisjointItemResponse>()).TargetSlot);
    }

    [Fact]
    public async Task Equipment_in_a_quick_slot_passes_occupancy_then_disjoints_by_equipment_behavior()
    {
        using var shop = Fixture();
        await shop.SeedCore(4, Piece());

        await Disjoint(shop, 4);

        Assert.Null(await shop.Durability(InventoryContainerType.Main, 4));
        var response = Assert.Single(shop.Sender.Messages.OfType<DisjointItemResponse>());
        Assert.Equal(new GameSlot(InventoryContainerType.Main, 4), response.TargetSlot);
    }

    [Fact]
    public async Task A_non_equipment_in_a_quick_slot_passes_occupancy_but_behavior_refuses_disjoint()
    {
        using var shop = Fixture();
        await shop.Seed(4, 4000, 1);

        await Disjoint(shop, 4);

        Assert.Equal(1, shop.Count(4000));
        var response = Assert.IsType<DisjointItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.Null(response.TargetSlot);
        Assert.Equal(0x13, response.ErrorCode);
    }

    [Fact]
    public async Task Stored_content_that_contradicts_its_business_space_is_an_invariant_failure()
    {
        using var shop = Fixture();
        await shop.SeedCore(121, Piece());

        var failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Disjoint(shop, 121).AsTask());

        Assert.Contains("Material/0", failure.Message, StringComparison.Ordinal);
        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 121));
        Assert.Empty(shop.Sender.Messages);
    }

    [Fact]
    public async Task Last_portable_machine_frees_its_quick_slot_for_the_reward()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 1000, 3);
        await shop.Seed(122, 1000, 1000, 3);
        await shop.Seed(3, 2600469, 1);
        for (var slot = 4; slot <= 8; slot++) await shop.Seed(slot, 2000, 10);

        await Disjoint(shop, 9, disjointToolSlot: 3);

        Assert.Null(await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(0, shop.Count(2600469));
        Assert.Equal(2012, shop.Count(1000));
        var response = Assert.Single(shop.Sender.Messages.OfType<DisjointItemResponse>());
        Assert.Equal(new DisjointItemMaterial(new(InventoryContainerType.Main, 3), 1000, 12),
            Assert.Single(response.Materials!));
    }

    [Fact]
    public async Task Unsupported_reward_refuses_disjoint_without_reporting_inventory_full()
    {
        using var shop = Fixture(rewards: [new(3000, 1)]);
        await shop.SeedCore(9, Piece());

        await Disjoint(shop, 9);

        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 9));
        var response = Assert.IsType<DisjointItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.Null(response.TargetSlot);
        Assert.Equal(0x13, response.ErrorCode);
    }

    [Fact]
    public async Task Expired_portable_machine_refuses_without_consuming_tool_or_piece()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.SeedCore(65, StackableItemCore.Create(2, 2600469, 1) with { ExpireTime = 1 });

        await Disjoint(shop, 9, disjointToolSlot: 65);

        Assert.Equal((ushort)45, await shop.Durability(InventoryContainerType.Main, 9));
        Assert.Equal(1, shop.Count(2600469));
        Assert.Equal(0, shop.Count(1000));
        var response = Assert.IsType<DisjointItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.Null(response.TargetSlot);
        Assert.Equal(0x13, response.ErrorCode);
    }

    private static ShopFixture Fixture(
        int equipmentLevel = 10,
        IReadOnlyList<EquipmentDisjointReward>? rewards = null) => new(services =>
    {
        services.AddSingleton<IEquipmentDisjointRuleService>(new FixedRules(
            rewards ?? [new EquipmentDisjointReward(1000, 12)]));
        services.AddSingleton<IEquipmentDisjointDefinitionSource>(
            new DisjointContent(equipmentLevel));
    });

    private static InventoryItemCoreState Piece() =>
        StackableItemCore.Create(1, 3000, 1234) with { Durability = 45 };

    private static ValueTask Disjoint(
        ShopFixture shop,
        short slot,
        int? disjointProviderId = null,
        short disjointToolSlot = -1) =>
        shop.Services.GetRequiredService<DisjointItemHandler>().HandleAsync(new(shop.Connection),
            new DisjointItemRequest(
                GameMessageMetadata.Empty,
                new GameSlot(InventoryContainerType.Main, slot), disjointToolSlot,
                disjointProviderId ?? (disjointToolSlot < 0 ? 1000 : 0)),
            default);

    private sealed class FixedRules(IReadOnlyList<EquipmentDisjointReward> rewards)
        : IEquipmentDisjointRuleService
    {
        public ValueTask<EquipmentDisjointRuleResult> ResolveAsync(EquipmentDisjointCandidate equipment,
            EquipmentDisjointSettings settings, int randomSeed, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new EquipmentDisjointRuleResult(
                EquipmentDisjointRejection.None, rewards));
    }

    private sealed class DisjointContent(int equipmentLevel) : IEquipmentDisjointDefinitionSource
    {
        public ValueTask<EquipmentDisjointDefinition?> GetEquipmentAsync(
            int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<EquipmentDisjointDefinition?>(
                itemId == 3000 ? new(100, equipmentLevel, 1, true) : null);

        public ValueTask<PortableDisjointMachineDefinition?> GetPortableMachineAsync(
            int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<PortableDisjointMachineDefinition?>(itemId switch
            {
                2600468 => new(0, 30),
                2600469 => new(1, 50),
                2600470 => new(2, 70),
                2600548 => new(3, 90),
                _ => null,
            });

        public ValueTask<EquipmentDisjointTable?> GetSystemTableAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<EquipmentDisjointTable?>(null);
    }
}
