using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Chat;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.OneToOneChat;

namespace Dfo.Server.Protocol.Tests.Game.OneToOneChat;

[Trait("Category", "Unit")]
public sealed class ConversationCodecTests
{
    private static readonly GameMessageMetadata Metadata = new(1, 0x01A3, 0, 1);

    [Fact]
    public void Open_request_reads_one_counted_name_that_fills_the_body()
    {
        var request = Assert.IsType<OpenConversationRequest>(new OpenConversationDecoder().Decode(Metadata, Convert.FromHexString("03000000303030")));

        Assert.Equal("000"u8.ToArray(), request.TargetName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("000000")]
    [InlineData("00000000")]
    [InlineData("0200000061")]
    [InlineData("02000000616263")]
    [InlineData("FFFFFFFF")]
    public void Open_request_whose_name_does_not_fill_its_body_is_not_read(string hex)
    {
        Assert.Throws<InvalidDataException>(() => new OpenConversationDecoder().Decode(Metadata, Convert.FromHexString(hex)));
    }

    [Fact]
    public void Open_request_name_longer_than_any_character_name_is_not_read()
    {
        var name = new byte[OpenConversationDecoder.DecodedNameLimit + 1];
        Assert.Throws<InvalidDataException>(() => new OpenConversationDecoder().Decode(Metadata,
            [.. BitConverter.GetBytes(name.Length), .. name]));
    }

    [Fact]
    public void Leave_request_is_exactly_one_conversation_id()
    {
        var request = Assert.IsType<LeaveConversationRequest>(new LeaveConversationDecoder().Decode(Metadata, Convert.FromHexString("78563412")));

        Assert.Equal(new ChatConversationId(0x12345678), request.Conversation);
        Assert.Throws<InvalidDataException>(() => new LeaveConversationDecoder().Decode(Metadata, Convert.FromHexString("785634")));
        Assert.Throws<InvalidDataException>(() => new LeaveConversationDecoder().Decode(Metadata, Convert.FromHexString("7856341200")));
    }

    [Fact]
    public void State_report_is_taken_whatever_it_carries()
    {
        Assert.IsType<ConversationUserStateReport>(new ConversationUserStateDecoder().Decode(Metadata, [2]));
        Assert.IsType<ConversationUserStateReport>(new ConversationUserStateDecoder().Decode(Metadata, []));
    }

    [Fact]
    public void Opened_frame_is_success_id_two_names_other_first()
    {
        var body = ConversationOpenedEncoder.EncodeBody(new(new(7), "bb"u8.ToArray(), "a"u8.ToArray()));

        Assert.Equal("00" + "07000000" + "02" + "02000000" + "6262" + "01000000" + "61", Convert.ToHexString(body));
    }

    [Fact]
    public void Left_frame_is_id_and_the_leavers_name()
    {
        var body = ConversationLeftEncoder.EncodeBody(new(new(0x0102), "ab"u8.ToArray()));

        Assert.Equal("02010000" + "02000000" + "6162", Convert.ToHexString(body));
    }

    [Fact]
    public void Message_frame_is_id_sender_and_text()
    {
        var body = ConversationMessageEncoder.EncodeBody(new(new(9), "a"u8.ToArray(), "hi"u8.ToArray()));

        Assert.Equal("09000000" + "01000000" + "61" + "02000000" + "6869", Convert.ToHexString(body));
    }

    [Fact]
    public void Every_frame_stays_inside_what_the_client_reads_and_its_stated_maximum()
    {
        var name = new byte[ConversationOpenedMessage.NameLimit];
        var text = new byte[ConversationMessage.TextLimit];

        Assert.Equal(ConversationOpenedEncoder.MaximumBodyLength, ConversationOpenedEncoder.EncodeBody(new(new(1), name, name)).Length);
        Assert.Equal(ConversationLeftEncoder.MaximumBodyLength, ConversationLeftEncoder.EncodeBody(new(new(1), name)).Length);
        Assert.Equal(ConversationMessageEncoder.MaximumBodyLength, ConversationMessageEncoder.EncodeBody(new(new(1), name, text)).Length);

        var longName = new byte[ConversationOpenedMessage.NameLimit + 1];
        Assert.Throws<InvalidDataException>(() => ConversationOpenedEncoder.EncodeBody(new(new(1), longName, "a"u8.ToArray())));
        Assert.Throws<InvalidDataException>(() => ConversationLeftEncoder.EncodeBody(new(new(1), longName)));
        Assert.Throws<InvalidDataException>(() => ConversationMessageEncoder.EncodeBody(new(new(1), "a"u8.ToArray(), new byte[ConversationMessage.TextLimit + 1])));
        Assert.Throws<InvalidDataException>(() => ConversationMessageEncoder.EncodeBody(new(new(1), "a"u8.ToArray(), [])));
    }

    [Fact]
    public async Task Each_member_is_told_of_the_opening_with_the_other_member_first_and_no_one_else_is_told()
    {
        var a = Member(1, "a");
        var b = Member(2, "bb");
        var conversation = new ChatConversation(new(5), a, b);
        var handler = new ChatConversationOpenedHandler();

        var toA = Assert.IsType<ConversationOpenedMessage>(Assert.Single(await handler.ComposeAsync(Recipient(1), new(conversation), default)));
        var toB = Assert.IsType<ConversationOpenedMessage>(Assert.Single(await handler.ComposeAsync(Recipient(2), new(conversation), default)));

        Assert.Equal(("bb", "a"), (Text(toA.PeerName), Text(toA.OwnName)));
        Assert.Equal(("a", "bb"), (Text(toB.PeerName), Text(toB.OwnName)));
        Assert.Empty(await handler.ComposeAsync(Recipient(3), new(conversation), default));
    }

    [Fact]
    public async Task Only_the_member_left_behind_is_told_who_left()
    {
        var departure = new ChatConversationDeparture(new(5), Member(1, "a"), Member(2, "bb"));
        var handler = new ChatConversationLeftHandler();

        var left = Assert.IsType<ConversationLeftMessage>(Assert.Single(await handler.ComposeAsync(Recipient(2), new(departure), default)));

        Assert.Equal(new ChatConversationId(5), left.Conversation);
        Assert.Equal("a", Text(left.LeaverName));
        Assert.Empty(await handler.ComposeAsync(Recipient(1), new(departure), default));
    }

    private static string Text(byte[] bytes) => System.Text.Encoding.UTF8.GetString(bytes);

    private static NotificationRecipient Recipient(int character) => new(new CharacterId(character), new($"c{character}"));

    private static ChatConversationMember Member(int character, string name) =>
        new(new SelectedSessionIdentity(new AccountId(character), new CharacterId(character), new($"c{character}"), 1, (ushort)character, 10011),
            System.Text.Encoding.UTF8.GetBytes(name));
}
