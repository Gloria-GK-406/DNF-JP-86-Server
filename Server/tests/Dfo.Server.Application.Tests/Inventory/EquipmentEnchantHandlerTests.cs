using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.EnchantByBead;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentEnchantHandlerTests
{
    [Fact]
    public async Task Success_sends_committed_inventory_changes_and_ack()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(), InventoryContainerType.Equipment);
        await shop.Seed(121, 1000, 2, 3);
        shop.Sender.BeforeSend = () => Assert.Equal(1, shop.Count(1000));

        await Send(shop, 7);

        Assert.True(Assert.Single(shop.Sender.Messages.OfType<EnchantByBeadResponse>()).Success);
        Assert.Equal(1, shop.Count(1000));
        Assert.Equal(3, shop.Sender.Messages.Count);
        Assert.Equal("InventoryDeltaMessage", shop.Sender.Messages[0].GetType().Name);
        Assert.Equal("InventoryDeltaMessage", shop.Sender.Messages[1].GetType().Name);
        Assert.IsType<EnchantByBeadResponse>(shop.Sender.Messages[2]);
    }

    [Fact]
    public async Task Same_sequence_replays_current_inventory_without_ack_while_changed_request_is_rejected()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(), InventoryContainerType.Equipment);
        await shop.Seed(121, 1000, 2, 3);
        await Send(shop, 7);
        var sent = shop.Sender.Messages.Count;
        await Send(shop, 7);
        Assert.Equal(sent + 6, shop.Sender.Messages.Count);
        Assert.Single(shop.Sender.Batches, EffectRequestTransport.IsStateReplay);
        Assert.Equal(3, Assert.Single(shop.Sender.Batches, batch => batch.Messages.Any(message => message is EnchantByBeadResponse)).Messages.Length);
        await Assert.ThrowsAsync<InvalidDataException>(() => Send(shop, 7, targetSlot: 13).AsTask());
        Assert.True(Assert.Single(shop.Sender.Messages.OfType<EnchantByBeadResponse>()).Success);
        Assert.Equal(1, shop.Count(1000));
    }

    [Fact]
    public async Task Pet_target_rejected_without_debit()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(), InventoryContainerType.Equipment);
        await shop.Seed(121, 1000, 2, 3);
        await Send(shop, 7, targetList: InventoryContainerType.Pet);
        var rejection = Assert.Single(shop.Sender.Messages.OfType<EnchantByBeadResponse>());
        Assert.False(rejection.Success);
        Assert.Equal((byte)0x13, rejection.ErrorCode);
        Assert.Equal(2, shop.Count(1000));
    }

    [Fact]
    public async Task Missing_bead_and_unsupported_content_use_distinct_upstream_error_branches()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(), InventoryContainerType.Equipment);
        await Send(shop, 7);
        Assert.Equal((byte)0x11,
            Assert.Single(shop.Sender.Messages.OfType<EnchantByBeadResponse>()).ErrorCode);

        await shop.Seed(121, 4000, 2);
        await Send(shop, 8);
        Assert.Equal((byte)0x17,
            shop.Sender.Messages.OfType<EnchantByBeadResponse>().Last().ErrorCode);
        Assert.Equal(2, shop.Count(4000));
    }

    [Fact]
    public async Task A_failed_notification_batch_can_be_replayed_without_a_second_debit()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(), InventoryContainerType.Equipment);
        await shop.Seed(121, 1000, 2, 3);
        shop.Sender.FailingSends = () => new IOException("forced second notification failure");

        await Send(shop, 7);
        Assert.Empty(shop.Sender.Messages);
        Assert.Equal(1, shop.Count(1000));
        shop.Sender.FailingSends = null;

        await Send(shop, 7);

        Assert.Equal(6, shop.Sender.Messages.Count);
        Assert.Single(shop.Sender.Batches);
        Assert.Empty(shop.Sender.Messages.OfType<EnchantByBeadResponse>());
        Assert.Equal(1, shop.Count(1000));
    }

    [Fact]
    public async Task Replay_delivery_cannot_overtake_a_later_enchant_on_the_same_connection()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(), InventoryContainerType.Equipment);
        await shop.Seed(121, 1000, 2, 3);
        await Send(shop, 7);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        shop.Sender.BeforeSend = () =>
        {
            if (!entered.IsSet)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("test send barrier timed out");
            }
        };
        var replay = Task.Run(async () => await Send(shop, 7));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var later = Send(shop, 8).AsTask();
        try
        {
            Assert.False(later.IsCompleted);
            Assert.Equal(1, shop.Count(1000));
        }
        finally
        {
            release.Set();
        }
        await Task.WhenAll(replay, later).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, shop.Count(1000));
    }

    [Fact]
    public async Task Evicted_sequence_is_rejected_instead_of_spending_again()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(), InventoryContainerType.Equipment);
        await shop.Seed(121, 1000, 131, 3);
        for (ushort sequence = 1; sequence <= 130; sequence++)
            await Send(shop, sequence);
        Assert.Equal(1, shop.Count(1000));

        var batchesBefore = shop.Sender.Batches.Count;
        await Assert.ThrowsAsync<InvalidDataException>(() => Send(shop, 1).AsTask());
        Assert.Equal(batchesBefore, shop.Sender.Batches.Count);
        Assert.Equal(1, shop.Count(1000));
    }

    [Fact]
    public async Task Many_other_game_frames_do_not_make_a_later_fresh_enchant_stale()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(), InventoryContainerType.Equipment);
        await shop.Seed(121, 1000, 2, 3);
        await Send(shop, 7);
        EffectRequestTransport.ObserveOtherFrames(shop, 32768);

        await Send(shop, 32776);

        Assert.Equal(0, shop.Count(1000));
        Assert.Equal(2, shop.Sender.Messages.OfType<EnchantByBeadResponse>()
            .Count(reply => reply.Success));
    }

    [Fact]
    public async Task Full_connection_counter_wrap_reuses_a_sequence_as_a_new_operation()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(), InventoryContainerType.Equipment);
        await shop.Seed(121, 1000, 2, 3);
        await Send(shop, 7);
        EffectRequestTransport.ObserveOtherFrames(shop, 65535);

        await Send(shop, 7);

        Assert.Equal(0, shop.Count(1000));
        Assert.Equal(2, shop.Sender.Messages.OfType<EnchantByBeadResponse>()
            .Count(reply => reply.Success));
    }

    [Fact]
    public async Task Replay_after_a_later_enchant_refreshes_the_current_bead_count()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(), InventoryContainerType.Equipment);
        await shop.Seed(121, 1000, 3, 3);
        await Send(shop, 7);
        await Send(shop, 8);
        await Send(shop, 7);

        Assert.Equal(1, EffectRequestTransport.MainValue(shop, 121));
        Assert.Single(shop.Sender.Batches, EffectRequestTransport.IsStateReplay);
        Assert.Equal(2, shop.Sender.Batches.Count(batch => batch.Messages.Any(message => message is EnchantByBeadResponse)));
        Assert.Equal(1, shop.Count(1000));
    }

    [Fact]
    public async Task Sixteen_bit_sequence_wrap_accepts_new_request_but_rejects_old_one()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(), InventoryContainerType.Equipment);
        await shop.Seed(121, 1000, 3, 3);
        await Send(shop, 65534);
        await Send(shop, 65535);
        await Send(shop, 0);
        await Assert.ThrowsAsync<InvalidDataException>(() => Send(shop, 65533).AsTask());
        Assert.Equal(0, shop.Count(1000));
        Assert.Equal(3, shop.Sender.Messages.OfType<EnchantByBeadResponse>().Count(x => x.Success));
    }

    [Fact]
    public async Task Failed_commit_sends_nothing_and_retry_same_sequence_can_succeed()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, Piece(), InventoryContainerType.Equipment);
        await shop.Seed(121, 1000, 2, 3);
        shop.Sql("CREATE TRIGGER refuse_bead BEFORE UPDATE ON items "
            + "WHEN NEW.item_core <> OLD.item_core BEGIN SELECT RAISE(ABORT,'forced bead failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Send(shop, 7).AsTask());
        Assert.Empty(shop.Sender.Messages);
        Assert.Equal(2, shop.Count(1000));
        shop.Sql("DROP TRIGGER refuse_bead;");
        await Send(shop, 7);
        Assert.True(Assert.Single(shop.Sender.Messages.OfType<EnchantByBeadResponse>()).Success);
        Assert.Equal(1, shop.Count(1000));
    }

    private static ShopFixture Fixture() => new(services =>
        services.AddSingleton<IEquipmentEnchantContentSource>(new Content()));

    private static InventoryItemCoreState Piece() =>
        StackableItemCore.Create(1, 3000, 1234) with { Durability = 45 };

    private static ValueTask Send(ShopFixture shop, ushort sequence, short targetSlot = 12,
        InventoryContainerType targetList = InventoryContainerType.Equipment)
    {
        var metadata = new Dfo.Server.Data.Networking.GameMessageMetadata(1, 0x0110, 0, sequence);
        return EffectRequestTransport.Send(shop, new EnchantByBeadRequest(metadata,
                InventoryContainerType.Main, 121, targetList, targetSlot));
    }

    private sealed class Content : IEquipmentEnchantContentSource
    {
        public ValueTask<EquipmentEnchantDefinition?> ResolveAsync(int beadItemId,
            int equipmentItemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentEnchantDefinition?>(
                beadItemId == 1000 && equipmentItemId == 3000 ? new(1000, 3600) : null);
    }
}
