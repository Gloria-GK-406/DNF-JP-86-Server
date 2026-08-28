using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.QuestCompletion;

/// <summary>
/// Reads the FINISH_QUEST (0x0022) body and writes its answer.
/// </summary>
/// <remarks>
/// Request: <c>{u16 echoed type, u16 questId, u16 rewardSelection, u16 completionCount,
/// u16 sentinel}</c>, the sentinel being 0xFFFF. Answer: <c>{0x00, u8 reason}</c> when refused and
/// <c>{0x01, u16 questId, u8 objectiveKind, u32 experience, u32 reserved, u8 consumedCount,
/// u8 itemCount, itemCount x 19 B}</c> when finished.
///
/// <b>Both directions are pinned to a capture taken on 2026-08-27</b>, of the reference server
/// answering the real client finishing quest 1016:
/// <code>
/// C-&gt;S  22 00 F8 03 FF FF 01 00 FF FF
/// S-&gt;C  01 F8 03 04 65 00 00 00 00 00 00 00 00 03 (+ 3 x 19 B)
/// </code>
/// See protocol/game/client-to-server/0x0022.md.
///
/// Only the chain-0 answer is written here, which is the ordinary completion: a quest that pays
/// experience and items. The reference server has five more shapes behind the same first bytes --
/// career change, title, slot expansion and two others -- each replacing the item list with
/// something else. None is reachable from the tutorial and none has a capture, so this codec
/// refuses to invent them rather than writing an untested branch that would desynchronise the
/// client mid-frame.
/// </remarks>
internal static class FinishQuestPayloadCodec
{
    /// <summary>The packet type the client repeats before the payload.</summary>
    public const ushort EchoedType = 0x0022;

    /// <summary>The value closing a well-formed request.</summary>
    /// <remarks>
    /// The reference server refuses the request outright when this is not 0xFFFF, and it is the
    /// only structural check it makes on the body beyond the length. Kept for the same reason it
    /// keeps it: with three little-endian fields of the same width in a row, the sentinel is what
    /// distinguishes this body from three unrelated numbers of the right total length.
    /// </remarks>
    public const ushort Sentinel = 0xFFFF;

    public const int BodyLength = 10;

    /// <summary>Nineteen bytes: the slot, the item, the count, and a stamped acquisition time.</summary>
    private const int ItemEntryLength = 19;

    public static FinishQuestRequest Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != BodyLength)
        {
            throw new InvalidDataException(
                $"FINISH_QUEST requires a {BodyLength}-byte body; received '{body.Length}'.");
        }

        var echoed = BinaryPrimitives.ReadUInt16LittleEndian(body);
        if (echoed != EchoedType)
        {
            throw new InvalidDataException(
                $"FINISH_QUEST opens with '{echoed}'; the client repeats its own type there.");
        }

        var sentinel = BinaryPrimitives.ReadUInt16LittleEndian(body[8..]);
        if (sentinel != Sentinel)
        {
            throw new InvalidDataException(
                $"FINISH_QUEST closes with '{sentinel}' where 0x{Sentinel:X4} is required; "
                + "this body has not been understood.");
        }

        return new FinishQuestRequest(
            metadata,
            BinaryPrimitives.ReadUInt16LittleEndian(body[2..]),
            BinaryPrimitives.ReadUInt16LittleEndian(body[4..]),
            BinaryPrimitives.ReadUInt16LittleEndian(body[6..]));
    }

    public static byte[] Encode(FinishQuestAcknowledgement message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!message.Finished)
            return [0x00, message.RefusalCode];

        if (message.Items.Count > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"FINISH_QUEST hands over '{message.Items.Count}' items; the count is one byte.");
        }

        var payload = new byte[14 + (message.Items.Count * ItemEntryLength)];
        payload[0] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1), message.QuestId);
        payload[3] = message.ObjectiveKind;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), message.Experience);
        // Reserved, and observed zero in every captured answer. The reference server has a field
        // here it fills only on a branch this codec does not write.
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), 0);
        // Nothing was taken back. A quest that consumes what it asked the player to collect fills
        // this, and none that this server can finish does.
        payload[12] = 0;
        payload[13] = (byte)message.Items.Count;

        var at = 14;
        foreach (var item in message.Items)
        {
            var entry = payload.AsSpan(at, ItemEntryLength);
            BinaryPrimitives.WriteUInt16LittleEndian(entry, item.SlotIndex);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[2..], item.ItemId);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[6..], item.Count);
            entry[10] = 0;                                        // upgrade level
            BinaryPrimitives.WriteUInt16LittleEndian(entry[11..], 0);  // durability
            BinaryPrimitives.WriteUInt32LittleEndian(entry[13..], message.AcquiredAtUnixTime);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[17..], 0);  // A21 entry tail
            at += ItemEntryLength;
        }

        return payload;
    }
}
