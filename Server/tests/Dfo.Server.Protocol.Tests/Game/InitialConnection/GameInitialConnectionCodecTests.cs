using System.Text;
using Dfo.Server.Protocol.Game.InitialConnection;

namespace Dfo.Server.Protocol.Tests.Game.InitialConnection;

[Trait("Category", "Unit")]
public sealed class GameInitialConnectionCodecTests
{
    /// <summary>
    /// The notice body, reproduced byte for byte from a session the A21 client accepted.
    /// </summary>
    /// <remarks>
    /// Every value here is transcribed, not derived: channel <c>ch.10</c>, host
    /// <c>127.0.0.2</c>, the two UDP ports, the two ASCII zero markers, and the client's two
    /// dispatch-table sizes. Keeping the whole body as one literal is the point -- the defect this
    /// guards against was not a wrong value but a missing field, and a per-field assertion would
    /// have passed straight through it.
    /// </remarks>
    private static readonly byte[] KnownGoodBody =
    [
        0x01,
        0x05, 0x00, 0x00, 0x00, 0x63, 0x68, 0x2E, 0x31, 0x30,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x01,
        0x0A,
        0x00,
        0x02, 0xCD, 0x89, 0x6A,
        0x01, 0x00, 0x00, 0x00,
        0x09, 0x00, 0x00, 0x00, 0x31, 0x32, 0x37, 0x2E, 0x30, 0x2E, 0x30, 0x2E, 0x32,
        0x17, 0x30, 0x00, 0x00,
        0x18, 0x30, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x30, 0x30,
        0xF7, 0x04, 0x00, 0x00,
        0xC2, 0x04, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
    ];

    private const long KnownGoodPreparedAt = 1787415810;

    [Fact]
    public void The_notice_reproduces_the_body_the_client_accepted()
    {
        var body = GameInitialConnectionPayloadCodec.EncodeBody(
            new GameInitialConnectionNotice(
                ChannelId: 10,
                PreparedAt: DateTimeOffset.FromUnixTimeSeconds(KnownGoodPreparedAt),
                EndpointHost: "127.0.0.2"));

        Assert.Equal(KnownGoodBody, body);
    }

    /// <summary>
    /// The body is sixty-eight bytes, and the eight this codec used to omit are the reason.
    /// </summary>
    /// <remarks>
    /// While the codec targeted the older client it wrote sixty: no extra-address count and no
    /// trailing word. The frame was still well formed and still delivered, so nothing on this side
    /// complained; the client simply read the two table sizes out of the wrong place, disagreed
    /// with itself, and closed the socket a moment later.
    /// </remarks>
    [Fact]
    public void The_body_is_sixty_eight_bytes_for_a_nine_character_host()
    {
        var body = GameInitialConnectionPayloadCodec.EncodeBody(
            new GameInitialConnectionNotice(
                ChannelId: 11,
                PreparedAt: DateTimeOffset.FromUnixTimeSeconds(KnownGoodPreparedAt),
                EndpointHost: "127.0.0.1"));

        Assert.Equal(68, body.Length);
    }

    /// <summary>
    /// The two markers are the ASCII digit, not the zero byte.
    /// </summary>
    /// <remarks>
    /// This one is worth its own guard because both readings produce a body of the right length.
    /// Only the content differs, and only the client notices.
    /// </remarks>
    [Fact]
    public void The_two_markers_are_ascii_zero_characters()
    {
        var body = GameInitialConnectionPayloadCodec.EncodeBody(
            new GameInitialConnectionNotice(
                ChannelId: 11,
                PreparedAt: DateTimeOffset.FromUnixTimeSeconds(KnownGoodPreparedAt),
                EndpointHost: "127.0.0.1"));

        var markers = body.AsSpan(body.Length - 14, 2).ToArray();

        Assert.Equal(Encoding.ASCII.GetBytes("00"), markers);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void A_channel_id_that_does_not_fit_the_wire_is_refused(int channelId)
    {
        Assert.Throws<InvalidDataException>(
            () => GameInitialConnectionPayloadCodec.EncodeBody(
                new GameInitialConnectionNotice(
                    channelId,
                    DateTimeOffset.FromUnixTimeSeconds(KnownGoodPreparedAt),
                    "127.0.0.1")));
    }
}
