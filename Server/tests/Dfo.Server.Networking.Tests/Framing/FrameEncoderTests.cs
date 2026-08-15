using Dfo.Server.Networking.Framing;

namespace Dfo.Server.Networking.Tests.Framing;

public sealed class FrameEncoderTests
{
    [Fact]
    public void Channel_encoder_matches_the_reverse_engineered_layout()
    {
        var actual = ChannelFrameEncoder.Encode(
            classification: 0x7C,
            messageNumber: 0x02,
            checksum: 0x12345678,
            acknowledgement: 0x01,
            body: new byte[] { 0xAA, 0xBB });

        Assert.Equal(
            new byte[]
            {
                0x7C, 0x02,
                0x0D, 0x00, 0x00, 0x00,
                0x78, 0x56, 0x34, 0x12,
                0x01,
                0xAA, 0xBB
            },
            actual);
    }

    [Fact]
    public void Game_server_encoder_keeps_the_observed_fifteen_byte_header()
    {
        var actual = GameServerFrameEncoder.Encode(
            selector: 0x01,
            type: 0x2345,
            body: new byte[] { 0xAA, 0xBB });

        Assert.Equal(
            new byte[]
            {
                0x01,
                0x45, 0x23,
                0x11, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0xAA, 0xBB
            },
            actual);
    }

    [Fact]
    public void Game_server_encoder_writes_opaque_header_values()
    {
        var actual = GameServerFrameEncoder.Encode(
            selector: 0x00,
            type: 0x0002,
            body: new byte[] { 0xAA },
            checksum: 0x11223344,
            reserved: 0x55667788);

        Assert.Equal(
            new byte[]
            {
                0x00,
                0x02, 0x00,
                0x10, 0x00, 0x00, 0x00,
                0x44, 0x33, 0x22, 0x11,
                0x88, 0x77, 0x66, 0x55,
                0xAA,
            },
            actual);
    }
}
