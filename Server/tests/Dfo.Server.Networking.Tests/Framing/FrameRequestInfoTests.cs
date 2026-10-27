using System.Buffers.Binary;
using Dfo.Server.Networking.Framing;

namespace Dfo.Server.Networking.Tests.Framing;

[Trait("Category", "Unit")]
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
        header[11] = 0x00;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 0x6789);

        var request = new GameClientFrameRequestInfo();

        Assert.True(request.OnParsingHeader(header));
        Assert.True(request.OnParsingBody(body));
        Assert.Equal(0x01, request.Selector);
        Assert.Equal(0x2345, request.Type);
        Assert.Equal(0x89ABCDEFu, request.Checksum);
        Assert.Equal(0x00, request.Reserved);
        Assert.Equal(0x6789, request.Sequence);
        Assert.Equal(body, request.Body.ToArray());
    }

    /// <summary>
    /// The client's connection probe, taken verbatim off the wire.
    /// </summary>
    /// <remarks>
    /// This is the frame that exposed the header boundary: fourteen bytes carrying nothing. Read
    /// with a thirteen-byte header it still parses -- as this same type with a one-byte body --
    /// which is exactly why the defect survived: nothing throws, the body is merely wrong, and the
    /// frame is then dropped for having no decoder.
    ///
    /// Transcribed from the archived session, so the checksum and counter here are the ones that
    /// were actually on the wire.
    /// </remarks>
    [Fact]
    public void The_clients_empty_connection_probe_parses_with_no_body()
    {
        byte[] frame =
        [
            0x01, 0xDD, 0x04, 0x0E, 0x00, 0x00, 0x00,
            0x8A, 0xD6, 0x51, 0xF4, 0x00, 0x0D, 0x00,
        ];

        var request = new GameClientFrameRequestInfo();

        Assert.True(request.OnParsingHeader(frame));
        Assert.Equal(0x01, request.Selector);
        Assert.Equal(0x04DD, request.Type);
        Assert.Equal(14u, request.TotalLength);
        Assert.Equal(0, request.BodyLength);
        Assert.True(request.OnParsingBody([]));
        Assert.Empty(request.Body.ToArray());
    }

    /// <summary>
    /// A login frame off the same session, kept for its header alone.
    /// </summary>
    /// <remarks>
    /// The body is not asserted and not reproduced here: it carries the account name and the
    /// password hash, which the protocol knowledge base is not allowed to hold. Only the header
    /// and the resulting body length are the point -- 87 total means 73 of payload, and a
    /// thirteen-byte header would claim 74 and take a byte that belongs to the next frame.
    /// </remarks>
    [Fact]
    public void A_login_frames_header_yields_the_measured_body_length()
    {
        byte[] header =
        [
            0x01, 0x01, 0x00, 0x57, 0x00, 0x00, 0x00,
            0xD3, 0x5D, 0xD7, 0x0E, 0x00, 0x00, 0x00,
        ];

        var request = new GameClientFrameRequestInfo();

        Assert.True(request.OnParsingHeader(header));
        Assert.Equal(0x0001, request.Type);
        Assert.Equal(87u, request.TotalLength);
        Assert.Equal(73, request.BodyLength);
    }

    /// <summary>
    /// The three trailing-byte patterns the first three frames of a connection carry.
    /// </summary>
    /// <remarks>
    /// The trailing bytes are measured -- the known-good session's first three inbound frames end
    /// in <c>00 00 00</c>, <c>00 01 00</c>, <c>00 02 00</c> -- but the headers around them here are
    /// synthetic: all three reuse one type and one checksum so that only the bytes under test
    /// differ. The real three frames are three different types with three different checksums.
    ///
    /// The upstream reference places the sequence one byte earlier and calls the last byte
    /// "extra". Read that way these count 0, 256, 512. Read as they are read here they count
    /// 0, 1, 2, which is what a per-connection frame counter does; across all 190 inbound frames
    /// that reading gives 189 consecutive increments and the earlier one gives none.
    /// </remarks>
    [Theory]
    [InlineData(0x00, 0x00, 0)]
    [InlineData(0x01, 0x00, 1)]
    [InlineData(0x02, 0x00, 2)]
    public void The_frame_counter_is_the_last_two_bytes(byte low, byte high, int expected)
    {
        byte[] header =
        [
            0x01, 0xDD, 0x04, 0x0E, 0x00, 0x00, 0x00,
            0x8A, 0xD6, 0x51, 0xF4, 0x00, low, high,
        ];

        var request = new GameClientFrameRequestInfo();

        Assert.True(request.OnParsingHeader(header));
        Assert.Equal(0x00, request.Reserved);
        Assert.Equal(expected, request.Sequence);
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
