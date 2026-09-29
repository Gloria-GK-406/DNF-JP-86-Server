using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.ReinforceItem;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentReinforcementHandlerTests
{
    [Fact]
    public async Task Ack_is_emitted_only_after_equipment_material_and_gold_commit()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 20, 3);
        shop.Sender.BeforeSend = () =>
        {
            Assert.Equal(10, shop.Count(1000));
            Assert.Equal(99_900, shop.Gold);
        };

        await Send(shop, 7);

        var ack = Assert.IsType<ReinforceItemResponse>(shop.Sender.Messages[0]);
        Assert.True(ack.Success);
        Assert.Equal((byte)1, ack.NewLevel);
        Assert.True(shop.Sender.Messages.Count > 2);
    }

    [Fact]
    public async Task Same_connection_sequence_replay_returns_ack_without_spending_again()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 20, 3);

        await Send(shop, 7);
        var countAfterFirst = shop.Sender.Messages.Count;
        await Send(shop, 7);

        Assert.Equal(countAfterFirst + 1, shop.Sender.Messages.Count);
        Assert.Equal(2, shop.Sender.Messages.OfType<ReinforceItemResponse>().Count());
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
    }

    [Fact]
    public async Task Same_sequence_with_changed_request_is_rejected_instead_of_replaying_success()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 20, 3);

        await Send(shop, 7);
        await Send(shop, 7, expectedItemId: 3001);

        var replies = shop.Sender.Messages.OfType<ReinforceItemResponse>().ToArray();
        Assert.Equal(2, replies.Length);
        Assert.True(replies[0].Success);
        Assert.False(replies[1].Success);
        Assert.Equal((byte)0x04, replies[1].ErrorCode);
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
    }

    [Fact]
    public async Task Switching_character_does_not_replay_the_previous_characters_ack()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 20, 3);

        await Send(shop, 7);
        shop.Select(2);
        await Send(shop, 7);

        var replies = shop.Sender.Messages.OfType<ReinforceItemResponse>().ToArray();
        Assert.Equal(2, replies.Length);
        Assert.True(replies[0].Success);
        Assert.False(replies[1].Success);
        Assert.Equal((byte)0x04, replies[1].ErrorCode);
        Assert.Equal(100_000, shop.Gold);
    }

    [Fact]
    public async Task Wrong_virtual_material_slot_is_rejected_without_falling_back_to_another_asset()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 20, 3);
        shop.Sql("UPDATE accounts SET cube_black=20 WHERE account_id=1");

        await Send(shop, 7, materialSlot: 354);

        var reply = Assert.IsType<ReinforceItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.False(reply.Success);
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(20, shop.Scalar("SELECT cube_black FROM accounts WHERE account_id=1"));
        Assert.Equal(100_000, shop.Gold);
    }

    [Fact]
    public async Task Different_sequence_can_intentionally_reinforce_the_next_level()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 20, 3);

        await Send(shop, 7);
        await Send(shop, 8);

        Assert.Equal([1, 2], shop.Sender.Messages.OfType<ReinforceItemResponse>()
            .Select(message => (int)message.NewLevel));
        Assert.Equal(0, shop.Count(1000));
        Assert.Equal(99_800, shop.Gold);
    }

    [Fact]
    public async Task Failed_commit_is_not_cached_as_a_success_for_replay()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 20, 3);
        shop.Sql("CREATE TRIGGER refuse_upgrade BEFORE UPDATE OF carried_gold ON character_stats "
            + "WHEN NEW.carried_gold <> OLD.carried_gold BEGIN SELECT RAISE(ABORT,'forced'); END;");

        await Assert.ThrowsAsync<SqliteException>(() => Send(shop, 7).AsTask());
        Assert.Empty(shop.Sender.Messages);
        Assert.Equal(20, shop.Count(1000));
        shop.Sql("DROP TRIGGER refuse_upgrade;");

        await Send(shop, 7);

        Assert.True(Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>()).Success);
        Assert.Equal(10, shop.Count(1000));
    }

    [Fact]
    public async Task Amplification_mode_does_not_run_ordinary_reinforcement()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 20, 3);

        await Send(shop, 7, method: 1);

        var ack = Assert.IsType<ReinforceItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.False(ack.Success);
        Assert.Equal((byte)0x17, ack.ErrorCode);
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(100_000, shop.Gold);
    }

    private static ShopFixture Fixture() => new(services =>
    {
        services.AddSingleton<IEquipmentReinforcementDefinitionSource>(new Content());
        services.AddSingleton<IEquipmentReinforcementRollSource>(new FixedRoll());
    });

    private static InventoryItemCoreState Piece() =>
        StackableItemCore.Create(1, 3000, 1234) with { Durability = 45 };

    private static ValueTask Send(ShopFixture shop, ushort sequence, ushort method = 0,
        int expectedItemId = 3000, short materialSlot = 121) =>
        shop.Services.GetRequiredService<ReinforceItemHandler>().HandleAsync(
            new(shop.Connection),
            new ReinforceItemRequest(new(1, 0x0050, 0, sequence), method,
                9, expectedItemId, materialSlot, -1, [0x41]), default);

    private sealed class FixedRoll : IEquipmentReinforcementRollSource
    {
        public int Next(int exclusiveUpperBound) => 0;
    }

    private sealed class Content : IEquipmentReinforcementDefinitionSource
    {
        public ValueTask<EquipmentReinforcementTable?> GetNormalTableAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementTable?>(new(
                [new(1, new double[19], 0, 1, 0, 1000, 10),
                    new(2, new double[19], 0, 1, 0, 1000, 10)],
                [0, 100], [1], [1], new Dictionary<int, double> { [0] = 1, [1] = 1 },
                new Dictionary<int, int>(), new Dictionary<int, int>(),
                new(3243, 40, 10, new Dictionary<int, double>(),
                    new Dictionary<int, int>()), 12));

        public ValueTask<EquipmentReinforcementEquipmentDefinition?> GetEquipmentAsync(
            int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementEquipmentDefinition?>(
                itemId == 3000 ? new(1, 10, 0, 0, 45, true) : null);

        public ValueTask<EquipmentReinforcementProtectionDefinition?> GetProtectionAsync(
            int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementProtectionDefinition?>(null);
    }
}
