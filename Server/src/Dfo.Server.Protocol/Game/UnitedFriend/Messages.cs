using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.UnitedFriend;

/// <summary>Whom a request names: a server group and a character name.</summary>
public sealed record FriendTarget(byte Server, byte[] Name);

/// <summary>CMD <c>0x0122</c>: add the character of this name as a friend.</summary>
/// <remarks>
/// A21 builder VA <c>01A2D180</c> writes <c>u16 uid, u8 server, dstr name</c>. The uid is the
/// client's own guess from its character table (0xFFFF when it has none) and is not read.
/// <paramref name="Target"/> is null when the body is not exactly those fields with a name of 1 to
/// <see cref="FriendListMessage.NameLimit"/> bytes; the request is still answered.
/// </remarks>
public sealed record AddFriendRequest(GameMessageMetadata Metadata, FriendTarget? Target) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameAddFriend;
}

/// <summary>CMD <c>0x0123</c>: remove the friend of this name.</summary>
/// <remarks>A21 builder VA <c>01A2D390</c>, <c>u8 server, dstr name</c>; null as for <see cref="AddFriendRequest"/>.</remarks>
public sealed record DeleteFriendRequest(GameMessageMetadata Metadata, FriendTarget? Target) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameDeleteFriend;
}

/// <summary>What an add came to, as far as the client can be told.</summary>
public enum AddFriendResult
{
    /// <summary>Added, or already a friend: the client is not told the difference.</summary>
    Added,
    Full,

    /// <summary>The request named another server group; this server has only its own.</summary>
    OtherServerGroup,
    NoSuchCharacter,

    /// <summary>Oneself, a malformed request, a requester not in the world: the client shows nothing for any of them.</summary>
    Refused,
}

/// <summary>CMD <c>0x0122</c> reply.</summary>
/// <remarks>
/// A21 handler VA <c>01119880</c> reads nothing after a success and does nothing with it: the list
/// changes only through the NOTI <c>0x0111</c> that follows.
/// </remarks>
public sealed record AddFriendResponse(AddFriendResult Result) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameAddFriend;
}

/// <summary>CMD <c>0x0123</c> reply: removed, or the one refusal the client shows (0x15).</summary>
/// <remarks>A21 handler VA <c>01119950</c> reads nothing after a success either.</remarks>
public sealed record DeleteFriendResponse(bool Removed) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameDeleteFriend;
}

/// <summary>CMD <c>0x0323</c>: which channel the character of this name is on.</summary>
/// <remarks>
/// A21 builder in VA <c>025A75C0</c>, <c>u8 server, dstr name</c>, with no length limit of its own.
/// The client sends it the moment it opens the menu it pops up for a name (a friend's, a chat
/// sender's), not when the player picks "move to that channel"; the reply is only cached and read
/// at that pick. <paramref name="Target"/> is null as for <see cref="DeleteFriendRequest"/>; the
/// request is still answered, because the client has no timeout and an unanswered query leaves the
/// menu saying "wait or try again" for good.
/// </remarks>
public sealed record UserChannelRequest(GameMessageMetadata Metadata, FriendTarget? Target) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameRequestUserChannel;
}

/// <summary>What a channel query came to.</summary>
public enum UserChannelOutcome
{
    /// <summary>The character is in the world, on <see cref="UserChannelResponse.Channel"/>.</summary>
    Found,

    /// <summary>No live character has the name, or it is not in the world: the client says the target is not there.</summary>
    NotThere,

    /// <summary>A malformed request, another server group, a requester out of the world, a channel number above 255: the client says it cannot follow.</summary>
    Refused,
}

/// <summary>CMD <c>0x0323</c> reply.</summary>
/// <remarks>
/// A21 handler VA <c>02594080</c> reads, after a success, <c>u8, dstr name (max 50), u8 group, u8
/// channel</c>; only the last two are ever used, to find the channel in the client's own channel
/// catalogue by group and number. A refusal is <c>[0][reason]</c>: 100 says the target is not there,
/// any other reason that it cannot be followed.
/// </remarks>
/// <param name="Name">The target's name as stored, echoed for the record; empty unless found.</param>
public sealed record UserChannelResponse(UserChannelOutcome Outcome, byte[] Name, byte Channel) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameRequestUserChannel;

    public static UserChannelResponse Found(byte[] name, byte channel) => new(UserChannelOutcome.Found, name, channel);

    public static UserChannelResponse NotThere { get; } = new(UserChannelOutcome.NotThere, [], 0);

    public static UserChannelResponse Refused { get; } = new(UserChannelOutcome.Refused, [], 0);
}
