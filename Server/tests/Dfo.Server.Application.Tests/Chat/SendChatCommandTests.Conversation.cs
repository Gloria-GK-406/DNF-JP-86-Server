using System.Text;
using Dfo.Server.Data.Chat;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.OneToOneChat;
using Dfo.Server.Protocol.Game.SendChat;

namespace Dfo.Server.Application.Tests.Chat;

/// <summary>The 1:1 conversation window: CMD 0x01A3, mode 43 lines, CMD 0x01A5 and 0x01A6.</summary>
public sealed partial class SendChatCommandTests
{
    [Fact]
    public async Task Opening_a_conversation_tells_both_members_each_with_the_other_named_first()
    {
        using var w = new ChatWorld();
        var b = w.Add(2, DuelChannel, OtherArea);
        var c = w.Add(3, FirstChannel, SpeakerArea);

        await w.SendRawAsync(w.Speaker, 0x01A3, Name("chat2"));

        var own = Assert.IsType<ConversationOpenedMessage>(Assert.Single(w.MessagesFor(w.Speaker)));
        var peer = Assert.IsType<ConversationOpenedMessage>(Assert.Single(w.MessagesFor(b)));
        Assert.Equal("chat2"u8.ToArray(), own.PeerName);
        Assert.Equal("shop1"u8.ToArray(), own.OwnName);
        Assert.Equal("shop1"u8.ToArray(), peer.PeerName);
        Assert.Equal("chat2"u8.ToArray(), peer.OwnName);
        Assert.Equal(own.Conversation, peer.Conversation);
        Assert.NotEqual(0u, own.Conversation.Value);
        Assert.NotEqual(uint.MaxValue, own.Conversation.Value);
        Assert.Empty(w.MessagesFor(c));
    }

    [Fact]
    public async Task Asking_again_from_either_side_while_the_conversation_is_open_sends_nothing()
    {
        using var w = new ChatWorld();
        var b = w.Add(2, FirstChannel, SpeakerArea);
        await w.SendRawAsync(w.Speaker, 0x01A3, Name("chat2"));

        await w.SendRawAsync(w.Speaker, 0x01A3, Name("chat2"));
        await w.SendRawAsync(b, 0x01A3, Name("shop1"));

        Assert.Single(w.MessagesFor(w.Speaker));
        Assert.Single(w.MessagesFor(b));
    }

    [Fact]
    public async Task Opening_with_no_one_by_that_name_someone_offline_or_oneself_sends_nothing()
    {
        using var w = new ChatWorld();
        var b = w.Add(2, FirstChannel, SpeakerArea);
        w.Release(b);

        await w.SendRawAsync(w.Speaker, 0x01A3, Name("nobody"));
        await w.SendRawAsync(w.Speaker, 0x01A3, Name("chat2"));
        await w.SendRawAsync(w.Speaker, 0x01A3, Name("shop1"));

        Assert.Empty(w.AllMessages);
    }

    [Fact]
    public async Task A_conversation_line_reaches_only_the_other_member_and_is_never_answered()
    {
        using var w = new ChatWorld();
        var b = w.Add(2, DuelChannel, OtherArea);
        var c = w.Add(3, FirstChannel, SpeakerArea);
        var id = await OpenAsync(w, w.Speaker, "chat2");

        await w.SendAsync(w.Speaker, Line(id, "hi there", "shop1"));
        await w.SendAsync(b, Line(id, "hello", "chat2"));

        var received = Assert.IsType<ConversationMessage>(w.MessagesFor(b)[^1]);
        Assert.Equal(id, received.Conversation);
        Assert.Equal("shop1"u8.ToArray(), received.SenderName);
        Assert.Equal("hi there"u8.ToArray(), received.Text);
        var answered = Assert.IsType<ConversationMessage>(w.MessagesFor(w.Speaker)[^1]);
        Assert.Equal("chat2"u8.ToArray(), answered.SenderName);
        Assert.Equal(2, w.MessagesFor(w.Speaker).Count);
        Assert.Equal(2, w.MessagesFor(b).Count);
        Assert.Empty(w.MessagesFor(c));
        Assert.DoesNotContain(w.AllMessages, message => message is SendChatRefusal);
    }

    [Fact]
    public async Task A_conversation_line_that_cannot_go_is_dropped_without_a_refusal()
    {
        using var w = new ChatWorld();
        var b = w.Add(2, FirstChannel, SpeakerArea);
        var c = w.Add(3, FirstChannel, SpeakerArea);
        var id = await OpenAsync(w, w.Speaker, "chat2");
        var before = w.AllMessages.Count;

        await w.SendAsync(w.Speaker, Line(new(id.Value + 1000), "wrong id", "shop1"));
        await w.SendAsync(c, Line(id, "not mine", "chat3"));
        await w.SendAsync(w.Speaker, Line(id, "", "shop1"));
        await w.SendAsync(w.Speaker, Line(id, new string('a', ConversationMessage.TextLimit + 1), "shop1"));
        Assert.Equal(before, w.AllMessages.Count);

        await w.SendAsync(w.Speaker, Line(id, new string('a', ConversationMessage.TextLimit), "shop1"));
        Assert.Equal(ConversationMessage.TextLimit, Assert.IsType<ConversationMessage>(w.MessagesFor(b)[^1]).Text.Length);

        w.Release(b);
        before = w.AllMessages.Count;
        await w.SendAsync(w.Speaker, Line(id, "anyone?", "shop1"));
        Assert.Equal(before, w.AllMessages.Count);
        Assert.DoesNotContain(w.AllMessages, message => message is SendChatRefusal);
    }

    [Fact]
    public async Task Closing_the_window_ends_the_conversation_and_tells_the_other_member()
    {
        using var w = new ChatWorld();
        var b = w.Add(2, FirstChannel, SpeakerArea);
        var c = w.Add(3, FirstChannel, SpeakerArea);
        var id = await OpenAsync(w, w.Speaker, "chat2");

        await w.SendRawAsync(c, 0x01A5, Id(id));
        Assert.Single(w.MessagesFor(b));

        await w.SendRawAsync(w.Speaker, 0x01A5, Id(id));
        var left = Assert.IsType<ConversationLeftMessage>(w.MessagesFor(b)[^1]);
        Assert.Equal(id, left.Conversation);
        Assert.Equal("shop1"u8.ToArray(), left.LeaverName);
        Assert.Single(w.MessagesFor(w.Speaker));

        var before = w.AllMessages.Count;
        await w.SendAsync(b, Line(id, "still there?", "chat2"));
        await w.SendRawAsync(b, 0x01A5, Id(id));
        Assert.Equal(before, w.AllMessages.Count);

        var reopened = await OpenAsync(w, w.Speaker, "chat2");
        Assert.NotEqual(id, reopened);
    }

    [Fact]
    public async Task The_window_state_report_is_taken_and_answered_with_nothing()
    {
        using var w = new ChatWorld();
        _ = w.Add(2, FirstChannel, SpeakerArea);
        _ = await OpenAsync(w, w.Speaker, "chat2");
        var before = w.AllMessages.Count;

        await w.SendRawAsync(w.Speaker, 0x01A6, [2]);

        Assert.Equal(before, w.AllMessages.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("00000000")]
    [InlineData("0300000061")]
    [InlineData("01000000616263")]
    [InlineData("FFFFFFFF61")]
    public async Task An_open_request_that_cannot_be_decoded_opens_nothing(string hex)
    {
        using var w = new ChatWorld();
        _ = w.Add(2, FirstChannel, SpeakerArea);

        await Assert.ThrowsAsync<InvalidDataException>(() => w.SendRawAsync(w.Speaker, 0x01A3, Convert.FromHexString(hex)));

        Assert.Empty(w.AllMessages);
    }

    [Theory]
    [InlineData("")]
    [InlineData("010000")]
    [InlineData("0100000000")]
    public async Task A_leave_request_that_is_not_one_id_is_not_decoded(string hex)
    {
        using var w = new ChatWorld();
        await Assert.ThrowsAsync<InvalidDataException>(() => w.SendRawAsync(w.Speaker, 0x01A5, Convert.FromHexString(hex)));
    }

    private static async Task<ChatConversationId> OpenAsync(ChatWorld w, SelectedSessionIdentity from, string target)
    {
        await w.SendRawAsync(from, 0x01A3, Name(target));
        return Assert.IsType<ConversationOpenedMessage>(w.MessagesFor(from)[^1]).Conversation;
    }

    private static byte[] Name(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        return [.. BitConverter.GetBytes(bytes.Length), .. bytes];
    }

    private static byte[] Id(ChatConversationId id) => BitConverter.GetBytes(id.Value);

    /// <summary>A mode 43 body as the window writes it: its own name and server 0 always trail the text.</summary>
    private static byte[] Line(ChatConversationId id, string text, string ownName) =>
        [.. Chat(43, text, aux: id.Value), .. Name(ownName), 0];
}
