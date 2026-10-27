using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.EnchantByBead;

namespace Dfo.Server.Protocol.Tests.Game;

[Trait("Category", "Unit")]
public sealed class EnchantByBeadWireTests
{
    [Fact]
    public void Request_reads_six_bytes_in_A21_write_order()
    {
        var message = Assert.IsType<EnchantByBeadRequest>(new EnchantByBeadDecoder().Decode(
            GameMessageMetadata.Empty, [0, 121, 0, 3, 12, 0]));
        Assert.Equal(InventoryContainerType.Main, message.BeadList);
        Assert.Equal((short)121, message.BeadSlot);
        Assert.Equal(InventoryContainerType.Equipment, message.TargetList);
        Assert.Equal((short)12, message.TargetSlot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(7)]
    public void Decoder_refuses_other_lengths(int length) =>
        Assert.Throws<InvalidDataException>(() => new EnchantByBeadDecoder().Decode(
            GameMessageMetadata.Empty, new byte[length]));

    [Fact]
    public void Ack_matches_A21_static_success_and_failure_read_shapes()
    {
        Assert.Equal([1, 3, 12, 0], EnchantByBeadEncoder.EncodeBody(
            new(true, TargetList: InventoryContainerType.Equipment, TargetSlot: 12)));
        Assert.Equal([0, 0x17], EnchantByBeadEncoder.EncodeBody(new(false, 0x17)));
    }
}
