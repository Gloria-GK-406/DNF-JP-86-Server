using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Chat;

/// <summary>The id the server gives a 1:1 conversation; the client keys its window by it.</summary>
public readonly record struct ChatConversationId(uint Value);

/// <summary>One side of a 1:1 conversation, as it was when the conversation began.</summary>
/// <param name="Session">
/// The selected session the member took part from. A later session of the same character -- after a
/// relog or a channel change -- has no window for this conversation, so it is not this member.
/// </param>
/// <param name="Name">The member's stored character name, read once when the conversation began.</param>
public sealed record ChatConversationMember(SelectedSessionIdentity Session, byte[] Name)
{
    public CharacterId CharacterId => Session.CharacterId;
}

/// <summary>A 1:1 conversation that is still going on.</summary>
public sealed record ChatConversation(ChatConversationId Id, ChatConversationMember First, ChatConversationMember Second)
{
    public ChatConversationMember? MemberFor(SelectedSessionIdentity session) =>
        First.Session == session ? First : Second.Session == session ? Second : null;

    public ChatConversationMember Other(ChatConversationMember member) =>
        ReferenceEquals(member, First) ? Second : First;
}

/// <summary>A member left a conversation, and the one left behind is to be told.</summary>
public sealed record ChatConversationDeparture(
    ChatConversationId Conversation,
    ChatConversationMember Leaver,
    ChatConversationMember Remaining);
