using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.InitialConnection;
using Dfo.Server.Protocol.Game.Login;

namespace Dfo.Server.Protocol.Tests.Game.Login;

[Trait("Category", "Unit")]
public sealed class GameLoginCodecTests
{
    /// <summary>
    /// The login answer, transcribed from a session the A21 client accepted.
    /// </summary>
    /// <remarks>
    /// Only the host differs from what this server sends: that session advertised
    /// <c>127.0.0.2</c> and this one is configured with <c>127.0.0.1</c>. Everything else --
    /// the five leading bytes including the revision, the connection port, the two endpoint ports
    /// and the twenty-four byte tail -- is asserted as it was on the wire.
    ///
    /// Worth stating plainly: the client accepted the previous, one-byte-shorter body too. This
    /// guard exists because "the client did not complain" and "the bytes are right" are different
    /// claims, and only the second one survives a later field that depends on the difference.
    /// </remarks>
    [Fact]
    public void The_success_body_matches_the_session_the_client_accepted()
    {
        var body = GameLoginResponseEncoder.EncodeSuccessBody(
            new GameLoginResponse(
                Succeeded: true,
                Environment: 1,
                ConnectionPort: 10128,
                EndpointHost: "127.0.0.2",
                EndpointPortA: 17200,
                EndpointPortB: 17200));

        Assert.Equal(
            new byte[]
            {
                0x01, 0x14, 0x00, 0x01, 0x00,
                0x90, 0x27, 0x00, 0x00,
                0x09, 0x00, 0x00, 0x00,
                0x31, 0x32, 0x37, 0x2E, 0x30, 0x2E, 0x30, 0x2E, 0x32,
                0x30, 0x43, 0x00, 0x00,
                0x30, 0x43, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            },
            body);
    }

    [Fact]
    public void The_success_body_is_forty_five_bytes_plus_the_host()
    {
        var body = GameLoginResponseEncoder.EncodeSuccessBody(
            new GameLoginResponse(
                Succeeded: true,
                Environment: 1,
                ConnectionPort: 10128,
                EndpointHost: "192.0.2.20",
                EndpointPortA: 17200,
                EndpointPortB: 17201));

        Assert.Equal(45 + "192.0.2.20".Length, body.Length);
    }

    [Fact]
    public void TryDecode_ReadsTwoAsciiDStrings()
    {
        var body = EncodeDStrings("10038", "0123456789abcdef");

        var decoded = GameLoginPayloadCodec.TryDecode(
            body,
            out var loginId,
            out var ticket);

        Assert.True(decoded);
        Assert.Equal("10038", loginId);
        Assert.Equal("0123456789abcdef", ticket);
    }

    [Fact]
    public void TryDecode_PreservesTheA21TwoDStringShapeAndIgnoresItsKnownTail()
    {
        var body = EncodeDStrings("player", new string('t', 43))
            .Concat(new byte[] { 0xA2, 0x10, 0x00, 0x7F })
            .ToArray();

        var decoded = GameLoginPayloadCodec.TryDecode(body, out var loginId, out var ticket);

        Assert.True(decoded);
        Assert.Equal("player", loginId);
        Assert.Equal(new string('t', 43), ticket);
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

    /// <summary>
    /// The frame around the body, and the body's fields named at their offsets.
    /// </summary>
    /// <remarks>
    /// The offsets moved by one when this stopped targeting the A12 client: that client took four
    /// leading bytes before the port and A21 takes five. The transcribed-body test above is the
    /// golden; this one says which field is which, which a literal cannot.
    /// </remarks>
    [Fact]
    public void EncodeSuccess_NamesEachFieldOfTheLoginBody()
    {
        var frame = Assert.Single(
            new GameLoginResponseEncoder().Encode(CreateLoginResponse()));

        Assert.Equal(0x01, frame[0]);
        Assert.Equal(0x0001, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));
        Assert.Equal(frame.Length, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(3, 4)));
        var body = frame.AsSpan(GameServerFrameEncoder.HeaderLength);
        Assert.Equal(new byte[] { 0x01, 20, 0x00, 0x01, 0x00 }, body[..5].ToArray());
        Assert.Equal(10128, BinaryPrimitives.ReadInt32LittleEndian(body[5..9]));
        Assert.Equal(10, BinaryPrimitives.ReadInt32LittleEndian(body[9..13]));
        Assert.Equal("192.0.2.20", Encoding.ASCII.GetString(body[13..23]));
        Assert.Equal(17200, BinaryPrimitives.ReadInt32LittleEndian(body[23..27]));
        Assert.Equal(17200, BinaryPrimitives.ReadInt32LittleEndian(body[27..31]));
        Assert.All(body[31..].ToArray(), value => Assert.Equal(0, value));
        Assert.Equal(55, body.Length);
    }

    [Fact]
    public void SharedType0001_UsesCmdForLoginAndNotiForInitialConnection()
    {
        var login = Assert.Single(
            new GameLoginResponseEncoder().Encode(CreateLoginResponse()));
        var initial = Assert.Single(
            new GameInitialConnectionNoticeEncoder().Encode(
                new GameInitialConnectionNotice(
                    11, DateTimeOffset.UnixEpoch, "192.0.2.20")));

        Assert.Equal((byte)GameMessageClass.Cmd, login[0]);
        Assert.Equal((byte)GameMessageClass.Noti, initial[0]);
        Assert.Equal((ushort)0x0001, BinaryPrimitives.ReadUInt16LittleEndian(login.AsSpan(1, 2)));
        Assert.Equal((ushort)0x0001, BinaryPrimitives.ReadUInt16LittleEndian(initial.AsSpan(1, 2)));
    }

    [Fact]
    public void EncodeInitialConnection_RejectsAnUnrepresentableChannel()
    {
        var notice = new GameInitialConnectionNotice(
            256, DateTimeOffset.UnixEpoch, "192.0.2.20");

        Assert.Throws<InvalidDataException>(() =>
            GameInitialConnectionPayloadCodec.EncodeBody(notice));
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("2001:db8::1")]
    public void EncodeInitialConnection_RejectsANonIpv4AdvertisedAddress(string address)
    {
        var notice = new GameInitialConnectionNotice(
            11, DateTimeOffset.UnixEpoch, address);

        Assert.Throws<InvalidDataException>(() =>
            GameInitialConnectionPayloadCodec.EncodeBody(notice));
    }

    private static GameLoginResponse CreateLoginResponse() =>
        new(true, 0x01, 10128, "192.0.2.20", 17200, 17200);

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
