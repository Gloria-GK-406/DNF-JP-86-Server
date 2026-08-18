using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.FinishLoading;

namespace Dfo.Server.Protocol.Tests.Game.FinishLoading;

/// <summary>
/// The gate at the end of the loading bar.
/// </summary>
/// <remarks>
/// Unlike the dungeon-entry frames, this pair has a live observation behind it. On 2026-08-18 the
/// client reached the dungeon's loading screen, sent this request, received nothing, and stopped
/// at the last sliver of the bar until it timed out and reported a failed connection. That is the
/// evidence that the reply is required, rather than merely registered for.
/// </remarks>
public sealed class FinishLoadingCodecTests
{
    /// <remarks>
    /// The observed body: eight bytes, all zero. Both u32 fields are read and neither is
    /// interpreted -- their boundaries are closed but nothing yet distinguishes their meanings.
    /// </remarks>
    [Fact]
    public void The_observed_request_decodes_as_two_zero_words()
    {
        byte[] body = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        var request = (FinishLoadingRequest)new FinishLoadingDecoder()
            .Decode(GameMessageMetadata.Empty, body)!;

        Assert.Equal(0u, request.First);
        Assert.Equal(0u, request.Second);
    }

    [Fact]
    public void Both_words_are_read_little_endian_and_kept_apart()
    {
        byte[] body = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];

        var request = (FinishLoadingRequest)new FinishLoadingDecoder()
            .Decode(GameMessageMetadata.Empty, body)!;

        Assert.Equal(0x04030201u, request.First);
        Assert.Equal(0x08070605u, request.Second);
    }

    [Fact]
    public void A_short_body_is_refused()
    {
        Assert.Throws<InvalidDataException>(() =>
            new FinishLoadingDecoder().Decode(GameMessageMetadata.Empty, new byte[7]));
    }

    /// <remarks>
    /// One byte. The reference server's common acknowledgement writes <c>01</c> for success and
    /// <c>00</c> followed by an error code for failure, so a success carries nothing else.
    /// </remarks>
    [Fact]
    public void The_acknowledgement_is_a_single_success_byte()
    {
        Assert.Equal([0x01], FinishLoadingPayloadCodec.EncodeResponse());
    }

    /// <remarks>
    /// Five zero bytes, as the reference builder writes them: an int32 and a byte, both zero.
    /// This is the frame that actually opens the gate; the acknowledgement alone does not.
    /// </remarks>
    [Fact]
    public void The_notification_that_opens_the_gate_is_five_zero_bytes()
    {
        Assert.Equal(
            [0x00, 0x00, 0x00, 0x00, 0x00],
            FinishLoadingPayloadCodec.EncodeLoadingComplete());
    }
}
