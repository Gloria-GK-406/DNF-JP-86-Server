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
    /// <summary>The client reads a name only when it is shorter than 128 bytes.</summary>
    public const int NameLimit = 127;

    public ServerMessageType MessageType => ServerMessageType.GameConversationOpened;
}

/// <summary>NOTI <c>0x016E</c>: the named member left (A21 VA <c>024EA5E0</c>, <c>u32 id, dstr name</c>).</summary>
/// <remarks>The recipient's window stays open and shows that the member has left.</remarks>
public sealed record ConversationLeftMessage(ChatConversationId Conversation, byte[] LeaverName) : IGameNotiMessage
{
    public const int NameLimit = ConversationOpenedMessage.NameLimit;

    public ServerMessageType MessageType => ServerMessageType.GameConversationLeft;
}

/// <summary>NOTI <c>0x016F</c>: a line said in a conversation window, as the other member receives it.</summary>
/// <remarks>
/// A21 handler VA <c>024EA700</c>: <c>u32 id, dstr sender, dstr text</c>, the sender shorter than 128
/// bytes and the text shorter than 1024. It finds the window by id and drops the line when there is
/// none, or when the sender is the recipient itself: the speaker's own window shows the line already.
/// </remarks>
public sealed record ConversationMessage(ChatConversationId Conversation, byte[] SenderName, byte[] Text) : IGameNotiMessage
{
    public const int NameLimit = ConversationOpenedMessage.NameLimit;
    public const int TextLimit = 1023;

    public ServerMessageType MessageType => ServerMessageType.GameConversationMessage;
}
