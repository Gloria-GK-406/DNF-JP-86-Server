using System.Buffers.Binary;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildDonation;

namespace Dfo.Server.Protocol.Tests.Game.GuildDonation;

public sealed class GuildDonationCodecTests
{
    [Fact]
    public void Decoder_accepts_only_the_single_signed_four_byte_amount()
    {
        var decoder = new GuildDonationDecoder();
        var request = Assert.IsType<GuildDonationRequest>(decoder.Decode(GameMessageMetadata.Empty, [0x10, 0x27, 0, 0]));
        Assert.Equal(ClientMessageType.GameGuildDonateGold, request.MessageType);
        Assert.Equal(10_000, request.Gold);
        Assert.Equal(-1, Assert.IsType<GuildDonationRequest>(
            decoder.Decode(GameMessageMetadata.Empty, [0xFF, 0xFF, 0xFF, 0xFF])).Gold);
        Assert.Equal(0, Assert.IsType<GuildDonationRequest>(
            decoder.Decode(GameMessageMetadata.Empty, [0x10, 0x27, 0])).Gold);
        Assert.Equal(0, Assert.IsType<GuildDonationRequest>(
            decoder.Decode(GameMessageMetadata.Empty, [0x10, 0x27, 0, 0, 0])).Gold);
    }

    [Fact]
    public void Reply_uses_the_native_success_flag_and_refusal_code()
    {
        var encoder = new GuildDonationReplyEncoder();
        var accepted = Assert.Single(encoder.Encode(new GuildDonationReply()));
        Assert.Equal((byte)1, accepted[0]);
        Assert.Equal((ushort)0x015A, BinaryPrimitives.ReadUInt16LittleEndian(accepted.AsSpan(1)));
        Assert.Equal(new byte[] { 1 }, accepted[15..]);
        var refused = Assert.Single(encoder.Encode(new GuildDonationReply(0x0A)));
        Assert.Equal(new byte[] { 0, 0x0A }, refused[15..]);
    }
}
