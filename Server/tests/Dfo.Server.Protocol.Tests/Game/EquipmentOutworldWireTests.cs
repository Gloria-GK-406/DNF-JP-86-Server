using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Outworld;

namespace Dfo.Server.Protocol.Tests.Game;

public sealed class EquipmentOutworldWireTests
{
    [Theory]
    [InlineData(0x12)]
    [InlineData(0x14)]
    [InlineData(0x15)]
    public void State_rejection_preserves_the_specific_reason_byte(byte reason)
    {
        Assert.Equal(new byte[] { 0, reason },
            InvestAmplifyOptionEncoder.EncodeBody(new(false, 0, ErrorCode: reason)));
    }

    [Fact]
    public void Purify_has_exact_twelve_A21_bytes_and_compatible_result()
    {
        var request = Assert.IsType<OutworldRequest>(new PurifyItemDecoder().Decode(
            GameMessageMetadata.Empty, [9, 0, 0xB8, 0x0B, 0, 0, 121, 0, 0x9F, 4, 0, 0]));
        Assert.True(request.Purify);
        Assert.Equal((short)9, request.TargetSlot);
        Assert.Equal(3000, request.TargetItemId);
        Assert.Equal((short)121, request.MaterialSlot);
        Assert.Equal(1183, request.MaterialItemId);
        Assert.Equal(ClientMessageType.GamePurifyItem, request.MessageType);
        Assert.Throws<InvalidDataException>(() => new PurifyItemDecoder().Decode(
            GameMessageMetadata.Empty, new byte[13]));
        Assert.Equal([1, 121, 0, 1, 0, 0, 0, 9, 0, 3, 5, 0],
            PurifyItemEncoder.EncodeBody(new(true, 121, 1, 9, 3, 5)));
        Assert.Equal([0, 0, 0, 0], PurifyItemEncoder.EncodeBody(new(false)));
    }

    [Fact]
    public void Invest_and_twist_have_fourteen_fixed_bytes_and_error_response()
    {
        var decoder = new InvestAmplifyOptionDecoder();
        var request = Assert.IsType<OutworldRequest>(decoder.Decode(GameMessageMetadata.Empty,
            [1, 9, 0, 0xB8, 0x0B, 0, 0, 121, 0, 0xA4, 4, 0, 0, 3]));
        Assert.False(request.Purify);
        Assert.Equal((byte)1, request.WireAction);
        Assert.Equal((byte)3, request.SelectedOption);
        Assert.Equal(1188, request.MaterialItemId);
        Assert.Equal(ClientMessageType.GameInvestAmplifyOption, request.MessageType);
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty,
            new byte[13]));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty,
            new byte[15]));
        var dynamicAction = Assert.IsType<OutworldRequest>(decoder.Decode(GameMessageMetadata.Empty,
            [2, 9, 0, 0xB8, 0x0B, 0, 0, 121, 0, 0xA4, 4, 0, 0, 0, 0x7F]));
        Assert.Equal((byte)2, dynamicAction.WireAction);
        Assert.Equal([1, 1, 121, 0, 0, 0, 0, 0, 9, 0, 3, 5, 0],
            InvestAmplifyOptionEncoder.EncodeBody(new(true, 1, 121, 0, 9, 3, 5)));
        Assert.Equal([0, 0x11], InvestAmplifyOptionEncoder.EncodeBody(new(false, 1, ErrorCode: 0x11)));
    }
}
