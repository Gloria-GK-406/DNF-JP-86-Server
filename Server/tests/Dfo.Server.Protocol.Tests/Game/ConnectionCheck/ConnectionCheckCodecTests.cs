using System.Buffers.Binary;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol.Game.ConnectionCheck;

namespace Dfo.Server.Protocol.Tests.Game.ConnectionCheck;

[Trait("Category", "Unit")]
public sealed class ConnectionCheckCodecTests
{
    private static GameMessageMetadata Probe(ushort sequence) =>
        new(Selector: 0x01, Type: 0x04DD, Checksum: 0xBD819FB5, Sequence: sequence);

    [Fact]
    public void An_empty_probe_decodes()
    {
        var request = ConnectionCheckPayloadCodec.Decode(Probe(0), []);

        Assert.Equal(ClientMessageType.GameConnectionCheck, request.MessageType);
        Assert.Equal(0x04DD, request.Metadata.Type);
    }

    /// <summary>
    /// A probe that arrives with a body means the header boundary moved, so it is refused here.
    /// </summary>
    /// <remarks>
    /// Reading the fourteen-byte frame with a thirteen-byte header produces exactly this: the
    /// same type, carrying one byte that is really the header's own. Accepting it would hide the
    /// boundary defect behind a probe that still gets answered.
    /// </remarks>
    [Fact]
    public void A_probe_carrying_a_body_is_refused()
    {
        Assert.Throws<InvalidDataException>(
            () => ConnectionCheckPayloadCodec.Decode(Probe(0), [0x00]));
    }

    /// <summary>
    /// The answer is the bare CMD acceptance byte, which is the whole body the client received.
    /// </summary>
    [Fact]
    public void The_answer_is_a_single_acceptance_byte()
    {
        Assert.Equal(new byte[] { 0x01 }, ConnectionCheckPayloadCodec.EncodeResponse());
    }

    /// <summary>
    /// The client's probe, verbatim, so the wire type is pinned to a measured frame.
    /// </summary>
    private static readonly byte[] MeasuredProbeFrame =
    [
        0x01, 0xDD, 0x04, 0x0E, 0x00, 0x00, 0x00,
        0x8A, 0xD6, 0x51, 0xF4, 0x00, 0x0D, 0x00,
    ];

    /// <summary>
    /// The answer goes out under the same wire type the probe arrived on.
    /// </summary>
    /// <remarks>
    /// Everything else about this slice is self-consistent: the decoder is registered under the
    /// same constant the encoder emits, so changing that constant changes both and no test
    /// notices -- while the client, which is not consulted, stops being answered. The only thing
    /// that can catch it is a frame the client actually sent, which is why the probe is
    /// transcribed here rather than rebuilt from the constant under test.
    /// </remarks>
    [Fact]
    public void The_answer_carries_the_wire_type_the_probe_arrived_on()
    {
        var probedType = BinaryPrimitives.ReadUInt16LittleEndian(MeasuredProbeFrame.AsSpan(1, 2));

        var frame = Assert.Single(
            new ConnectionCheckResponseEncoder().Encode(new ConnectionCheckResponse()));
        var answeredType = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2));

        Assert.Equal(probedType, answeredType);
        Assert.Equal(0x01, frame[0]);
        Assert.Equal(new byte[] { 0x01 }, frame[GameServerFrameEncoder.HeaderLength..]);
    }
}
