using Dfo.Server.Abstractions.Chat;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Chat;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Chat;

/// <summary>The one place 1:1 conversations are kept, in memory, for as long as both windows are open.</summary>
internal sealed class ChatConversationDomainService : IChatConversationDomainService
{
    private readonly Lock sync = new();
    private readonly Dictionary<ChatConversationId, ChatConversation> byId = [];
    private readonly Dictionary<(CharacterId, CharacterId), ChatConversationId> byPair = [];
    private uint next = 1;

    public ChatConversation? Open(ChatConversationMember requester, ChatConversationMember target)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(target);
        if (requester.CharacterId == target.CharacterId)
            throw new ArgumentException("A conversation needs two characters.", nameof(target));
        GameplayWriteAssertion.Writing(nameof(ChatConversationDomainService) + "." + nameof(Open));
        lock (sync)
        {
            var pair = Pair(requester.CharacterId, target.CharacterId);
            if (byPair.TryGetValue(pair, out var existing))
            {
                // Still open between these very sessions: both already have its window.
                var current = byId[existing];
                if (current.MemberFor(requester.Session) is not null && current.MemberFor(target.Session) is not null)
                    return null;
                // One side has moved on to another session without the old one being cleaned up;
                // that window is gone, so the old conversation is too.
                Remove(current);
            }

            var conversation = new ChatConversation(Allocate(), requester, target);
            byId.Add(conversation.Id, conversation);
            byPair.Add(pair, conversation.Id);
            return conversation;
        }
    }

    public ChatConversation? Find(ChatConversationId id)
    {
        lock (sync)
            return byId.GetValueOrDefault(id);
    }

    public ChatConversationDeparture? Leave(ChatConversationId id, SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        GameplayWriteAssertion.Writing(nameof(ChatConversationDomainService) + "." + nameof(Leave));
        lock (sync)
        {
            if (!byId.TryGetValue(id, out var conversation) || conversation.MemberFor(session) is not { } leaver)
                return null;
            Remove(conversation);
            return new(conversation.Id, leaver, conversation.Other(leaver));
        }
    }

    public IReadOnlyList<ChatConversationDeparture> EndAllOf(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        GameplayWriteAssertion.Writing(nameof(ChatConversationDomainService) + "." + nameof(EndAllOf));
        lock (sync)
        {
            var departures = new List<ChatConversationDeparture>();
            foreach (var conversation in byId.Values.ToArray())
            {
                if (conversation.MemberFor(session) is not { } leaver)
                    continue;
                Remove(conversation);
                departures.Add(new(conversation.Id, leaver, conversation.Other(leaver)));
            }
            return departures;
        }
    }

    // 0 is never handed out, and 0xFFFFFFFF is the client's "no conversation" (-1) for a window.
    private ChatConversationId Allocate()
    {
        while (next is 0 or uint.MaxValue || byId.ContainsKey(new(next)))
            next++;
        return new(next++);
    }

    private void Remove(ChatConversation conversation)
    {
        byId.Remove(conversation.Id);
        byPair.Remove(Pair(conversation.First.CharacterId, conversation.Second.CharacterId));
    }

    private static (CharacterId, CharacterId) Pair(CharacterId a, CharacterId b) =>
        a.Value <= b.Value ? (a, b) : (b, a);
}
