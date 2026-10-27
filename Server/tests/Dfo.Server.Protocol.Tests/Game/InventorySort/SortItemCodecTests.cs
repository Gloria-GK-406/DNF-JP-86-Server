using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.InventorySort;

namespace Dfo.Server.Protocol.Tests.Game.InventorySort;

[Trait("Category", "Unit")]
public sealed class SortItemCodecTests
{
    [Fact]
    public void Captured_equipment_request_and_ack_layout()
    {
        var decoder = new SortItemMessageDecoder();
        var request = Assert.IsType<SortItemRequest>(decoder.Decode(GameMessageMetadata.Empty, [0, 1, 0]));
        Assert.True(SortItemMapping.TryMap(request, out var space, out var order));
        Assert.Equal(BusinessInventorySpace.CarriedEquipment, space);
        Assert.Equal(InventorySortOrder.Default, order);
        Assert.Equal(new byte[] { 1, 0 }, SortItemResponseEncoder.EncodeBody(new(0, true)));
        Assert.Equal(new byte[] { 0, 1, 7 }, SortItemResponseEncoder.EncodeBody(new(7, false, 1)));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, [0]));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, [0, 1, 0, 0]));
    }

    [Fact]
    public void Lock_capture_reads_list_and_little_endian_slot_and_unlock_is_explicit()
    {
        var request = Assert.IsType<SortLockRequest>(new SortLockDecoder().Decode(GameMessageMetadata.Empty, [0, 28, 0]));
        Assert.Equal(new GameSlot(InventoryContainerType.Main, 28), request.Slot);
        Assert.Equal(InventorySortLockAction.Toggle, request.Action);
        var unlock = Assert.IsType<SortLockRequest>(new SortUnlockDecoder().Decode(GameMessageMetadata.Empty, [0, 60, 0]));
        Assert.Equal(60, unlock.Slot.Index);
        Assert.Equal(InventorySortLockAction.Unlock, unlock.Action);
        Assert.Throws<InvalidDataException>(() => new SortLockDecoder().Decode(GameMessageMetadata.Empty, [0]));
        Assert.Throws<InvalidDataException>(() => new SortUnlockDecoder().Decode(GameMessageMetadata.Empty, [0, 0, 0, 0]));
    }

    [Theory]
    [InlineData(0, 9, BusinessInventorySpace.AvatarEmblem)]
    [InlineData(0, 10, BusinessInventorySpace.ExpertMaterial)]
    [InlineData(1, 8, BusinessInventorySpace.Avatar)]
    [InlineData(2, 11, BusinessInventorySpace.PersonalCargo)]
    [InlineData(12, 11, BusinessInventorySpace.AccountCargo)]
    [InlineData(7, 6, BusinessInventorySpace.CreatureEquipment)]
    [InlineData(7, 7, BusinessInventorySpace.CreatureConsumable)]
    public void Categories_map_to_local_business_spaces(byte list, byte category, BusinessInventorySpace expected)
    {
        Assert.True(SortItemMapping.TryMap(new(GameMessageMetadata.Empty, list, category, 0), out var space, out _));
        Assert.Equal(expected, space);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(7, 5)]
    [InlineData(0, 255)]
    public void Unsupported_lists_and_categories_are_rejected(byte list, byte category) =>
        Assert.False(SortItemMapping.TryMap(new(GameMessageMetadata.Empty, list, category, 0), out _, out _));
}
