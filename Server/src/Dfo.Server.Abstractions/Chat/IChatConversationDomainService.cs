using Dfo.Server.Data.Chat;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Chat;

/// <summary>Owns the 1:1 conversations that are going on: who is in each, and under what id.</summary>
/// <remarks>
/// A conversation lives only as long as both windows do. It ends when either member closes the
/// window or the member's selected session ends, and is never stored: a conversation does not
/// outlast the sessions it was opened between.
/// </remarks>
public interface IChatConversationDomainService
{
    /// <summary>
    /// Opens a conversation between two sessions, or returns null when one is already going on
    /// between them -- the client opens a new window for every conversation it is told of.
    /// </summary>
    ChatConversation? Open(ChatConversationMember requester, ChatConversationMember target);

    /// <summary>The conversation going on under this id, if any.</summary>
    ChatConversation? Find(ChatConversationId id);

    /// <summary>Ends the conversation if the session is one of its members; null when it is not.</summary>
    ChatConversationDeparture? Leave(ChatConversationId id, SelectedSessionIdentity session);

    /// <summary>Ends every conversation the session is a member of.</summary>
    IReadOnlyList<ChatConversationDeparture> EndAllOf(SelectedSessionIdentity session);
}
