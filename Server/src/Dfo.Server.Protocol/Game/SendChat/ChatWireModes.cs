namespace Dfo.Server.Protocol.Game.SendChat;

/// <summary>The kinds of chat line this server delivers.</summary>
internal enum ChatKind
{
    Area,
    Party,
    Whisper,
    Guild,
    ChannelMegaphone,
    ServerMegaphone,
    Conversation,
}

/// <summary>What a supported wire mode means.</summary>
/// <param name="MegaphoneItemId">The item a megaphone line uses up, or null for a line that costs nothing.</param>
internal sealed record ChatRoute(ChatKind Kind, int? MegaphoneItemId = null)
{
    /// <summary>The longest text the frame this kind is delivered in may carry.</summary>
    public int TextLimit => Kind switch
    {
        ChatKind.Whisper => WhisperMessage.TextLimit,
        ChatKind.ChannelMegaphone or ChatKind.ServerMegaphone => MegaphoneMessage.TextLimit,
        ChatKind.Conversation => ConversationMessage.TextLimit,
        _ => ChatLineMessage.TextLimit,
    };

    /// <summary>The longest speaker name that frame may carry, or null when it carries none.</summary>
    public int? NameLimit => Kind switch
    {
        ChatKind.Whisper => WhisperMessage.NameLimit,
        ChatKind.ChannelMegaphone or ChatKind.ServerMegaphone => MegaphoneMessage.NameLimit,
        ChatKind.Conversation => ConversationMessage.NameLimit,
        _ => null,
    };
}

/// <summary>The A21 client's chat modes, which exist only on the wire.</summary>
internal static class ChatWireModes
{
    /// <summary>
    /// The first batch of supported modes. Everything else -- the server-only modes, the ones not
    /// supported yet, and any unmapped value -- is refused.
    /// </summary>
    /// <remarks>
    /// The megaphone items are written into the client's send function (item table at VA
    /// <c>01A6426C</c>): mode 11 uses 36, 12 uses 898, 57 uses 10151255 and 13 uses 37. Mode 43 is a
    /// line in a 1:1 conversation window (sender VA <c>024EF150</c>), whose <c>aux</c> is the conversation id.
    /// </remarks>
    public static ChatRoute? Route(byte mode) => mode switch
    {
        3 or 45 or 46 => new(ChatKind.Area),
        2 or 9 => new(ChatKind.Party),
        1 or 7 => new(ChatKind.Whisper),
        6 => new(ChatKind.Guild),
        13 => new(ChatKind.ChannelMegaphone, 37),
        11 => new(ChatKind.ServerMegaphone, 36),
        12 => new(ChatKind.ServerMegaphone, 898),
        57 => new(ChatKind.ServerMegaphone, 10151255),
        43 => new(ChatKind.Conversation),
        _ => null,
    };

    /// <summary>The modes whose request also names another character (A21 client static analysis).</summary>
    public static bool CarriesTarget(byte mode) => mode is 1 or 7 or 36 or 37 or 38;
}
