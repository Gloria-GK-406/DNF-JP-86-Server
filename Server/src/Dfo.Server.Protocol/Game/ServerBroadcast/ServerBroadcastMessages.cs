using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ServerBroadcast;

/// <summary>NOTI <c>0x0056</c>: a server-wide announcement the client words from its own text table.</summary>
/// <remarks>
/// A21 handler VA <c>011803E0</c> reads a subtype byte and then that subtype's fields; see
/// <c>protocol/game/server-to-client/NOTI-0x0056.md</c>. Only the subtypes that carry the actor's
/// name are modelled: the others name the actor by a u16 user id, which a client shows only if it has
/// met that character on its current channel, so they cannot announce to the whole server
/// (ClientVerified 2026-11-14). Names are the stored character-name bytes, passed on untouched.
/// </remarks>
public abstract record ServerBroadcastMessage : IGameNotiMessage
{
    /// <summary>The client reads a name only when its length is below 0x100.</summary>
    public const int NameLimit = 0xFF;

    private protected ServerBroadcastMessage()
    {
    }

    public ServerMessageType MessageType => ServerMessageType.GameServerBroadcast;

    private protected static byte[] RequireName(byte[] name, string parameter)
    {
        ArgumentNullException.ThrowIfNull(name, parameter);
        if (name.Length is 0 or > NameLimit)
            throw new ArgumentOutOfRangeException(parameter, "A broadcast name must be 1 to 255 bytes.");
        return name;
    }
}

/// <summary>Subtype 0x12: the actor opened <see cref="ItemId"/> from the box <see cref="BoxItemId"/> (text 0x11323).</summary>
public sealed record BoxRewardBroadcast : ServerBroadcastMessage
{
    public BoxRewardBroadcast(byte[] name, int boxItemId, int itemId)
    {
        Name = RequireName(name, nameof(name));
        BoxItemId = boxItemId > 0 ? boxItemId : throw new ArgumentOutOfRangeException(nameof(boxItemId));
        ItemId = itemId > 0 ? itemId : throw new ArgumentOutOfRangeException(nameof(itemId));
    }

    public byte[] Name { get; }
    public int BoxItemId { get; }
    public int ItemId { get; }
}

/// <summary>Subtype 0x14: the actor completed the second awakening (text 0x11728, red).</summary>
public sealed record SecondAwakeningBroadcast : ServerBroadcastMessage
{
    public SecondAwakeningBroadcast(byte[] name) => Name = RequireName(name, nameof(name));

    public byte[] Name { get; }
}

internal sealed class ServerBroadcastEncoder<TMessage> : GameNotiMessageEncoder<TMessage>
    where TMessage : ServerBroadcastMessage
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(TMessage message) =>
        OnePayload(message, Body(message));

    internal static byte[] Body(ServerBroadcastMessage message)
    {
        using var stream = new MemoryStream();
        switch (message)
        {
            case BoxRewardBroadcast box:
                stream.WriteByte(0x12);
                Name(stream, box.Name);
                Int32(stream, box.BoxItemId);
                Int32(stream, box.ItemId);
                break;
            case SecondAwakeningBroadcast awakening:
                stream.WriteByte(0x14);
                Name(stream, awakening.Name);
                break;
            default:
                throw new NotSupportedException($"Broadcast '{message.GetType().Name}' has no layout.");
        }
        return stream.ToArray();
    }

    private static void Name(Stream stream, byte[] name)
    {
        Int32(stream, name.Length);
        stream.Write(name);
    }

    private static void Int32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}
