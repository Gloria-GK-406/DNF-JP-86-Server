using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.QuestProgress;

/// <summary>
/// Reads the SET_QUEST_TRIGGER (0x0021) body and writes its answer.
/// </summary>
/// <remarks>
/// Request: <c>{u16 echoed type, u16 questId, u8 selector, u8 increment}</c>. Answer:
/// <c>{0x00, u8 reason}</c> when refused and <c>{0x01, u16 questId, u32 trigger}</c> otherwise.
/// Both are the reference server's own builders, read out of its <c>QuestService.HandleSetTrigger</c>
/// and <c>QuestAckBuilder.BuildSetTrigger</c>.
///
/// <b>No capture of this protocol exists.</b> The request bytes are this server's own live log --
/// <c>21 00 F8 03 00 00</c>, sent five times because nothing answered it -- and the answer's shape
/// is read from the reference server's source alone. The reference sessions on file never took a
/// quest to an NPC, so none of them carries one of these frames in either direction.
/// </remarks>
internal static class SetQuestTriggerPayloadCodec
{
    /// <summary>The packet type the client repeats before the payload.</summary>
    /// <remarks>
    /// Not a field. The reference server drops two bytes off the front of this body before parsing
    /// it, and the observed body carries 0x0021 there -- the type of the packet it is in. It is
    /// checked rather than skipped so that a body which does not carry it is refused here instead
    /// of being read one field to the left, where the quest number would come out of the echo.
    /// </remarks>
    public const ushort EchoedType = 0x0021;

    /// <summary>
    /// The shortest body this can be read from, which is one byte shorter than what the client
    /// sends.
    /// </summary>
    /// <remarks>
    /// The reference server requires three bytes after the echo and treats a missing fourth as
    /// "one fewer" -- <c>body.Length >= 4 &amp;&amp; body[3] != 0</c>. Followed here rather than
    /// tightened to the observed six, because a five-byte body is a body it would accept and this
    /// server rejecting it would be a difference invented rather than found.
    /// </remarks>
    public const int MinimumBodyLength = 5;

    public static SetQuestTriggerRequest Decode(
        GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < MinimumBodyLength)
        {
            throw new InvalidDataException(
                $"SET_QUEST_TRIGGER requires at least {MinimumBodyLength} bytes; "
                + $"received '{body.Length}'.");
        }

        var echoed = BinaryPrimitives.ReadUInt16LittleEndian(body);
        if (echoed != EchoedType)
        {
            throw new InvalidDataException(
                $"SET_QUEST_TRIGGER opens with '{echoed}'; the client repeats its own type there.");
        }

        return new SetQuestTriggerRequest(
            metadata,
            BinaryPrimitives.ReadUInt16LittleEndian(body[2..]),
            body[4],
            body.Length >= 6 && body[5] != 0);
    }

    public static byte[] Encode(SetQuestTriggerAcknowledgement message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!message.Answered)
            return [0x00, message.RefusalCode];

        var payload = new byte[7];
        payload[0] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1), message.QuestId);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(3), message.TriggerValue);
        return payload;
    }
}
