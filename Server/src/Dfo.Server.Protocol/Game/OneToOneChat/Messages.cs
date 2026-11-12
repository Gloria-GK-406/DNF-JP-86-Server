using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Chat;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.OneToOneChat;

/// <summary>CMD <c>0x01A3</c>: open a 1:1 conversation window with the character of this name.</summary>
/// <remarks>
/// A21 builder VA <c>024E7EB0</c> writes one <c>dstr</c>, the target's name. The client opens no window
/// of its own and waits for nothing: the window appears only when NOTI <c>0x016A</c> arrives.
/// </remarks>
public sealed record OpenConversationRequest(GameMessageMetadata Metadata, byte[] TargetName) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameOpenConversation;
}

/// <summary>CMD <c>0x01A5</c>: the window of this conversation was closed (A21 VA <c>024EEE70</c>, <c>u32 id</c>).</summary>
public sealed record LeaveConversationRequest(GameMessageMetadata Metadata, ChatConversationId Conversation) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameLeaveConversation;
}

/// <summary>CMD <c>0x01A6</c>: the window's member-state report. The client has no handler for an answer.</summary>
public sealed record ConversationUserStateReport(GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameConversationUserState;
}

/// <summary>NOTI <c>0x016A</c>: a conversation was opened; the recipient's client opens its window.</summary>
/// <remarks>
/// A21 handler VA <c>024EAFA0</c>: <c>u8 result</c>, then for any result but 77, 100 and 101 <c>u32 id,
/// u8 n, n x dstr name</c>. Every such frame opens a new window -- the client does not look for one
/// with the same id -- and the first name becomes the window's other party and title, so each
/// recipient is sent the other member first and itself second.
/// </remarks>
public sealed record ConversationOpenedMessage(ChatConversationId Conversation, byte[] PeerName, byte[] OwnName) : IGameNotiMessage
{
    public const int NameLimit = ConversationMessage.NameLimit;

    public ServerMessageType MessageType => ServerMessageType.GameConversationOpened;
}

/// <summary>NOTI <c>0x016A</c> with the blacklist result: no conversation, and the requester's client shows text 4763.</summary>
/// <remarks>
/// Sent only to the requester's own connection. The client reads the result byte alone for 77 and
/// opens no window, so the other side, which never asked, is told nothing.
/// </remarks>
public sealed record ConversationRefusedMessage : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameConversationOpened;
}
