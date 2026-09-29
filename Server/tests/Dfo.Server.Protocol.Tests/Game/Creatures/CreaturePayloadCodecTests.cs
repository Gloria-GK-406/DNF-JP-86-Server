using Dfo.Server.Protocol.Game.Creatures;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Tests.Game.Creatures;

public sealed class CreaturePayloadCodecTests
{
    [Fact]
    public void Experience_mode_controls_the_two_extra_wire_bytes()
    {
        Assert.Equal(new byte[] { 20, 0, 0x78, 0x56, 0x34, 0x12 },
            CreaturePayloadCodec.EncodeExperience(new(20, 0, 0x12345678)));
        Assert.Equal(new byte[] { 20, 1, 0x78, 0x56, 0x34, 0x12, 3, 4 },
            CreaturePayloadCodec.EncodeExperience(new(20, 1, 0x12345678, 3, 4)));
    }

    [Fact]
    public void State_and_identity_notifications_have_exact_a21_widths()
    {
        Assert.Equal(new byte[] { 0x78, 0x56, 0x34, 0x12, 0x2C, 1, 0, 0 },
            CreaturePayloadCodec.EncodeState(new(0x12345678, 300)));
        Assert.Equal(new byte[] { 0x34, 0x12 }, CreaturePayloadCodec.EncodeDeath(0x1234));
        Assert.Equal(new byte[] { 0x34, 0x12 }, CreaturePayloadCodec.EncodeResponse(0x1234));
        Assert.Equal(new byte[] { 0x34, 0x12 }, CreaturePayloadCodec.EncodeRevival(0x1234));
        Assert.Equal(new byte[] { 0x34, 0x12, 0x78, 0x56 },
            CreaturePayloadCodec.EncodeEvolution(0x1234, 0x5678));
    }

    [Fact]
    public void Rename_notice_writes_u16_then_length_prefixed_raw_bytes()
    {
        Assert.Equal(new byte[] { 0x34, 0x12, 3, 0, 0, 0, 0x82, 0xA0, 0x41 },
            CreaturePayloadCodec.EncodeRenameNotification(0x1234, [0x82, 0xA0, 0x41]));
    }

    [Fact]
    public void Rename_request_and_result_follow_distinct_a21_command_bodies()
    {
        var request = CreaturePayloadCodec.DecodeRenameRequest(
            new byte[] { 0xFE, 0xFF, 7, 3, 0, 0, 0, 0x82, 0xA0, 0x41 });
        Assert.Equal((short)-2, request.Slot);
        Assert.Equal((byte)7, request.List);
        Assert.Equal(new byte[] { 0x82, 0xA0, 0x41 }, request.NameBytes);
        Assert.Equal(new byte[] { 1, 0xFE, 0xFF, 7 },
            CreaturePayloadCodec.EncodeRenameSuccess(-2, 7));
        Assert.Equal(new byte[] { 0, 17 }, CreaturePayloadCodec.EncodeRenameFailure(17));
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 7, 2, 0, 0, 0, 0x41 })]
    [InlineData(new byte[] { 0, 0, 7, 0, 0, 0, 0, 0x41 })]
    [InlineData(new byte[] { 0, 0, 7, 0xFF, 0xFF, 0xFF, 0xFF })]
    [InlineData(new byte[] { 0, 0, 6, 0, 0, 0, 0 })]
    public void Rename_request_rejects_truncated_excess_or_wrong_list(byte[] body)
        => Assert.Throws<InvalidDataException>(() => CreaturePayloadCodec.DecodeRenameRequest(body));

    [Fact]
    public void Response_report_is_exactly_one_u16_and_has_no_ack_shape()
    {
        var decoder = new CreatureResponseReportDecoder();
        var report = Assert.IsType<CreatureResponseReport>(
            decoder.Decode(GameMessageMetadata.Empty, [0x34, 0x12]));
        Assert.Equal((ushort)0x1234, report.ActorKey);
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, [0x34]));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, [0x34, 0x12, 0]));
    }

    [Fact]
    public void Script_report_requires_the_complete_length_prefixed_message_without_a_tail()
    {
        var decoder = new CreatureScriptMessageReportDecoder();
        var report = Assert.IsType<CreatureScriptMessageReport>(decoder.Decode(GameMessageMetadata.Empty,
            [3, 0, 0, 0x78, 0x56, 0x34, 0x12, 2, 0, 0, 0, 0x82, 0xA0]));
        Assert.Equal((byte)3, report.Mode);
        Assert.Equal((ushort)0, report.TargetKey);
        Assert.Equal(0x12345678u, report.CharacterId);
        Assert.Equal(new byte[] { 0x82, 0xA0 }, report.MessageBytes);
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty,
            [3, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0x82]));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty,
            [3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1]));
    }
}
