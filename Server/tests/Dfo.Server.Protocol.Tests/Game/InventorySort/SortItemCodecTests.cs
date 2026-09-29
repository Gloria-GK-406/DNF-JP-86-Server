using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.InventorySort;

namespace Dfo.Server.Protocol.Tests.Game.InventorySort;

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

    [Theory]
    [InlineData(0, 9, BusinessInventorySpace.AvatarEmblem)]
    [InlineData(0, 10, BusinessInventorySpace.ExpertMaterial)]
    [InlineData(1, 8, BusinessInventorySpace.Avatar)]
    [InlineData(7, 6, BusinessInventorySpace.CreatureEquipment)]
    [InlineData(7, 7, BusinessInventorySpace.CreatureConsumable)]
    public void Categories_map_to_local_business_spaces(byte list, byte category, BusinessInventorySpace expected)
    {
        Assert.True(SortItemMapping.TryMap(new(GameMessageMetadata.Empty, list, category, 0), out var space, out _));
        Assert.Equal(expected, space);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(2, 11)]
    [InlineData(7, 5)]
    [InlineData(0, 255)]
    public void Unsupported_lists_and_categories_are_rejected(byte list, byte category) =>
        Assert.False(SortItemMapping.TryMap(new(GameMessageMetadata.Empty, list, category, 0), out _, out _));
}
