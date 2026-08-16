using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Configuration;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.InitialConnection;
using Dfo.Server.Protocol.Game.Login;

namespace Dfo.Server.Protocol.Tests.Game.Login;

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

        var frame = Assert.Single(
            new GameLoginResponseEncoder(options).Encode(new GameLoginResponse(true)));

        Assert.Equal(0x01, frame[0]);
        Assert.Equal(0x0001, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));
        Assert.Equal(frame.Length, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(3, 4)));
        // Offsets follow the client's read order: four u8, a raw 4-byte port, a length-prefixed
        // host string, then the two u32 the client passes to SetEndpoint.
        var body = frame.AsSpan(GameServerFrameEncoder.HeaderLength);
        Assert.Equal(new byte[] { 0x01, 18, 0x00, 0x01 }, body[..4].ToArray());
        Assert.Equal(10128, BinaryPrimitives.ReadInt32LittleEndian(body[4..8]));
        Assert.Equal(10, BinaryPrimitives.ReadInt32LittleEndian(body[8..12]));
        Assert.Equal("192.0.2.20", Encoding.ASCII.GetString(body[12..22]));
        Assert.Equal(17200, BinaryPrimitives.ReadInt32LittleEndian(body[22..26]));
        Assert.Equal(17200, BinaryPrimitives.ReadInt32LittleEndian(body[26..30]));
        Assert.All(body[30..].ToArray(), value => Assert.Equal(0, value));
        Assert.Equal(54, body.Length);
    }

    [Fact]
    public void SharedType0001_UsesCmdForLoginAndNotiForInitialConnection()
    {
        var options = new GameLoginOptions { ServerIp = "192.0.2.20" };
        var login = Assert.Single(
            new GameLoginResponseEncoder(options).Encode(new GameLoginResponse(true)));
        var initial = Assert.Single(
            new GameInitialConnectionNoticeEncoder(options).Encode(
                new GameInitialConnectionNotice(11, DateTimeOffset.UnixEpoch)));

        Assert.Equal((byte)GameMessageClass.Cmd, login[0]);
        Assert.Equal((byte)GameMessageClass.Noti, initial[0]);
        Assert.Equal((ushort)0x0001, BinaryPrimitives.ReadUInt16LittleEndian(login.AsSpan(1, 2)));
        Assert.Equal((ushort)0x0001, BinaryPrimitives.ReadUInt16LittleEndian(initial.AsSpan(1, 2)));
    }

    [Fact]
    public void EncodeInitialConnection_RejectsAnUnrepresentableChannel()
    {
        var notice = new GameInitialConnectionNotice(256, DateTimeOffset.UnixEpoch);

        Assert.Throws<InvalidDataException>(() =>
            GameInitialConnectionPayloadCodec.EncodeBody(notice, new GameLoginOptions()));
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("2001:db8::1")]
    public void EncodeInitialConnection_RejectsANonIpv4AdvertisedAddress(string address)
    {
        var notice = new GameInitialConnectionNotice(11, DateTimeOffset.UnixEpoch);

        Assert.Throws<InvalidDataException>(() =>
            GameInitialConnectionPayloadCodec.EncodeBody(
                notice,
                new GameLoginOptions { ServerIp = address }));
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
