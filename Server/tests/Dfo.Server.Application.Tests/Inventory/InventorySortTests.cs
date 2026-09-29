using Dfo.Server.Abstractions.Networking;
using System.Buffers.Binary;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game.InventorySort;
using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class InventorySortTests
{
    [Fact]
    public async Task Sorting_compacts_only_selected_band_and_preserves_locked_slots_and_uids()
    {
        using var shop = new ShopFixture(s => {
            s.AddSingleton<IInventorySortDomainService, InventorySortDomainService>();
            s.AddSingleton<IInventorySortKeySource, Keys>();
        });
        await shop.SeedCore(12, StackableItemCore.Create(1, 3000, 101) with { Durability = 4 });
        await shop.SeedCore(14, StackableItemCore.Create(1, 3000, 202) with { Durability = 8 });
        await shop.SeedCore(10, StackableItemCore.Create(1, 3000, 303) with { SortLockFlag = 1 });
        var uid = shop.Scalar("SELECT core_uid FROM item_slots WHERE list_type=0 AND slot_index=12");
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using (var tx = scope.Begin())
        {
            Assert.True(await shop.Services.GetRequiredService<IInventorySortDomainService>()
                .SortAsync(shop.Session.CharacterId, BusinessInventorySpace.CarriedEquipment,
                    InventorySortOrder.Default, default));
            await tx.CommitAsync(default);
        }
        Assert.Equal(uid, shop.Scalar("SELECT core_uid FROM item_slots WHERE list_type=0 AND slot_index=9"));
        var container = shop.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
            .GetStore(shop.Session.CharacterId, BusinessInventoryStoreKind.Item)
            .GetContainer(BusinessInventorySpace.CarriedEquipment);
        Assert.Equal((ushort)4, (await container.ReadSlotAsync(0, default))!.Core.Durability);
        Assert.Equal(303, (await container.ReadSlotAsync(1, default))!.Core.Value);
        Assert.Equal(202, (await container.ReadSlotAsync(2, default))!.Core.Value);
        Assert.Null(await container.ReadSlotAsync(3, default));
        Assert.Null(await container.ReadSlotAsync(5, default));
    }

    [Fact]
    public async Task Same_template_instances_survive_a_permutation_and_a_second_sort_is_idempotent()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, StackableItemCore.Create(1, 3000, 55));
        await shop.SeedCore(10, StackableItemCore.Create(1, 100310669, 77));
        await shop.SeedCore(12, StackableItemCore.Create(1, 3000, 55));
        var first = shop.Scalar("SELECT core_uid FROM item_slots WHERE list_type=0 AND slot_index=9");
        var second = shop.Scalar("SELECT core_uid FROM item_slots WHERE list_type=0 AND slot_index=12");
        var high = shop.Scalar("SELECT core_uid FROM item_slots WHERE list_type=0 AND slot_index=10");
        for (var i = 0; i < 2; i++)
        {
            await Sort(shop, BusinessInventorySpace.CarriedEquipment, InventorySortOrder.Level);
            Assert.Equal(high, shop.Scalar("SELECT core_uid FROM item_slots WHERE list_type=0 AND slot_index=9"));
            Assert.Equal(first, shop.Scalar("SELECT core_uid FROM item_slots WHERE list_type=0 AND slot_index=10"));
            Assert.Equal(second, shop.Scalar("SELECT core_uid FROM item_slots WHERE list_type=0 AND slot_index=11"));
            Assert.Equal(3, shop.Scalar("SELECT COUNT(*) FROM items"));
        }
    }

    [Fact]
    public async Task Piles_keep_separate_counts_and_other_bands_do_not_move()
    {
        using var shop = Fixture();
        await shop.Seed(125, 1000, 6, 3);
        await shop.Seed(128, 1000, 7, 3);
        await shop.SeedCore(14, StackableItemCore.Create(1, 3000, 1));
        await Sort(shop, BusinessInventorySpace.Material);
        var bag = shop.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
            .GetStore(shop.Session.CharacterId, BusinessInventoryStoreKind.Item).GetContainer(BusinessInventorySpace.Material);
        Assert.Equal(6, (await bag.ReadSlotAsync(0, default))!.Count);
        Assert.Equal(7, (await bag.ReadSlotAsync(1, default))!.Count);
        Assert.Equal(1, shop.Scalar("SELECT COUNT(*) FROM item_slots WHERE slot_index=14"));
        Assert.Equal(13, shop.Count(1000));
    }

    [Fact]
    public async Task Aborted_sort_rolls_back_all_positions_and_identities()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, StackableItemCore.Create(1, 3000, 55));
        var uid = shop.Scalar("SELECT core_uid FROM item_slots WHERE slot_index=12");
        await using (var tx = ((DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin())
            Assert.True(await shop.Services.GetRequiredService<IInventorySortDomainService>()
                .SortAsync(shop.Session.CharacterId, BusinessInventorySpace.CarriedEquipment, InventorySortOrder.Default, default));
        Assert.Equal(uid, shop.Scalar("SELECT core_uid FROM item_slots WHERE slot_index=12"));
        Assert.Equal(0, shop.Scalar("SELECT COUNT(*) FROM item_slots WHERE slot_index=9"));
        await Sort(shop, BusinessInventorySpace.CarriedEquipment);
        Assert.Equal(uid, shop.Scalar("SELECT core_uid FROM item_slots WHERE slot_index=9"));
    }

    [Fact]
    public async Task Worn_equipment_cannot_be_reordered()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, StackableItemCore.Create(1, 3000, 55), InventoryContainerType.Equipment);
        Assert.False(await shop.Services.GetRequiredService<IInventorySortDomainService>()
            .SortAsync(shop.Session.CharacterId, BusinessInventorySpace.WornEquipment, InventorySortOrder.Default, default));
        Assert.Equal(1, shop.Scalar("SELECT COUNT(*) FROM item_slots WHERE list_type=3 AND slot_index=12"));
    }

    [Fact]
    public async Task Successful_handler_sends_ack_then_full_list_then_locks_with_currency_unchanged()
    {
        using var shop = Fixture();
        await shop.SeedCore(12, StackableItemCore.Create(1, 3000, 55));
        await shop.Services.GetRequiredService<SortItemHandler>().HandleAsync(new(shop.Connection),
            new SortItemRequest(GameMessageMetadata.Empty, 0, 1, 0), default);
        Assert.Collection(shop.Sender.Messages,
            message => Assert.Equal(new SortItemResponse(0, true), message),
            message => {
                var bytes = Encode(shop, message);
                Assert.Equal(0x000D, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(1, 2)));
                Assert.Equal(100000, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(26, 4)));
                Assert.Equal(9, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(15 + 5 + 3 * 101, 2)));
            },
            message => Assert.Equal(0x00FB, BinaryPrimitives.ReadUInt16LittleEndian(Encode(shop, message).AsSpan(1, 2))));
        Assert.Equal(100000, shop.Gold);
    }

    [Fact]
    public async Task Stale_session_command_cannot_mutate_inventory()
    {
        using var shop = Fixture();
        var stale = shop.Session;
        await shop.SeedCore(12, StackableItemCore.Create(1, 3000, 55));
        shop.Select(2);
        var result = await shop.Services.GetRequiredService<ICommandBus>().SendAsync(
            new SortItemCommand(shop.OnlineUser, stale, BusinessInventorySpace.CarriedEquipment, InventorySortOrder.Default));
        Assert.Null(result.Receipt);
        Assert.Equal(1, shop.Scalar("SELECT COUNT(*) FROM item_slots WHERE owner_id=1 AND slot_index=12"));
    }

    private static byte[] Encode(ShopFixture shop, IServerNetworkMessage message)
    {
        var registration = shop.Services.GetServices<ServerMessageEncoderRegistration>()
            .Single(value => value.MessageType == message.GetType());
        return Assert.Single(((IServerMessageEncoder)shop.Services.GetRequiredService(registration.EncoderType)).Encode(message));
    }

    private static ShopFixture Fixture() => new(s => s.AddSingleton<IInventorySortKeySource, Keys>());
    private static async Task Sort(ShopFixture shop, BusinessInventorySpace space,
        InventorySortOrder order = InventorySortOrder.Default)
    {
        await using var tx = ((DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
        Assert.True(await shop.Services.GetRequiredService<IInventorySortDomainService>()
            .SortAsync(shop.Session.CharacterId, space, order, default));
        await tx.CommitAsync(default);
    }

    private sealed class Keys : IInventorySortKeySource
    {
        public ValueTask<InventorySortKeys> GetAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult(new InventorySortKeys(0, itemId == 100310669 ? 80 : 1, 0, ""));
    }
}
