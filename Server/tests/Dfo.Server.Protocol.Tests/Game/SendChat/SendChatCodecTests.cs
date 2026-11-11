using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.SendChat;

namespace Dfo.Server.Protocol.Tests.Game.SendChat;

[Trait("Category", "Unit")]
public sealed class SendChatCodecTests
{
    private static readonly GameMessageMetadata Metadata = new(1, 0x0011, 0, 1);

    [Fact]
    public void Area_request_reads_the_fixed_fields_and_text_and_no_target()
    {
        var request = Decode("03" + "3412" + "41000000" + "02000000" + "6869");

        Assert.Equal((byte)3, request.Mode);
        Assert.Equal((ushort)0x1234, request.TargetUserId);
        Assert.Equal(0x41u, request.Aux);
        Assert.Equal("hi"u8.ToArray(), request.Text);
        Assert.Null(request.TargetName);
        Assert.Null(request.TargetServerGroup);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(36)]
    [InlineData(37)]
    [InlineData(38)]
    public void Target_carrying_modes_also_read_the_target_name_and_server_group(byte mode)
    {
        var request = Decode($"{mode:X2}" + "0000" + "00000000" + "01000000" + "61" + "02000000" + "6263" + "05");

        Assert.Equal("a"u8.ToArray(), request.Text);
        Assert.Equal("bc"u8.ToArray(), request.TargetName);
        Assert.Equal((byte)5, request.TargetServerGroup);
    }

    [Fact]
    public void Text_up_to_the_decode_limit_is_read_so_the_command_can_refuse_it()
    {
        var text = new byte[SendChatDecoder.DecodedTextLimit];
        var body = Convert.FromHexString("03" + "0000" + "00000000" + "00040000").Concat(text).ToArray();

        Assert.Equal(1024, ((SendChatRequest)new SendChatDecoder().Decode(Metadata, body)).Text.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("030000000000")]
    [InlineData("0300000000000000")]
    [InlineData("03000000000000FFFFFFFF")]
    [InlineData("03000000000000010400006161")]
    [InlineData("0300000000000005000000616263")]
    [InlineData("01000000000000010000006141000000")]
    [InlineData("0100000000000001000000610100000062")]
    [InlineData("01000000000000010000006102000000")]
    public void Truncated_or_out_of_range_frames_are_refused_by_the_decoder(string hex) =>
        Assert.Throws<InvalidDataException>(() => new SendChatDecoder().Decode(Metadata, Convert.FromHexString(hex)));

    [Fact]
    public void Item_link_request_is_decoded_without_reading_its_body() =>
        Assert.IsType<ItemLinkChatRequest>(new ItemLinkChatDecoder().Decode(Metadata, Convert.FromHexString("FFFFFFFF")));

    [Fact]
    public void Chat_line_is_mode_sender_zero_group_and_text()
    {
        Assert.Equal(Convert.FromHexString("03" + "3412" + "00" + "02000000" + "6869"),
            ChatLineEncoder.EncodeBody(new ChatLineMessage(3, 0x1234, "hi"u8.ToArray())));
    }

    [Fact]
    public void Whisper_is_mode_zero_status_name_zero_server_and_text()
    {
        Assert.Equal(Convert.FromHexString("01" + "00" + "02000000" + "6162" + "00" + "02000000" + "6869"),
            WhisperEncoder.EncodeBody(new WhisperMessage(1, "ab"u8.ToArray(), "hi"u8.ToArray())));
    }

    [Fact]
    public void Megaphone_is_mode_server_zero_group_channel_uid_name_and_text()
    {
        Assert.Equal(Convert.FromHexString("0D" + "01" + "00" + "0B" + "3412" + "02000000" + "6162" + "02000000" + "6869"),
            MegaphoneEncoder.EncodeBody(new MegaphoneMessage(13, 1, 11, 0x1234, "ab"u8.ToArray(), "hi"u8.ToArray())));
    }

    [Fact]
    public void Each_frame_at_its_limits_encodes_to_its_stated_maximum()
    {
        Assert.Equal(263, ChatLineEncoder.MaximumBodyLength);
        Assert.Equal(296, WhisperEncoder.MaximumBodyLength);
        Assert.Equal(554, MegaphoneEncoder.MaximumBodyLength);
        Assert.Equal(ChatLineEncoder.MaximumBodyLength,
            ChatLineEncoder.EncodeBody(new ChatLineMessage(3, 1, new byte[255])).Length);
        Assert.Equal(WhisperEncoder.MaximumBodyLength,
            WhisperEncoder.EncodeBody(new WhisperMessage(1, new byte[30], new byte[255])).Length);
        Assert.Equal(MegaphoneEncoder.MaximumBodyLength,
            MegaphoneEncoder.EncodeBody(new MegaphoneMessage(11, 1, 1, 1, new byte[29], new byte[511])).Length);
    }

    [Fact]
    public void A_field_past_the_clients_limit_or_empty_is_never_encoded()
    {
        Assert.Throws<InvalidDataException>(() => ChatLineEncoder.EncodeBody(new ChatLineMessage(3, 1, new byte[256])));
        Assert.Throws<InvalidDataException>(() => ChatLineEncoder.EncodeBody(new ChatLineMessage(3, 1, [])));
        Assert.Throws<InvalidDataException>(() => WhisperEncoder.EncodeBody(new WhisperMessage(1, new byte[31], [1])));
        Assert.Throws<InvalidDataException>(() => WhisperEncoder.EncodeBody(new WhisperMessage(1, [1], new byte[256])));
        Assert.Throws<InvalidDataException>(() => MegaphoneEncoder.EncodeBody(new MegaphoneMessage(11, 1, 1, 1, new byte[30], [1])));
        Assert.Throws<InvalidDataException>(() => MegaphoneEncoder.EncodeBody(new MegaphoneMessage(11, 1, 1, 1, [1], new byte[512])));
    }

    [Theory]
    [InlineData(3, ChatKind.Area, null)]
    [InlineData(45, ChatKind.Area, null)]
    [InlineData(46, ChatKind.Area, null)]
    [InlineData(2, ChatKind.Party, null)]
    [InlineData(9, ChatKind.Party, null)]
    [InlineData(1, ChatKind.Whisper, null)]
    [InlineData(7, ChatKind.Whisper, null)]
    [InlineData(6, ChatKind.Guild, null)]
    [InlineData(13, ChatKind.ChannelMegaphone, 37)]
    [InlineData(11, ChatKind.ServerMegaphone, 36)]
    [InlineData(12, ChatKind.ServerMegaphone, 898)]
    [InlineData(57, ChatKind.ServerMegaphone, 10151255)]
    internal void First_batch_modes_map_to_their_kind_and_megaphone_item(byte mode, ChatKind kind, int? item)
    {
        var route = ChatWireModes.Route(mode);
        Assert.NotNull(route);
        Assert.Equal(kind, route.Kind);
        Assert.Equal(item, route.MegaphoneItemId);
    }

    [Fact]
    public void Every_other_mode_is_unmapped()
    {
        byte[] supported = [1, 2, 3, 6, 7, 9, 11, 12, 13, 45, 46, 57];
        for (var mode = 0; mode <= byte.MaxValue; mode++)
            Assert.Equal(supported.Contains((byte)mode), ChatWireModes.Route((byte)mode) is not null);
    }

    [Fact]
    public async Task The_speaker_alone_gets_the_follow_up_and_only_after_the_line()
    {
        var line = new ChatLineMessage(3, 1, [1]);
        var followUp = new ChatLineMessage(3, 1, [2]);
        var posted = new ChatMessagePosted(line, new CharacterId(1), [followUp]);
        var handler = new ChatMessagePostedHandler();

        Assert.Equal([line, followUp], await handler.ComposeAsync(new NotificationRecipient(new(1), new("a")), posted, default));
        Assert.Equal([line], await handler.ComposeAsync(new NotificationRecipient(new(2), new("b")), posted, default));
    }

    private static SendChatRequest Decode(string hex) =>
        Assert.IsType<SendChatRequest>(new SendChatDecoder().Decode(Metadata, Convert.FromHexString(hex)));
}
