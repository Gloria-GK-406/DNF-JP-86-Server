using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.OneToOneChat;

internal sealed class OpenConversationDecoder : IGameMessageDecoder
{
    /// <summary>The longest target name accepted, the same bound as a whisper's; character names are far shorter.</summary>
    public const int DecodedNameLimit = ChatDString.DecodedNameLimit;

    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < sizeof(int)) throw new InvalidDataException("CREATE_GROUP name length is missing.");
        var length = BinaryPrimitives.ReadInt32LittleEndian(body);
        if (length < 1 || length > DecodedNameLimit || length != body.Length - sizeof(int))
            throw new InvalidDataException($"CREATE_GROUP name length {length} does not fit its {body.Length}-byte body.");
        return new OpenConversationRequest(metadata, body[sizeof(int)..].ToArray());
    }
}

internal sealed class LeaveConversationDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != sizeof(uint))
            throw new InvalidDataException($"LEAVE_FROM_GROUP requires exactly 4 body bytes; received '{body.Length}'.");
        return new LeaveConversationRequest(metadata, new(BinaryPrimitives.ReadUInt32LittleEndian(body)));
    }
}

internal sealed class ConversationUserStateDecoder : IGameMessageDecoder
{
    // Nothing is done with the report, so its body is not read.
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new ConversationUserStateReport(metadata);
}

internal sealed class ConversationOpenedEncoder : GameNotiMessageEncoder<ConversationOpenedMessage>
{
    /// <summary>Result 0: success. The client treats every value but 77, 100 and 101 as one.</summary>
    private const byte Opened = 0;

    /// <summary>The longest body this frame can have: <c>1 + 4 + 1 + 2 x (4 + 127)</c>.</summary>
    public const int MaximumBodyLength = 1 + sizeof(uint) + 1 + 2 * (sizeof(int) + ConversationOpenedMessage.NameLimit);

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ConversationOpenedMessage message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(ConversationOpenedMessage message)
    {
        ChatDString.Require(message.PeerName, 1, ConversationOpenedMessage.NameLimit, "conversation peer name");
        ChatDString.Require(message.OwnName, 1, ConversationOpenedMessage.NameLimit, "conversation own name");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Opened);
        writer.Write(message.Conversation.Value);
        writer.Write((byte)2);
        ChatDString.Write(writer, message.PeerName);
        ChatDString.Write(writer, message.OwnName);
        writer.Flush();
        return stream.ToArray();
    }
}
