using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.DeleteItem;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class ItemDiscardTests
{
    [Fact]
    public async Task Equipment_is_removed_before_one_success_ack_and_context_does_not_change_the_decision()
    {
        using var bag = new ShopFixture();
        await bag.SeedCore(9, StackableItemCore.Create(1, 3000, 27312));
        bag.Sender.BeforeSend = () => Assert.Equal(0, bag.Count(3000));

        await Discard(bag, 9, 3000, 1, safeModeSkipIndex: 1);

        var response = Assert.IsType<DeleteItemResponse>(Assert.Single(bag.Sender.Messages));
        Assert.Equal((9, 3000, 1),
            (response.Receipt!.SlotIndex, response.Receipt.ItemId, response.Receipt.AppliedCount));
    }

    [Fact]
    public async Task Part_of_an_interchangeable_pile_is_removed_authoritatively()
    {
        using var bag = new ShopFixture();
        await bag.Seed(65, 5000, 10);
        bag.Sender.BeforeSend = () => Assert.Equal(7, bag.Count(5000));

        await Discard(bag, 65, 5000, 3);

        var receipt = Assert.IsType<DeleteItemResponse>(Assert.Single(bag.Sender.Messages)).Receipt!;
        Assert.Equal(3, receipt.AppliedCount);
        Assert.Equal(7, receipt.RemainingCore!.Value);
    }

    [Theory]
    [InlineData(0, 5000, 1)]
    [InlineData(1, 6000, 1)]
    [InlineData(1, 5000, 11)]
    public async Task Unsupported_operation_item_disagreement_or_excess_count_cannot_change_the_slot(
        ushort operationType, int itemId, int count)
    {
        using var bag = new ShopFixture();
        await bag.Seed(65, 5000, 10);

        await Discard(bag, 65, itemId, count, operationType);

        Assert.Equal(10, bag.Count(5000));
        var response = Assert.IsType<DeleteItemResponse>(Assert.Single(bag.Sender.Messages));
        Assert.Null(response.Receipt);
        Assert.Equal(DeleteItemResponse.CannotDelete, response.ErrorCode);
    }

    [Fact]
    public async Task A_locked_piece_is_not_discarded()
    {
        using var bag = new ShopFixture();
        await bag.SeedCore(9, StackableItemCore.Create(1, 3000, 27312) with { EquipmentLockId = 1 });

        await Discard(bag, 9, 3000, 1);

        Assert.Equal(1, bag.Count(3000));
        Assert.Null(Assert.IsType<DeleteItemResponse>(Assert.Single(bag.Sender.Messages)).Receipt);
    }

    [Fact]
    public async Task A_storage_failure_rolls_back_the_deletion_and_publishes_nothing()
    {
        using var bag = new ShopFixture();
        await bag.SeedCore(9, StackableItemCore.Create(1, 3000, 27312));
        bag.Sql("CREATE TRIGGER refuse_discard BEFORE DELETE ON item_slots "
            + "BEGIN SELECT RAISE(ABORT,'test discard failure'); END;");

        await Assert.ThrowsAsync<SqliteException>(() => Discard(bag, 9, 3000, 1).AsTask());

        Assert.Equal(1, bag.Count(3000));
        Assert.Empty(bag.Sender.Messages);
    }

    [Fact]
    public async Task A_command_for_an_old_selected_character_cannot_delete_from_its_slot()
    {
        using var bag = new ShopFixture();
        await bag.Seed(65, 5000, 10);
        var old = bag.Session;
        bag.Select(2);

        var result = await bag.Services.GetRequiredService<ICommandBus>().SendAsync(
            new DeleteItemCommand(bag.OnlineUser, old, InventoryContainerType.Main,
                1, 1, 65, 5000, 1));

        Assert.Equal(ItemDiscardRejection.NoSession, result.Rejection);
        Assert.Equal(10, bag.Scalar(
            "SELECT count FROM item_slots WHERE owner_id=1 AND list_type=0 AND slot_index=65"));
        Assert.Empty(bag.Sender.Messages);
    }

    private static ValueTask Discard(ShopFixture bag, ushort slot, int itemId, int count,
        ushort operationType = 1, byte safeModeSkipIndex = 0) =>
        bag.Services.GetRequiredService<DeleteItemHandler>().HandleAsync(new(bag.Connection),
            new DeleteItemRequest(GameMessageMetadata.Empty, InventoryContainerType.Main, 1, operationType,
                slot, itemId, count, 0, safeModeSkipIndex), default);
}
