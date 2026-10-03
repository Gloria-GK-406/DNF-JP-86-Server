using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.EquipmentDurability;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentDurabilityTests
{
    [Fact]
    public async Task Wear_report_commits_one_point_before_answering()
    {
        using var shop = new ShopFixture();
        await shop.SeedCore(12, StackableItemCore.Create(1, 3000, 1234) with { Durability = 2 },
            InventoryContainerType.Equipment);
        shop.Sender.BeforeSend = () =>
            Assert.Equal((ushort)1, shop.Durability(InventoryContainerType.Equipment, 12)
                .AsTask().GetAwaiter().GetResult());

        await Wear(shop, 12);

        Assert.Equal((ushort)1, await shop.Durability(InventoryContainerType.Equipment, 12));
        Assert.Equal(new BusinessSlot(BusinessInventorySpace.WornEquipment, 0), Assert.IsType<DecreaseDurabilityResponse>(
            Assert.Single(shop.Sender.Messages)).Receipt!.Slot);
    }

    [Fact]
    public async Task Charm_slot_30_keeps_its_wear_command_behavior()
    {
        using var shop = new ShopFixture();
        await shop.SeedCore(30, StackableItemCore.Create(1, 3000, 1234) with { Durability = 2 },
            InventoryContainerType.Equipment);

        await Wear(shop, 30);

        Assert.Equal((ushort)1, await shop.Durability(InventoryContainerType.Equipment, 30));
        Assert.Equal(new BusinessSlot(BusinessInventorySpace.WornEquipmentExtension, 1),
            Assert.IsType<DecreaseDurabilityResponse>(Assert.Single(shop.Sender.Messages)).Receipt!.Slot);
    }

    [Fact]
    public async Task Wear_stops_at_zero_and_still_acknowledges_an_already_broken_piece()
    {
        using var shop = new ShopFixture();
        await shop.SeedCore(12, StackableItemCore.Create(1, 3000, 1234) with { Durability = 1 },
            InventoryContainerType.Equipment);
        await Wear(shop, 12);
        Assert.Equal((ushort)0, await shop.Durability(InventoryContainerType.Equipment, 12));
        shop.Sql("CREATE TRIGGER refuse_wear BEFORE UPDATE OF item_core ON items BEGIN SELECT RAISE(ABORT,'unexpected item write'); END;");
        await Wear(shop, 12);
        Assert.Equal((ushort)0, await shop.Durability(InventoryContainerType.Equipment, 12));
        Assert.Equal(2, shop.Sender.Messages.Count);
        Assert.All(shop.Sender.Messages, message =>
            Assert.Equal(new BusinessSlot(BusinessInventorySpace.WornEquipment, 0),
                Assert.IsType<DecreaseDurabilityResponse>(message).Receipt!.Slot));
    }

    [Fact]
    public async Task Accessory_and_empty_worn_slot_are_rejected_without_inventory_change()
    {
        using var shop = new ShopFixture();
        await shop.SeedCore(19, StackableItemCore.Create(1, 3000, 1234) with { Durability = 2 },
            InventoryContainerType.Equipment);
        await Wear(shop, 19);
        await Wear(shop, 12);
        Assert.Equal((ushort)2, await shop.Durability(InventoryContainerType.Equipment, 19));
        Assert.All(shop.Sender.Messages, message =>
        {
            var response = Assert.IsType<DecreaseDurabilityResponse>(message);
            Assert.Null(response.Receipt);
            Assert.Equal((byte)17, response.ErrorCode);
        });
    }

    [Fact]
    public async Task Replaced_session_cannot_wear_the_previous_characters_equipment()
    {
        using var shop = new ShopFixture();
        await shop.SeedCore(12, StackableItemCore.Create(1, 3000, 1234) with { Durability = 2 },
            InventoryContainerType.Equipment);
        var old = shop.Session;
        shop.Select(2);
        var result = await shop.Services.GetRequiredService<ICommandBus>().SendAsync(
            new DecreaseDurabilityCommand(shop.OnlineUser, old,
                new BusinessSlot(BusinessInventorySpace.WornEquipment, 0)));
        Assert.Equal(EquipmentDurabilityRejection.NoSession, result.Rejection);
        Assert.Empty(shop.Sender.Messages);
    }

    [Fact]
    public async Task Failed_item_write_rolls_back_wear_and_sends_no_acknowledgement()
    {
        using var shop = new ShopFixture();
        await shop.SeedCore(12, StackableItemCore.Create(1, 3000, 1234) with { Durability = 2 },
            InventoryContainerType.Equipment);
        shop.Sql("CREATE TRIGGER refuse_wear BEFORE UPDATE OF item_core ON items BEGIN SELECT RAISE(ABORT,'test wear failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Wear(shop, 12).AsTask());
        Assert.Equal((ushort)2, await shop.Durability(InventoryContainerType.Equipment, 12));
        Assert.Empty(shop.Sender.Messages);
    }

    private static async ValueTask Wear(ShopFixture shop, byte slot)
    {
        var registration = Assert.Single(shop.Services.GetServices<GameMessageDecoderRegistration>(),
            value => value.Selector == 1 && value.Type == 0x0030);
        var request = Assert.IsAssignableFrom<IClientNetworkMessage>(
            ((IGameMessageDecoder)shop.Services.GetRequiredService(registration.DecoderType))
                .Decode(GameMessageMetadata.Empty, [slot]));
        await shop.Services.GetRequiredService<INetworkMessageDispatcher>()
            .DispatchAsync(new(shop.Connection), request, default);
    }
}
