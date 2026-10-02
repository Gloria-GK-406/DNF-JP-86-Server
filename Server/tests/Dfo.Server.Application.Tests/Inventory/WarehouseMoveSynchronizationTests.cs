using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class WarehouseMoveSynchronizationTests
{
    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(12, false)]
    [InlineData(12, true)]
    public async Task Clicking_a_nonstackable_coupon_into_cargo_chooses_an_empty_slot_and_keeps_each_expiry(
        byte list, bool differentExpiry)
    {
        using var host = WarehouseStorageTests.Fixture();
        host.Sql("INSERT INTO account_cargo_state(account_id,selection_key) VALUES(1,8)");
        var first = StackableItemCore.Create(2, 4000, 1) with { ExpireTime = int.MaxValue - 100 };
        var second = first with { ExpireTime = first.ExpireTime + (differentExpiry ? 1 : 0) };
        await host.SeedCore(65, first);
        await Send(host, 0, 65, list, 0);
        await host.SeedCore(65, second);
        await Send(host, 0, 65, list, 0);
        var acknowledgement = host.Sender.Messages.OfType<MoveItemSpaceResponse>().Last();
        Assert.Null(acknowledgement.ErrorCode);
        Assert.Equal(new((InventoryContainerType)list, 1), acknowledgement.DestinationSlot);
        Assert.Null(await WarehouseStorageTests.Bag(host, BusinessInventorySpace.Consumable)
            .ReadSlotAsync(0, default));
        Assert.Equal(first, (await WarehouseStorageTests.Bag(host, list == 2
            ? BusinessInventorySpace.PersonalCargo : BusinessInventorySpace.AccountCargo).ReadSlotAsync(0, default))!.Core);
        AssertSlot(host, 0, 65, -1, 0, 0);
        AssertSlot(host, list, 0, 4000, 1, first.ExpireTime);
        AssertSlot(host, list, 1, 4000, 1, second.ExpireTime);
        await Send(host, 0, 65, list, 0, instance: 4000);
        Assert.NotNull(host.Sender.Messages.OfType<MoveItemSpaceResponse>().Last().ErrorCode);
        AssertSlot(host, 0, 65, -1, 0, 0);
        AssertSlot(host, list, 0, 4000, 1, first.ExpireTime);
        AssertSlot(host, list, 1, 4000, 1, second.ExpireTime);
        await Send(host, list, 0, 0, 65);
        AssertSlot(host, list, 0, -1, 0, 0);
        AssertSlot(host, 0, 65, 4000, 1, first.ExpireTime);
        await Send(host, list, 0, 0, 65, instance: 4000);
        Assert.NotNull(host.Sender.Messages.OfType<MoveItemSpaceResponse>().Last().ErrorCode);
        AssertSlot(host, list, 0, -1, 0, 0);
        AssertSlot(host, 0, 65, 4000, 1, first.ExpireTime);
        await Send(host, list, 1, list, 2);
        AssertSlot(host, list, 1, -1, 0, 0);
        AssertSlot(host, list, 2, 4000, 1, second.ExpireTime);
        await Send(host, 0, 65, list, 0);
        await Send(host, list, 2, list, 0);
        Assert.Equal(MoveItemSpaceResponse.StackLimitReached,
            host.Sender.Messages.OfType<MoveItemSpaceResponse>().Last().ErrorCode);
        AssertSlot(host, list, 0, 4000, 1, first.ExpireTime);
        AssertSlot(host, list, 2, 4000, 1, second.ExpireTime);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(12)]
    public async Task Click_deposit_uses_the_last_open_slot_and_refuses_only_when_all_open_slots_are_full(byte list)
    {
        using var host = WarehouseStorageTests.Fixture();
        host.Sql("INSERT INTO account_cargo_state(account_id,selection_key) VALUES(1,8)");
        for (var slot = 0; slot < 7; slot++)
        {
            await host.Seed(65, 4000, 1);
            await Send(host, 0, 65, list, slot);
            Assert.Null(host.Sender.Messages.OfType<MoveItemSpaceResponse>().Last().ErrorCode);
        }
        await host.Seed(65, 4000, 1);
        await Send(host, 0, 65, list, 0);
        var acknowledgement = host.Sender.Messages.OfType<MoveItemSpaceResponse>().Last();
        Assert.Null(acknowledgement.ErrorCode);
        Assert.Equal(new((InventoryContainerType)list, 7), acknowledgement.DestinationSlot);
        AssertSlot(host, list, 7, 4000, 1, 0);
        await host.Seed(65, 4000, 1);
        var before = host.StoredCore(65);
        await Send(host, 0, 65, list, 0);
        Assert.Equal((byte)4, host.Sender.Messages.OfType<MoveItemSpaceResponse>().Last().ErrorCode);
        Assert.Equal(before, host.StoredCore(65));
        Assert.Equal(8, host.Scalar($"SELECT COUNT(*) FROM item_slots WHERE list_type={list}"));
        AssertSlot(host, list, 8, -1, 0, 0);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(12)]
    public async Task Split_merge_and_different_item_swap_publish_both_committed_slots(byte list)
    {
        using var host = WarehouseStorageTests.Fixture();
        host.Sql("INSERT INTO account_cargo_state(account_id,selection_key) VALUES(1,8)");
        await host.Seed(65, 2000, 8);
        await Send(host, 0, 65, list, 0, 3);
        AssertSlot(host, 0, 65, 2000, 5, 0);
        AssertSlot(host, list, 0, 2000, 3, 0);
        await Send(host, 0, 65, list, 0, 2);
        AssertSlot(host, 0, 65, 2000, 3, 0);
        AssertSlot(host, list, 0, 2000, 5, 0);
        await host.Seed(65, 5000, 1);
        await Send(host, 0, 65, list, 0);
        AssertSlot(host, 0, 65, 2000, 5, 0);
        AssertSlot(host, list, 0, 5000, 1, 0);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(12)]
    public async Task Failed_storage_does_not_publish_a_success_or_lose_the_source(byte list)
    {
        using var host = WarehouseStorageTests.Fixture();
        host.Sql("INSERT INTO account_cargo_state(account_id,selection_key) VALUES(1,8)");
        await host.Seed(65, 4000, 1);
        await Send(host, 0, 65, list, 0);
        await host.Seed(65, 4000, 1);
        var before = host.StoredCore(65);
        host.Sql($"CREATE TRIGGER reject_cargo BEFORE INSERT ON item_slots WHEN NEW.list_type={list} BEGIN SELECT RAISE(ABORT,'forced'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Send(host, 0, 65, list, 0).AsTask());
        Assert.Empty(host.Sender.Messages);
        Assert.Equal(before, host.StoredCore(65));
        Assert.Equal(1, host.Scalar($"SELECT COUNT(*) FROM item_slots WHERE list_type={list}"));
    }

    private static ValueTask Send(ShopFixture host, byte from, int source, byte into, int target,
        int count = 0, int instance = 0)
    {
        host.Sender.Messages.Clear();
        return host.Services.GetRequiredService<MoveItemSpaceHandler>().HandleAsync(new(host.Connection),
            new MoveItemSpaceRequest(GameMessageMetadata.Empty, new((InventoryContainerType)from, source),
                instance, count, new((InventoryContainerType)into, target), 0), default);
    }

    private static void AssertSlot(ShopFixture host, byte list, int slot, int item, int count, int expiry)
    {
        var frames = host.Sender.Messages.SelectMany(message =>
        {
            var registration = host.Services.GetServices<ServerMessageEncoderRegistration>()
                .Single(value => value.MessageType == message.GetType());
            return ((IServerMessageEncoder)host.Services.GetRequiredService(registration.EncoderType)).Encode(message);
        }).ToArray();
        Assert.Equal(1, frames[0][0]); // Acknowledge before correcting the client's local move.
        var frame = Assert.Single(frames, f => f[0] == 0 && f[1] == 0x0d && f[15] == list);
        var entries = list == 12 ? 24 : 20;
        var size = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(entries - 2));
        Assert.Equal(entries + size * 101, frame.Length);
        var matching = Enumerable.Range(0, size).Select(i => entries + i * 101)
            .Where(offset => BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(offset)) == slot).ToArray();
        if (item == -1)
        {
            Assert.Empty(matching); // Full snapshots remove slots absent from the committed state.
            return;
        }
        var entry = Assert.Single(matching);
        Assert.Equal(item, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(entry + 2)));
        Assert.Equal(count, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(entry + 6)));
        Assert.Equal(expiry, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(entry + 43)));
    }
}
