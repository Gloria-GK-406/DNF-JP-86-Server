using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.MagicSeal;

namespace Dfo.Server.Protocol.Tests.Game;

public sealed class MagicSealWireTests
{
    [Fact]
    public void Unseal_reads_exact_A21_four_bytes()
    {
        var request = Assert.IsType<MagicSealRequest>(new UnsealRandomOptionDecoder().Decode(
            GameMessageMetadata.Empty, [9, 0, 0xFF, 0xFF]));
        Assert.False(request.Change);
        Assert.Equal((short)9, request.TargetSlot);
        Assert.Equal((short)-1, request.MaterialSlot);
        Assert.Equal(ClientMessageType.GameUnsealRandomOption, request.MessageType);
        Assert.Throws<InvalidDataException>(() => new UnsealRandomOptionDecoder().Decode(
            GameMessageMetadata.Empty, new byte[5]));
    }

    [Fact]
    public void Change_reads_exact_A21_five_bytes_and_compatibility_ack()
    {
        var request = Assert.IsType<MagicSealRequest>(new ChangeRandomOptionDecoder().Decode(
            GameMessageMetadata.Empty, [9, 0, 0, 0, 2]));
        Assert.True(request.Change);
        Assert.Equal((short)9, request.TargetSlot);
        Assert.Equal((short)0, request.MaterialSlot);
        Assert.Equal((byte)2, request.OptionIndex);
        Assert.Equal(ClientMessageType.GameChangeRandomOption, request.MessageType);
        Assert.Throws<InvalidDataException>(() => new ChangeRandomOptionDecoder().Decode(
            GameMessageMetadata.Empty, new byte[4]));
        Assert.Equal([1], ChangeRandomOptionEncoder.EncodeBody(new(true)));
        Assert.Equal([0, 0x0A], ChangeRandomOptionEncoder.EncodeBody(new(false, 0x0A)));
    }
}
