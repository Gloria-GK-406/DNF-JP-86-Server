using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.RosterFollowUp;
using Dfo.Server.Protocol.Game.TaggedIdReport;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// The two smallest confirmed reports. Both have a fully static layout across every client builder
/// site, so the interesting cases are the length boundaries and value preservation.
/// </summary>
public sealed class SmallReportPayloadCodecTests
{
    [Fact]
    public void TaggedIdReport_InterpretsTheObservedBody()
    {
        // Real captured body (protocol record E-001): id 31, flag 1.
        byte[] body = [0x1F, 0x00, 0x00, 0x00, 0x01];
        var metadata = new GameMessageMetadata(0x01, 0x008F, 0xED60D4D0, 12);

        var request = TaggedIdReportPayloadCodec.Decode(metadata, body);

        Assert.Equal(ClientMessageType.GameTaggedIdReport, request.MessageType);
        Assert.Equal(metadata, request.Metadata);
        Assert.Equal(31u, request.Id);
        Assert.Equal(1, request.Flag);
    }

    [Fact]
    public void TaggedIdReport_ReadsTheIdAsLittleEndian()
    {
        var request = TaggedIdReportPayloadCodec.Decode(
            GameMessageMetadata.Empty, [0x78, 0x56, 0x34, 0x12, 0xFF]);

        Assert.Equal(0x12345678u, request.Id);
        Assert.Equal(0xFF, request.Flag);
    }

    [Fact]
    public void TaggedIdReport_PreservesTheHardCodedEnumIds()
    {
        // Two builder sites write these as immediates, so both must survive decoding.
        foreach (var id in (uint[])[77, 38])
        {
            var body = new byte[5];
            BitConverter.GetBytes(id).CopyTo(body, 0);
            body[4] = 1;

            Assert.Equal(id, TaggedIdReportPayloadCodec.Decode(GameMessageMetadata.Empty, body).Id);
        }
    }

    [Fact]
    public void TaggedIdReport_RejectsBodiesThatAreNotFiveBytes()
    {
        Assert.Throws<InvalidDataException>(
            () => TaggedIdReportPayloadCodec.Decode(GameMessageMetadata.Empty, []));
        Assert.Throws<InvalidDataException>(
            () => TaggedIdReportPayloadCodec.Decode(GameMessageMetadata.Empty, [0x1F, 0x00, 0x00, 0x00]));
        Assert.Throws<InvalidDataException>(
            () => TaggedIdReportPayloadCodec.Decode(GameMessageMetadata.Empty, [0x1F, 0x00, 0x00, 0x00, 0x01, 0x00]));
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
