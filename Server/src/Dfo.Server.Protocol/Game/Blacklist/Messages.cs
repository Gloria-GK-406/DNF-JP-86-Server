using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Blacklist;

/// <summary>CMD <c>0x0076</c>: blacklist the character of this name.</summary>
/// <remarks>
/// A21 builder VA <c>01A2D530</c> writes one <c>dstr</c>. <paramref name="Name"/> is null when the
/// body is not exactly one name of 1 to <see cref="BlacklistPayloadCodec.RequestNameLimit"/> bytes;
/// the request is still answered, because the client is waiting on a reply to it.
/// </remarks>
public sealed record RegisterBlacklistRequest(GameMessageMetadata Metadata, byte[]? Name) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameRegisterBlacklist;
}

/// <summary>CMD <c>0x0077</c>: lift the blacklisting of the character of this name.</summary>
/// <remarks>A21 builder VA <c>01A2D6B0</c>, one <c>dstr</c>; null as for <see cref="RegisterBlacklistRequest"/>.</remarks>
public sealed record DeleteBlacklistRequest(GameMessageMetadata Metadata, byte[]? Name) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameDeleteBlacklist;
}

/// <summary>CMD <c>0x0078</c>: send the whole blacklist. An empty body, asked for on the way back into town.</summary>
public sealed record BlacklistListRequest(GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameRequestBlacklist;
}

/// <summary>What a blacklisting came to, as far as the client can be told.</summary>
public enum RegisterBlacklistResult
{
    Registered,
    Duplicate,
    NoSuchCharacter,
    Full,

    /// <summary>Oneself, a malformed request, a requester not in the world: the client shows nothing for any of them.</summary>
    Refused,
}

/// <summary>CMD <c>0x0076</c> reply.</summary>
/// <remarks>
/// A21 handler VA <c>0111F630</c>. On success it reads the name from this reply, not the one it asked
/// with, into a <c>0x100</c> buffer (1 to 255 bytes) and adds that name to its list.
/// <paramref name="Name"/> is the target's current name and is sent only for
/// <see cref="RegisterBlacklistResult.Registered"/>.
/// </remarks>
public sealed record RegisterBlacklistResponse(RegisterBlacklistResult Result, byte[] Name) : IGameCmdMessage
{
    public const int NameLimit = 255;

    public ServerMessageType MessageType => ServerMessageType.GameRegisterBlacklist;
}

/// <summary>CMD <c>0x0077</c> reply: the lifted name, or null for a refusal.</summary>
/// <remarks>
/// A21 handler VA <c>011243E0</c> reads the name into a <c>0x1F</c> buffer, so a success carries 1 to
/// 30 bytes, and clears that name's blacklist mark.
/// </remarks>
public sealed record DeleteBlacklistResponse(byte[]? Name) : IGameCmdMessage
{
    public const int NameLimit = BlacklistListResponse.NameLimit;

    public ServerMessageType MessageType => ServerMessageType.GameDeleteBlacklist;
}
