using System.Buffers.Binary;
using Dfo.Server.Networking.Framing;

namespace Dfo.Server.Networking.Tests.Framing;

public sealed class FrameRequestInfoTests
{
    [Fact]
    public void Channel_header_is_parsed_as_little_endian_total_length_frame()
    {
        var body = new byte[] { 0x10, 0x20, 0x30 };
        var header = new byte[ChannelFrameRequestInfo.HeaderLength];
        header[0] = 0x7C;
        header[1] = 0x03;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(2), (uint)(header.Length + body.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(6), 0x12345678);
        header[10] = 0x01;

        var request = new ChannelFrameRequestInfo();

        Assert.True(request.OnParsingHeader(header));
        Assert.True(request.OnParsingBody(body));
        Assert.Equal(0x7C, request.Classification);
        Assert.Equal(0x03, request.MessageNumber);
        Assert.Equal(0x12345678u, request.Checksum);
        Assert.Equal(0x01, request.Acknowledgement);
        Assert.Equal(body.Length, request.BodyLength);
        Assert.Equal(body, request.Body.ToArray());
    }

    [Fact]
    public void Game_client_header_is_parsed_as_little_endian_total_length_frame()
    {
        var body = new byte[] { 0xAA, 0xBB };
        var header = new byte[GameClientFrameRequestInfo.HeaderLength];
        header[0] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(1), 0x2345);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(3), (uint)(header.Length + body.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(7), 0x89ABCDEF);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(11), 0x6789);

        var request = new GameClientFrameRequestInfo();

        Assert.True(request.OnParsingHeader(header));
        Assert.True(request.OnParsingBody(body));
        Assert.Equal(0x01, request.Selector);
        Assert.Equal(0x2345, request.Type);
        Assert.Equal(0x89ABCDEFu, request.Checksum);
        Assert.Equal(0x6789, request.Sequence);
        Assert.Equal(body, request.Body.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(ChannelFrameRequestInfo.HeaderLength - 1)]
    [InlineData(DnfFrameProtocol.MaximumFrameLength + 1)]
    public void Channel_rejects_invalid_total_length(uint totalLength)
    {
        var header = new byte[ChannelFrameRequestInfo.HeaderLength];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(2), totalLength);

        var exception = Assert.Throws<FrameProtocolException>(
            () => new ChannelFrameRequestInfo().OnParsingHeader(header));

        Assert.Equal(totalLength, exception.TotalLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(GameClientFrameRequestInfo.HeaderLength - 1)]
    [InlineData(uint.MaxValue)]
    public void Game_client_rejects_invalid_total_length(uint totalLength)
    {
        var header = new byte[GameClientFrameRequestInfo.HeaderLength];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(3), totalLength);

        Assert.Throws<FrameProtocolException>(
            () => new GameClientFrameRequestInfo().OnParsingHeader(header));
    }
}
