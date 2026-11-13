using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.OneToOneChat;
using Dfo.Server.Protocol.Game.SendChat;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Chat;

/// <summary>
/// A blacklist decides what its owner receives: a line is withheld from a recipient who blacklisted
/// its speaker, and a 1:1 conversation is not opened while either side blacklists the other.
/// </summary>
/// <remarks>
/// The relations are written through the real relation service in a committed use case, so that a
/// removal is seen by the next line exactly as one made through CMD 0x0077 would be.
/// </remarks>
public sealed partial class SendChatCommandTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(45)]
    [InlineData(46)]
    public async Task Area_line_skips_a_recipient_who_blacklisted_the_speaker_and_reaches_the_rest(byte mode)
    {
        using var w = new ChatWorld();
        var blocker = w.Add(2, FirstChannel, SpeakerArea);
        var other = w.Add(3, FirstChannel, SpeakerArea);
        await BlacklistAsync(w, blocker, w.Speaker);

        await w.SendAsync(w.Speaker, Chat(mode, "hello"));

        Assert.Empty(w.MessagesFor(blocker));
        Assert.IsType<ChatLineMessage>(Assert.Single(w.MessagesFor(other)));
        Assert.IsType<ChatLineMessage>(Assert.Single(w.MessagesFor(w.Speaker)));
        Assert.DoesNotContain(w.AllMessages, message => message is SendChatRefusal);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    public async Task Party_line_skips_a_member_who_blacklisted_the_speaker(byte mode)
    {
        using var w = new ChatWorld();
        var blocker = w.Add(2, FirstChannel, SpeakerArea);
        var other = w.Add(3, FirstChannel, SpeakerArea);
        w.FormParty(w.Speaker, blocker, other);
        await BlacklistAsync(w, blocker, w.Speaker);

        await w.SendAsync(w.Speaker, Chat(mode, "party"));

        Assert.Empty(w.MessagesFor(blocker));
        Assert.IsType<ChatLineMessage>(Assert.Single(w.MessagesFor(other)));
        Assert.IsType<ChatLineMessage>(Assert.Single(w.MessagesFor(w.Speaker)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public async Task Whisper_to_someone_who_blacklisted_the_speaker_is_silent_like_an_unreachable_target(byte mode)
    {
        using var w = new ChatWorld();
        var blocker = w.Add(2, FirstChannel, SpeakerArea);
        await BlacklistAsync(w, blocker, w.Speaker);

        await w.SendAsync(w.Speaker, Chat(mode, "psst", target: "chat2"));

        Assert.Empty(w.AllMessages);
    }

    [Fact]
    public async Task Channel_megaphone_is_still_used_and_reaches_everyone_but_who_blacklisted_the_speaker()
    {
        using var w = new ChatWorld();
        await w.Fixture.Seed(MegaphoneSlot, 37, 3);
        var blocker = w.Add(2, FirstChannel, OtherArea);
        var other = w.Add(3, FirstChannel, OtherArea);
        await BlacklistAsync(w, blocker, w.Speaker);

        await w.SendAsync(w.Speaker, Chat(13, "sale", aux: (uint)MegaphoneSlot));

        Assert.Equal(2, w.Fixture.Count(37));
        Assert.Empty(w.MessagesFor(blocker));
        Assert.IsType<MegaphoneMessage>(Assert.Single(w.MessagesFor(other)));
        var own = w.MessagesFor(w.Speaker);
        Assert.Equal(2, own.Count);
        Assert.IsType<MegaphoneMessage>(own[0]);
        Assert.Equal("InventoryDeltaMessage", own[1].GetType().Name);
    }

    [Theory]
    [InlineData(11, 36)]
    [InlineData(12, 898)]
    [InlineData(57, 10151255)]
    public async Task Server_megaphone_is_still_used_and_reaches_everyone_but_who_blacklisted_the_speaker(byte mode, int item)
    {
        using var w = new ChatWorld();
        await w.Fixture.Seed(MegaphoneSlot, item, 2);
        var blocker = w.Add(2, DuelChannel, OtherArea);
        var other = w.Add(3, DuelChannel, OtherArea);
        await BlacklistAsync(w, blocker, w.Speaker);

        await w.SendAsync(w.Speaker, Chat(mode, "everyone", aux: (uint)MegaphoneSlot));

        Assert.Equal(1, w.Fixture.Count(item));
        Assert.Empty(w.MessagesFor(blocker));
        Assert.Equal(mode, Assert.IsType<MegaphoneMessage>(Assert.Single(w.MessagesFor(other))).Mode);
        Assert.Equal(mode, Assert.IsType<MegaphoneMessage>(w.MessagesFor(w.Speaker)[0]).Mode);
    }

    [Fact]
    public async Task Megaphone_whose_only_other_listener_blacklisted_the_speaker_still_tells_the_speaker_its_slot()
    {
        using var w = new ChatWorld();
        await w.Fixture.Seed(MegaphoneSlot, 37, 3);
        var blocker = w.Add(2, FirstChannel, OtherArea);
        await BlacklistAsync(w, blocker, w.Speaker);

        await w.SendAsync(w.Speaker, Chat(13, "sale", aux: (uint)MegaphoneSlot));

        Assert.Equal(2, w.Fixture.Count(37));
        Assert.Empty(w.MessagesFor(blocker));
        Assert.IsType<MegaphoneMessage>(w.MessagesFor(w.Speaker)[0]);
    }

    [Fact]
    public async Task Conversation_line_is_dropped_for_a_member_who_blacklisted_the_speaker_without_a_refusal()
    {
        using var w = new ChatWorld();
        var blocker = w.Add(2, FirstChannel, SpeakerArea);
        var id = await OpenAsync(w, w.Speaker, "chat2");
        await BlacklistAsync(w, blocker, w.Speaker);
        var before = w.MessagesFor(blocker).Count;

        await w.SendAsync(w.Speaker, Line(id, "still there?", "shop1"));

        Assert.Equal(before, w.MessagesFor(blocker).Count);
        Assert.DoesNotContain(w.AllMessages, message => message is SendChatRefusal);

        // The blacklist is the blocker's alone: its own lines still reach the speaker.
        await w.SendAsync(blocker, Line(id, "go away", "chat2"));
        Assert.Equal("go away"u8.ToArray(), Assert.IsType<ConversationMessage>(w.MessagesFor(w.Speaker)[^1]).Text);
    }

    [Fact]
    public async Task A_speakers_own_blacklist_does_not_hold_back_its_lines()
    {
        using var w = new ChatWorld();
        await w.Fixture.Seed(MegaphoneSlot, 37, 3);
        var blocked = w.Add(2, FirstChannel, SpeakerArea);
        await BlacklistAsync(w, w.Speaker, blocked);

        await w.SendAsync(w.Speaker, Chat(3, "area"));
        await w.SendAsync(w.Speaker, Chat(1, "psst", target: "chat2"));
        await w.SendAsync(w.Speaker, Chat(13, "sale", aux: (uint)MegaphoneSlot));

        var received = w.MessagesFor(blocked);
        Assert.Equal(3, received.Count);
        Assert.IsType<ChatLineMessage>(received[0]);
        Assert.IsType<WhisperMessage>(received[1]);
        Assert.IsType<MegaphoneMessage>(received[2]);
    }

    [Fact]
    public async Task Lines_reach_the_recipient_again_once_it_takes_the_speaker_off_its_blacklist()
    {
        using var w = new ChatWorld();
        var blocker = w.Add(2, FirstChannel, SpeakerArea);
        await BlacklistAsync(w, blocker, w.Speaker);
        await w.SendAsync(w.Speaker, Chat(3, "before"));
        await w.SendAsync(w.Speaker, Chat(1, "before", target: "chat2"));
        Assert.Empty(w.MessagesFor(blocker));

        await UnblacklistAsync(w, blocker, w.Speaker);
        await w.SendAsync(w.Speaker, Chat(3, "after"));
        await w.SendAsync(w.Speaker, Chat(1, "after", target: "chat2"));

        var received = w.MessagesFor(blocker);
        Assert.Equal(2, received.Count);
        Assert.Equal("after"u8.ToArray(), Assert.IsType<ChatLineMessage>(received[0]).Text);
        Assert.Equal("after"u8.ToArray(), Assert.IsType<WhisperMessage>(received[1]).Text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_conversation_between_a_blacklist_owner_and_its_target_is_refused_with_77_to_the_requester_only(bool requesterOwnsIt)
    {
        using var w = new ChatWorld();
        var peer = w.Add(2, FirstChannel, SpeakerArea);
        if (requesterOwnsIt) await BlacklistAsync(w, w.Speaker, peer);
        else await BlacklistAsync(w, peer, w.Speaker);

        await w.SendRawAsync(w.Speaker, 0x01A3, Name("chat2"));

        var frame = Assert.Single(w.FramesFor(w.Speaker));
        Assert.Equal([0x00, 0x6A, 0x01], frame[..3]);
        Assert.Equal([77], frame[15..]);
        Assert.Empty(w.MessagesFor(peer));

        // Nothing was opened: once the entry is gone the same request opens a conversation on both sides.
        if (requesterOwnsIt) await UnblacklistAsync(w, w.Speaker, peer);
        else await UnblacklistAsync(w, peer, w.Speaker);
        await w.SendRawAsync(w.Speaker, 0x01A3, Name("chat2"));

        var opened = Assert.IsType<ConversationOpenedMessage>(w.MessagesFor(w.Speaker)[^1]);
        Assert.Equal(opened.Conversation, Assert.IsType<ConversationOpenedMessage>(Assert.Single(w.MessagesFor(peer))).Conversation);
    }

    private static Task BlacklistAsync(ChatWorld w, SelectedSessionIdentity owner, SelectedSessionIdentity target) =>
        RelationWriteAsync(w, async relations => Assert.Equal(CharacterRelationAddOutcome.Added,
            await relations.AddAsync(owner.CharacterId, target.CharacterId, CharacterRelationKind.Blacklist, default)));

    private static Task UnblacklistAsync(ChatWorld w, SelectedSessionIdentity owner, SelectedSessionIdentity target) =>
        RelationWriteAsync(w, async relations => Assert.Equal(CharacterRelationRemoveOutcome.Removed,
            await relations.RemoveAsync(owner.CharacterId, target.CharacterId, CharacterRelationKind.Blacklist, default)));

    private static Task RelationWriteAsync(ChatWorld w, Func<ICharacterRelationDomainService, Task> body) =>
        TestGameplay.RunAsync(w.Fixture.Services, async () =>
        {
            await using var transaction = w.Fixture.Services.GetRequiredService<DomainTransactionScope>().Begin();
            await body(w.Fixture.Services.GetRequiredService<ICharacterRelationDomainService>());
            await transaction.CommitAsync(default);
            return true;
        });
}
