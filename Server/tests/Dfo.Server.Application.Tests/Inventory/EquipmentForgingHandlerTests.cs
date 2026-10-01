using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Forging;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentForgingHandlerTests
{
    [Fact]
    public async Task Success_commits_before_ack_and_replay_does_not_consume_twice()
    {
        using var shop = Fixture(0);
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 3326, 50);
        shop.Sender.BeforeSend = () => Assert.Equal(11, shop.Count(3326));

        await Send(shop, 7);
        await Send(shop, 7);

        Assert.Equal(11, shop.Count(3326));
        Assert.Equal(9, shop.Sender.Messages.Count);
        var first = Assert.IsType<ForgingResponse>(shop.Sender.Messages[0]);
        Assert.True(first.Success);
        Assert.True(first.ForgeSucceeded);
        Assert.Equal((byte)1, first.NewLevel);
        Assert.Equal(11, first.MaterialRemaining);
        Assert.Single(shop.Sender.Messages.OfType<ForgingResponse>());
        Assert.Single(shop.Sender.Batches);
        Assert.Equal("InventoryDeltaMessage", shop.Sender.Messages[1].GetType().Name);
        Assert.Equal("InventoryDeltaMessage", shop.Sender.Messages[2].GetType().Name);
    }

    [Fact]
    public async Task Failed_roll_is_committed_with_failure_outcome_and_current_material_refresh()
    {
        using var shop = Fixture(5000);
        await shop.SeedCore(9, Piece() with { GenuineUpgrade = 1 });
        await shop.Seed(121, 3326, 50);

        await Send(shop, 7);

        Assert.Equal(7, shop.Count(3326));
        var response = Assert.IsType<ForgingResponse>(shop.Sender.Messages[0]);
        Assert.True(response.Success);
        Assert.False(response.ForgeSucceeded);
        Assert.Equal((byte)1, response.OldLevel);
        Assert.Equal((byte)1, response.NewLevel);
        Assert.Equal(7, response.MaterialRemaining);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Ticket_variants_reject_without_debit_and_replay_same_error(byte variant)
    {
        using var shop = Fixture(0);
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 3326, 50);

        await Send(shop, 7, variant);
        await Assert.ThrowsAsync<InvalidDataException>(() => Send(shop, 7, variant).AsTask());

        Assert.Equal(50, shop.Count(3326));
        Assert.Single(shop.Sender.Messages);
        Assert.All(shop.Sender.Messages, message =>
        {
            var response = Assert.IsType<ForgingResponse>(message);
            Assert.False(response.Success);
            Assert.Equal((byte)0x0D, response.ErrorCode);
        });
    }

    [Fact]
    public async Task Failed_commit_emits_no_success_then_same_sequence_can_retry()
    {
        using var shop = Fixture(0);
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 3326, 50);
        shop.Sql("CREATE TRIGGER refuse_forging BEFORE UPDATE ON items "
            + "WHEN NEW.item_core <> OLD.item_core AND NEW.item_kind=1 "
            + "BEGIN SELECT RAISE(ABORT,'forced forging failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Send(shop, 7).AsTask());
        Assert.Empty(shop.Sender.Messages);
        Assert.Equal(50, shop.Count(3326));
        shop.Sql("DROP TRIGGER refuse_forging;");
        await Send(shop, 7);
        Assert.True(Assert.IsType<ForgingResponse>(shop.Sender.Messages[0]).Success);
        Assert.Equal(11, shop.Count(3326));
    }

    private static ShopFixture Fixture(int roll) => new(services =>
    {
        services.AddSingleton<IEquipmentForgingContentSource>(new Content());
        services.AddSingleton<IEquipmentReinforcementDefinitionSource>(new Equipment());
        services.AddSingleton<IEquipmentDefinitionSource>(new Equipment());
        services.AddSingleton<IEquipmentForgingRollSource>(new Roll(roll));
    });

    private static InventoryItemCoreState Piece() => StackableItemCore.Create(1, 3000, 1234)
        with { Durability = 45 };

    private static ValueTask Send(ShopFixture shop, ushort sequence, byte variant = 0)
    {
        var metadata = new GameMessageMetadata(1, 0x01B7, 0, sequence);
        return EffectRequestTransport.Send(shop, new ForgingRequest(metadata, 0, 9, 3000, 121,
                [0x41, 0x42, 0x43, 0x44], variant));
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

    private sealed class Equipment : IEquipmentReinforcementDefinitionSource, IEquipmentDefinitionSource
    {
        public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(0);

        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(
            int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentDefinition?>(
                itemId == 3000 ? new(75, 77, 2, "weapon", 45) : null);
        public ValueTask<EquipmentReinforcementTable?> GetAmplificationTableAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementTable?>(null);

        public ValueTask<EquipmentReinforcementTable?> GetNormalTableAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementTable?>(null);
        public ValueTask<EquipmentReinforcementProtectionDefinition?> GetProtectionAsync(
            int itemId, CancellationToken ct, EquipmentUpgradeKind kind = EquipmentUpgradeKind.Reinforcement) =>
            ValueTask.FromResult<EquipmentReinforcementProtectionDefinition?>(null);
    }

    private sealed class Roll(int value) : IEquipmentForgingRollSource
    {
        public int Next() => value;
    }
}
