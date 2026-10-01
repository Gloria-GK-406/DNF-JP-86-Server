using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MagicSeal;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class MagicSealHandlerTests
{
    [Fact]
    public async Task Unseal_sends_committed_equipment_and_gold_without_unhandled_ack()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        shop.Sender.BeforeSend = () => Assert.Equal(99500, shop.Gold);

        await Send(shop, 7, false);

        Assert.Equal(99500, shop.Gold);
        Assert.Equal(2, shop.Sender.Messages.Count);
        Assert.Equal("InventoryDeltaMessage", shop.Sender.Messages[0].GetType().Name);
        Assert.Empty(shop.Sender.Messages.OfType<ChangeRandomOptionResponse>());
    }

    [Fact]
    public async Task Change_sends_ack_and_authoritative_equipment_and_gold()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece() with { RandomOption0 = new(15, 2, 3) });

        await Send(shop, 7, true);

        Assert.Equal(99800, shop.Gold);
        Assert.True(Assert.IsType<ChangeRandomOptionResponse>(shop.Sender.Messages[0]).Success);
        Assert.Equal("InventoryDeltaMessage", shop.Sender.Messages[1].GetType().Name);
        Assert.Equal(3, shop.Sender.Messages.Count);
    }

    [Fact]
    public async Task Change_rejects_unsealed_target_without_debit()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());

        await Send(shop, 7, true);

        Assert.False(Assert.Single(shop.Sender.Messages.OfType<ChangeRandomOptionResponse>()).Success);
        Assert.Equal(100000, shop.Gold);
    }

    [Fact]
    public async Task Replayed_unseal_projects_current_state_without_second_gold_debit()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await Send(shop, 7, false);
        await Send(shop, 7, false);

        Assert.Equal(99500, shop.Gold);
        Assert.Equal(8, shop.Sender.Messages.Count);
        Assert.Single(shop.Sender.Batches);
    }

    [Fact]
    public async Task Replay_after_another_purchase_reports_current_gold_not_old_receipt()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await Send(shop, 7, false);
        shop.Sql("UPDATE character_stats SET carried_gold=99400 WHERE character_id=1");

        await Send(shop, 7, false);

        Assert.Equal(99400, EffectRequestTransport.MainValue(shop, 0));
        Assert.Equal(99400, shop.Gold);
    }

    [Fact]
    public async Task Non_sentinel_second_field_is_rejected_without_spending_assets()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 4000, 2, 3);

        await Send(shop, 7, false, materialSlot: 121);

        Assert.Equal(100000, shop.Gold);
        Assert.Equal(2, shop.Count(4000));
        Assert.Empty(shop.Sender.Messages);
    }

    [Fact]
    public async Task Failed_commit_sends_nothing_then_same_sequence_can_retry()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        shop.Sql("CREATE TRIGGER refuse_seal BEFORE UPDATE ON items "
            + "WHEN NEW.item_core <> OLD.item_core BEGIN SELECT RAISE(ABORT,'forced seal failure'); END;");

        await Assert.ThrowsAsync<SqliteException>(() => Send(shop, 7, false).AsTask());
        Assert.Empty(shop.Sender.Messages);
        Assert.Equal(100000, shop.Gold);
        shop.Sql("DROP TRIGGER refuse_seal;");
        await Send(shop, 7, false);

        Assert.Equal(99500, shop.Gold);
        Assert.Equal(2, shop.Sender.Messages.Count);
    }

    private static ShopFixture Fixture() => new(services =>
        services.AddSingleton<IEquipmentMagicSealContentSource>(new Content()));

    private static InventoryItemCoreState Piece() =>
        StackableItemCore.Create(1, 3000, 1234);

    private static ValueTask Send(ShopFixture shop, ushort sequence, bool change,
        short materialSlot = -1)
    {
        var metadata = new GameMessageMetadata(1, change ? (ushort)0x01B6 : (ushort)0x0191,
            0, sequence);
        return EffectRequestTransport.Send(shop, new MagicSealRequest(metadata, change, 9, materialSlot));
    }

    private sealed class Content : IEquipmentMagicSealContentSource
    {
        public ValueTask<EquipmentMagicSealDefinition?> GetEquipmentAsync(int itemId,
            CancellationToken ct) => ValueTask.FromResult<EquipmentMagicSealDefinition?>(
            itemId == 3000 ? new(3000, 2, 75, 500, 200, [new(1, 1)],
                [[new(new(15, 2, 3), 1)]], [[new(new(16, 4, 5), 1)]]) : null);

        public ValueTask<MagicSealScrollDefinition?> GetScrollAsync(int itemId,
            CancellationToken ct) => ValueTask.FromResult<MagicSealScrollDefinition?>(null);
    }
}
