using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.SendChat;

internal sealed class SendChatDecoder : IGameMessageDecoder
{
    /// <summary>The largest text field accepted at all; the per-frame limits are applied by the command.</summary>
    /// <remarks>
    /// Twice the largest text any chat frame may carry, so an over-long line is still read and refused
    /// with an answer, while a length field no client produces ends the connection here.
    /// </remarks>
    public const int DecodedTextLimit = 1024;

    /// <summary>The largest target name accepted; character names are far shorter.</summary>
    public const int DecodedNameLimit = 64;

    private const int FixedLength = 1 + 2 + 4;

    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < FixedLength) throw new InvalidDataException("SEND_CHAT is shorter than its fixed fields.");
        var mode = body[0];
        var target = BinaryPrimitives.ReadUInt16LittleEndian(body[1..]);
        var aux = BinaryPrimitives.ReadUInt32LittleEndian(body[3..]);
        var offset = FixedLength;
        var text = ReadDString(body, ref offset, DecodedTextLimit, "text");
        byte[]? name = null;
        byte? group = null;
        if (ChatWireModes.CarriesTarget(mode))
        {
            name = ReadDString(body, ref offset, DecodedNameLimit, "target name");
            if (offset >= body.Length) throw new InvalidDataException("SEND_CHAT is missing the target server group.");
            group = body[offset];
        }
        // Bytes past the known fields are left unread rather than refused: the layout is from static
        // analysis only, and a trailing field this reading does not know is no reason to drop the player.
        return new SendChatRequest(metadata, mode, target, aux, text, name, group);
    }

    private static byte[] ReadDString(ReadOnlySpan<byte> body, ref int offset, int limit, string field)
    {
        if (body.Length - offset < sizeof(int)) throw new InvalidDataException($"SEND_CHAT {field} length is missing.");
        var length = BinaryPrimitives.ReadInt32LittleEndian(body[offset..]);
        offset += sizeof(int);
        // Compared against what remains rather than by adding to the offset, which a hostile length could overflow.
        if (length < 0 || length > limit || length > body.Length - offset)
            throw new InvalidDataException($"SEND_CHAT {field} length {length} is out of range.");
        var value = body.Slice(offset, length).ToArray();
        offset += length;
        return value;
    }
}

internal sealed class ItemLinkChatDecoder : IGameMessageDecoder
{
    // Item-link lines are refused whatever they carry, so the body is never read.
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new ItemLinkChatRequest(metadata);
}

internal sealed class SendChatRefusalEncoder : GameCmdMessageEncoder<SendChatRefusal>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(SendChatRefusal message) =>
        OnePayload(message, GameCmdEnvelope.Refuse(message.Reason));
}

internal sealed class ItemLinkChatRefusalEncoder : GameCmdMessageEncoder<ItemLinkChatRefusal>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ItemLinkChatRefusal message) =>
        OnePayload(message, GameCmdEnvelope.Refuse(message.Reason));
}

internal sealed class ChatLineEncoder : GameNotiMessageEncoder<ChatLineMessage>
{
    /// <summary>The longest body this frame can have: <c>1 + 2 + 1 + 4 + 255</c>.</summary>
    public const int MaximumBodyLength = 1 + 2 + 1 + sizeof(int) + ChatLineMessage.TextLimit;

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ChatLineMessage message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(ChatLineMessage message)
    {
        ChatDString.Require(message.Text, 1, ChatLineMessage.TextLimit, "chat text");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(message.Mode);
        writer.Write(message.SenderUserId);
        // Server group: this server is the only one, and the client drops 0xFA or more.
        writer.Write((byte)0);
        ChatDString.Write(writer, message.Text);
        writer.Flush();
        return stream.ToArray();
    }
}

internal sealed class WhisperEncoder : GameNotiMessageEncoder<WhisperMessage>
{
    /// <summary>The longest body this frame can have: <c>1 + 1 + 4 + 30 + 1 + 4 + 255</c>.</summary>
    public const int MaximumBodyLength = 1 + 1 + sizeof(int) + WhisperMessage.NameLimit + 1 + sizeof(int) + WhisperMessage.TextLimit;

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WhisperMessage message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(WhisperMessage message)
    {
        ChatDString.Require(message.SenderName, 1, WhisperMessage.NameLimit, "whisper sender name");
        ChatDString.Require(message.Text, 1, WhisperMessage.TextLimit, "whisper text");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(message.Mode);
        // Status 0 is an ordinary incoming whisper.
        writer.Write((byte)0);
        ChatDString.Write(writer, message.SenderName);
        // The sender's server: the only one there is.
        writer.Write((byte)0);
        ChatDString.Write(writer, message.Text);
        writer.Flush();
        return stream.ToArray();
    }
}

internal sealed class MegaphoneEncoder : GameNotiMessageEncoder<MegaphoneMessage>
{
    /// <summary>The longest body this frame can have: <c>1 + 1 + 1 + 1 + 2 + 4 + 29 + 4 + 511</c>.</summary>
    public const int MaximumBodyLength = 1 + 1 + 1 + 1 + 2 + sizeof(int) + MegaphoneMessage.NameLimit + sizeof(int) + MegaphoneMessage.TextLimit;

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(MegaphoneMessage message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(MegaphoneMessage message)
    {
        ChatDString.Require(message.SenderName, 1, MegaphoneMessage.NameLimit, "megaphone sender name");
        ChatDString.Require(message.Text, 1, MegaphoneMessage.TextLimit, "megaphone text");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(message.Mode);
        writer.Write(message.ServerId);
        // Server group, as in 0x000C.
        writer.Write((byte)0);
        writer.Write(message.ChannelId);
        writer.Write(message.SenderUserId);
        ChatDString.Write(writer, message.SenderName);
        ChatDString.Write(writer, message.Text);
        writer.Flush();
        return stream.ToArray();
    }
}

/// <summary>The <c>i32</c>-counted byte string every chat frame uses, written as the bytes arrived.</summary>
internal static class ChatDString
{
    /// <summary>
    /// The fan-out bound. A frame over the client's limit would be dropped by every recipient, and
    /// an unbounded one could fill a third party's send queue, so it is refused before it is built.
    /// </summary>
    public static void Require(byte[] value, int minimum, int maximum, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length < minimum || value.Length > maximum)
            throw new InvalidDataException($"The {field} is {value.Length} bytes; the client takes {minimum} to {maximum}.");
    }

    public static void Write(BinaryWriter writer, byte[] value)
    {
        writer.Write(value.Length);
        writer.Write(value);
    }
}
