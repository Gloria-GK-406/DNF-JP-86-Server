using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Networking.Configuration;
using Dfo.Server.Networking.Framing;

namespace Dfo.Server.Networking.Tests.Framing;

public sealed class GameLoginCodecTests
{
    [Fact]
    public void TryDecode_ReadsTwoAsciiDStrings()
    {
        var body = EncodeDStrings("10038", "0123456789abcdef");

        var decoded = GameLoginPayloadCodec.TryDecode(
            body,
            out var loginId,
            out var passwordHash);

        Assert.True(decoded);
        Assert.Equal("10038", loginId);
        Assert.Equal("0123456789abcdef", passwordHash);
    }

    [Fact]
    public void TryDecode_RejectsTruncatedOrOversizedDStrings()
    {
        Assert.False(GameLoginPayloadCodec.TryDecode(
            [5, 0, 0, 0, (byte)'a'], out _, out _));

        var oversized = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(oversized, 257);
        Assert.False(GameLoginPayloadCodec.TryDecode(oversized, out _, out _));
    }

    [Fact]
    public void EncodeSuccess_PreservesLegacyLoginBody()
    {
        var options = new GameLoginOptions
        {
            ServerIp = "192.0.2.20",
            Environment = 0x01,
            LoginChannelPort = 10128,
            UnknownPort = 17200,
        };

        var frame = GameLoginPayloadCodec.EncodeSuccess(options);

        Assert.Equal(0x01, frame[0]);
        Assert.Equal(0x0001, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));
        Assert.Equal(frame.Length, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(3, 4)));
        var body = frame.AsSpan(GameServerFrameEncoder.HeaderLength);
        Assert.Equal(new byte[] { 0x01, 18, 0x00, 0x01, 0x00 }, body[..5].ToArray());
        Assert.Equal(10128, BinaryPrimitives.ReadInt32LittleEndian(body[5..9]));
        Assert.Equal(10, BinaryPrimitives.ReadInt32LittleEndian(body[9..13]));
        Assert.Equal("192.0.2.20", Encoding.ASCII.GetString(body[13..23]));
        Assert.Equal(17200, BinaryPrimitives.ReadInt32LittleEndian(body[23..27]));
        Assert.Equal(17200, BinaryPrimitives.ReadInt32LittleEndian(body[27..31]));
        Assert.All(body[31..].ToArray(), value => Assert.Equal(0, value));
        Assert.Equal(55, body.Length);
    }

    private static byte[] EncodeDStrings(params string[] values)
    {
        using var stream = new MemoryStream();
        foreach (var value in values)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            var length = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            stream.Write(length);
            stream.Write(bytes);
        }

        return stream.ToArray();
    }
}
