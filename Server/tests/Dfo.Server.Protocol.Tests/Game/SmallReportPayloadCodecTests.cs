using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.RosterFollowUp;
using Dfo.Server.Protocol.Game.TaggedIdReport;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// The two smallest confirmed reports. Both have a fully static layout across every client builder
/// site, so the interesting cases are the length boundaries and value preservation.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SmallReportPayloadCodecTests
{
    /// <remarks>
    /// These four used to pin the older client's five-byte <c>{u32 id, u8 flag}</c>, including its
    /// captured body <c>1F 00 00 00 01</c>. A21 puts a mode byte in front, so that body is not this
    /// message on A21 -- it is four bytes too short and its fields sit one byte early. What each
    /// test guards is kept: the observed body decodes to the right fields, the index is little
    /// endian, the indices the client hard-codes survive, and the length boundary is enforced.
    /// The boundary itself moved from "exactly five" to "at least six", which is what let every
    /// A21 CHANGE_TUTORIAL_FLAG take the connection down.
    /// </remarks>
    [Fact]
    public void TaggedIdReport_InterpretsTheObservedA21Body()
    {
        // The body a real A21 session sends on entering the tutorial: mode 0, flag index 30,
        // reward claimed. Archived in patterns/data/traces/a21-reference-entry-bytes-20260825.md.
        byte[] body = [0x00, 0x1E, 0x00, 0x00, 0x00, 0x01];
        var metadata = new GameMessageMetadata(0x01, 0x008F, 0xED60D4D0, 12);

        var request = TaggedIdReportPayloadCodec.Decode(metadata, body);

        Assert.Equal(ClientMessageType.GameTaggedIdReport, request.MessageType);
        Assert.Equal(metadata, request.Metadata);
        Assert.Equal(0, request.Mode);
        Assert.Equal(30u, request.FlagIndex);
        Assert.Equal(1, request.RewardFlag);
    }

    [Fact]
    public void TaggedIdReport_ReadsTheFlagIndexAsLittleEndianFromOffsetOne()
    {
        var request = TaggedIdReportPayloadCodec.Decode(
            GameMessageMetadata.Empty, [0x00, 0x78, 0x56, 0x34, 0x12, 0xFF]);

        Assert.Equal(0x12345678u, request.FlagIndex);
        Assert.Equal(0xFF, request.RewardFlag);
    }

    [Fact]
    public void TaggedIdReport_PreservesTheHardCodedFlagIndices()
    {
        // The two the client writes as immediates. Both carry meaning upstream: 31 ends the
        // tutorial run, 77 sets every flag at once.
        foreach (var index in (uint[])[77, 31])
        {
            var body = new byte[6];
            BitConverter.GetBytes(index).CopyTo(body, 1);
            body[5] = 1;

            Assert.Equal(
                index,
                TaggedIdReportPayloadCodec.Decode(GameMessageMetadata.Empty, body).FlagIndex);
        }
    }

    [Fact]
    public void TaggedIdReport_RejectsBodiesShorterThanSixBytesAndAcceptsLongerOnes()
    {
        Assert.Throws<InvalidDataException>(
            () => TaggedIdReportPayloadCodec.Decode(GameMessageMetadata.Empty, []));
        // The older client's whole body is one of the too-short cases now.
        Assert.Throws<InvalidDataException>(
            () => TaggedIdReportPayloadCodec.Decode(
                GameMessageMetadata.Empty, [0x1F, 0x00, 0x00, 0x00, 0x01]));

        // The client may append nine reserved bytes. Same fields, same offsets, still accepted --
        // rejecting it would drop the connection exactly as the old length check did.
        var padded = new byte[15];
        padded[1] = 0x1E;
        padded[5] = 0x01;
        var request = TaggedIdReportPayloadCodec.Decode(GameMessageMetadata.Empty, padded);

        Assert.Equal(30u, request.FlagIndex);
        Assert.Equal(1, request.RewardFlag);
    }

    [Fact]
    public void RosterFollowUp_InterpretsTheObservedBody()
    {
        var metadata = new GameMessageMetadata(0x01, 0x02A8, 0xC2096F5B, 5);

        var request = RosterFollowUpPayloadCodec.Decode(metadata, [0xFF, 0xFF]);

        Assert.Equal(ClientMessageType.GameRosterFollowUp, request.MessageType);
        Assert.Equal(metadata, request.Metadata);
        Assert.Equal(RosterFollowUpPayloadCodec.ObservedSentinel, request.Value);
    }

    [Fact]
    public void RosterFollowUp_AcceptsNonSentinelValues()
    {
        // All three builder sites write a runtime value, so the sentinel is not a validation rule.
        var request = RosterFollowUpPayloadCodec.Decode(GameMessageMetadata.Empty, [0x34, 0x12]);

        Assert.Equal(0x1234, request.Value);
    }

    [Fact]
    public void RosterFollowUp_RejectsBodiesThatAreNotTwoBytes()
    {
        Assert.Throws<InvalidDataException>(
            () => RosterFollowUpPayloadCodec.Decode(GameMessageMetadata.Empty, []));
        Assert.Throws<InvalidDataException>(
            () => RosterFollowUpPayloadCodec.Decode(GameMessageMetadata.Empty, [0xFF]));
        Assert.Throws<InvalidDataException>(
            () => RosterFollowUpPayloadCodec.Decode(GameMessageMetadata.Empty, [0xFF, 0xFF, 0x00]));
    }
}
