using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Chat;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Chat;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Chat;

[Trait("Category", "Unit")]
public sealed class ChatConversationDomainServiceTests
{
    private readonly ChatConversationDomainService conversations = new();

    [Fact]
    public void Opening_gives_the_two_members_one_conversation_under_an_id_the_client_can_use()
    {
        var a = Member(1);
        var b = Member(2);

        var opened = Run(() => conversations.Open(a, b));

        Assert.NotNull(opened);
        Assert.NotEqual(0u, opened.Id.Value);
        Assert.NotEqual(uint.MaxValue, opened.Id.Value);
        Assert.Same(a, opened.First);
        Assert.Same(b, opened.Second);
        Assert.Same(opened, conversations.Find(opened.Id));
    }

    [Fact]
    public void Asking_again_while_the_conversation_goes_on_opens_nothing_from_either_side()
    {
        var a = Member(1);
        var b = Member(2);
        var opened = Run(() => conversations.Open(a, b))!;

        Assert.Null(Run(() => conversations.Open(a, b)));
        Assert.Null(Run(() => conversations.Open(b, a)));
        Assert.Same(opened, conversations.Find(opened.Id));
    }

    [Fact]
    public void A_conversation_left_behind_by_an_earlier_session_is_replaced()
    {
        var a = Member(1);
        var opened = Run(() => conversations.Open(a, Member(2)))!;
        var laterB = Member(2, generation: 2);

        var reopened = Run(() => conversations.Open(a, laterB));

        Assert.NotNull(reopened);
        Assert.NotEqual(opened.Id, reopened.Id);
        Assert.Null(conversations.Find(opened.Id));
    }

    [Fact]
    public void Each_pair_has_its_own_conversation_and_its_own_id()
    {
        var a = Member(1);
        var first = Run(() => conversations.Open(a, Member(2)))!;
        var second = Run(() => conversations.Open(Member(3), a))!;

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void A_member_leaving_ends_the_conversation_and_names_who_is_left()
    {
        var a = Member(1);
        var b = Member(2);
        var opened = Run(() => conversations.Open(a, b))!;

        Assert.Null(Run(() => conversations.Leave(opened.Id, Member(3).Session)));
        Assert.Null(Run(() => conversations.Leave(opened.Id, Member(1, generation: 2).Session)));
        var departure = Run(() => conversations.Leave(opened.Id, b.Session));

        Assert.Equal(new ChatConversationDeparture(opened.Id, b, a), departure);
        Assert.Null(conversations.Find(opened.Id));
        Assert.Null(Run(() => conversations.Leave(opened.Id, a.Session)));
        Assert.NotNull(Run(() => conversations.Open(a, b)));
    }

    [Fact]
    public void A_session_ending_ends_every_conversation_it_was_in()
    {
        var a = Member(1);
        var b = Member(2);
        var c = Member(3);
        var withB = Run(() => conversations.Open(a, b))!;
        var withC = Run(() => conversations.Open(c, a))!;
        var unrelated = Run(() => conversations.Open(b, c))!;

        var departures = Run(() => conversations.EndAllOf(a.Session));

        Assert.Equal(
            [new ChatConversationDeparture(withB.Id, a, b), new ChatConversationDeparture(withC.Id, a, c)],
            departures.OrderBy(departure => departure.Conversation.Value));
        Assert.Null(conversations.Find(withB.Id));
        Assert.Null(conversations.Find(withC.Id));
        Assert.Same(unrelated, conversations.Find(unrelated.Id));
        Assert.Empty(Run(() => conversations.EndAllOf(a.Session)));
    }

    [Fact]
    public void Changing_conversations_off_the_executor_is_refused()
    {
        Assert.Throws<GameplayOffExecutorException>(() => conversations.Open(Member(1), Member(2)));
        Assert.Throws<GameplayOffExecutorException>(() => conversations.EndAllOf(Member(1).Session));
    }

    [Fact]
    public void A_character_cannot_open_a_conversation_with_itself()
    {
        Assert.Throws<ArgumentException>(() => Run(() => conversations.Open(Member(1), Member(1))));
    }

    private static T Run<T>(Func<T> body) => TestGameplay.Run(body);

    private static ChatConversationMember Member(int character, long generation = 1) =>
        new(new SelectedSessionIdentity(new AccountId(character), new CharacterId(character),
            new NetworkConnectionId($"conversation-{character}-{generation}"), generation, (ushort)character, 10011),
            [(byte)('0' + character)]);
}
