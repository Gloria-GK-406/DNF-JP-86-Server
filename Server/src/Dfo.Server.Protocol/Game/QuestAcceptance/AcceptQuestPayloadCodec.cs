using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.QuestAcceptance;

/// <summary>
/// Reads the ACCEPT_QUEST (0x001F) body and writes its answer.
/// </summary>
/// <remarks>
/// Request: <c>{u16 echoed type, u16 questId}</c>. Answer: <c>{0x00, u8 reason}</c> when refused,
/// and <c>{0x01, u16 questId, u32 initialTrigger, u8 count, count x (u16 slot, u32 itemId,
/// u32 count)}</c> when accepted. Both are the reference server's own builders, checked against
/// its capture: <c>01 F8 03 01 00 00 00 00</c> is quest 1016 starting at trigger 1 with no items.
/// </remarks>
internal static class AcceptQuestPayloadCodec
{
    /// <summary>The packet type the client repeats before the payload.</summary>
    /// <remarks>
    /// Not a field. The reference server drops two bytes off the front of this body before parsing
    /// it, and every observed body carries 0x001F there -- the type of the packet it is in. It is
    /// checked rather than skipped so that a body which does not carry it is refused here instead
    /// of being read one field to the left.
    /// </remarks>
    public const ushort EchoedType = 0x001F;

    public const int BodyLength = 4;

    public static AcceptQuestRequest Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != BodyLength)
        {
            throw new InvalidDataException(
                $"ACCEPT_QUEST requires a {BodyLength}-byte body; received '{body.Length}'.");
        }

        var echoed = BinaryPrimitives.ReadUInt16LittleEndian(body);
        if (echoed != EchoedType)
        {
            throw new InvalidDataException(
                $"ACCEPT_QUEST opens with '{echoed}'; the client repeats its own type there.");
        }

        return new AcceptQuestRequest(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body[2..]));
    }

    public static byte[] Encode(AcceptQuestAcknowledgement message)
    {
        if (!message.Accepted)
            return [0x00, message.RefusalCode];

        if (message.Items.Count > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"ACCEPT_QUEST hands over '{message.Items.Count}' items; the count is one byte.");
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)0x01);
        writer.Write(message.QuestId);
        writer.Write(message.InitialTrigger);
        writer.Write((byte)message.Items.Count);
        foreach (var item in message.Items)
        {
            writer.Write(item.SlotIndex);
            writer.Write(item.ItemId);
            writer.Write(item.Count);
        }

        return stream.ToArray();
    }
}
