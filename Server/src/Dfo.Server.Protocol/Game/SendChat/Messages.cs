using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.SendChat;

/// <summary>CMD <c>0x0011</c>: one chat line, whatever channel the player picked.</summary>
/// <remarks>
/// A21 layout (client static analysis): <c>u8 mode, u16 targetUid, u32 aux, dstr text</c>, and for
/// the modes in <see cref="ChatWireModes.CarriesTarget"/> also <c>dstr targetName, u8 serverGroup</c>.
/// <c>aux</c> is the Main-container slot of the megaphone being used. A dstr is an <c>i32</c> byte
/// count and that many UTF-8 bytes; the bytes are passed on untouched.
/// </remarks>
public sealed record SendChatRequest(
    GameMessageMetadata Metadata,
    byte Mode,
    ushort TargetUserId,
    uint Aux,
    byte[] Text,
    byte[]? TargetName,
    byte? TargetServerGroup) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSendChat;
}

/// <summary>CMD <c>0x01A9</c>: a chat line carrying an item link. Not supported; its body is not read.</summary>
public sealed record ItemLinkChatRequest(GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameItemLinkChat;
}

/// <summary>
/// The answer to a refused chat line. A21 handler VA <c>0111B480</c> reads <c>u8 ok, u8 reason</c>;
/// a line that went through is never answered, because the client does not wait for one.
/// </summary>
public sealed record SendChatRefusal(byte Reason = ChatRefusal.Reason) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSendChatRefusal;
}

/// <summary>The same refusal under <c>0x01A9</c>, which shares the <c>0x0011</c> handler and layout.</summary>
public sealed record ItemLinkChatRefusal(byte Reason = ChatRefusal.Reason) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameItemLinkChatRefusal;
}

internal static class ChatRefusal
{
    /// <summary>The chat failure code the 70 server answers with (70Reference); what A21 shows for it is unverified.</summary>
    public const byte Reason = 0x11;
}

/// <summary>NOTI <c>0x000C</c>: an area, party or guild line.</summary>
/// <remarks>
/// A21 handler VA <c>01189860</c>: <c>u8 mode, u16 senderUid, u8 serverGroup, dstr text</c>; it drops a
/// mode of <c>0x3B</c> or more, a server group of <c>0xFA</c> or more, and text that is empty or 256
/// bytes or longer.
/// </remarks>
public sealed record ChatLineMessage(byte Mode, ushort SenderUserId, byte[] Text) : IGameNotiMessage
{
    public const int TextLimit = 255;

    public ServerMessageType MessageType => ServerMessageType.GameChatMessage;
}

/// <summary>NOTI <c>0x0041</c>: a whisper, as its recipient receives it.</summary>
/// <remarks>
/// A21 handler VA <c>01173D70</c>: <c>u8 mode, u8 status, dstr name, u8 server, dstr text</c>, with the
/// name at most 30 bytes and the text shorter than 256.
/// </remarks>
public sealed record WhisperMessage(byte Mode, byte[] SenderName, byte[] Text) : IGameNotiMessage
{
    public const int NameLimit = 30;
    public const int TextLimit = 255;

    public ServerMessageType MessageType => ServerMessageType.GameWhisperMessage;
}

/// <summary>NOTI <c>0x0076</c>: a channel or server megaphone line.</summary>
/// <remarks>
/// A21 handler VA <c>01166B60</c>: <c>u8 mode, u8 serverId, u8 serverGroup, u8 channel, u16 uid,
/// dstr name, dstr text</c>, with the name at most 29 bytes and the text at most 511. The channel is
/// compared with the channel number NOTI <c>0x0001</c> gave the recipient.
/// </remarks>
public sealed record MegaphoneMessage(byte Mode, byte ServerId, byte ChannelId, ushort SenderUserId,
    byte[] SenderName, byte[] Text) : IGameNotiMessage
{
    public const int NameLimit = 29;
    public const int TextLimit = 511;

    public ServerMessageType MessageType => ServerMessageType.GameMegaphoneMessage;
}
