using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.QuestCompletion;

/// <summary>
/// Reads the FINISH_QUEST (0x0022) body and writes its answer.
/// </summary>
/// <remarks>
/// Request: <c>{u16 echoed type, u16 questId, u16 rewardSelection, u16 completionCount,
/// u16 sentinel}</c>, the sentinel being 0xFFFF. Answer: <c>{0x00, u8 reason}</c> when refused and
/// <c>{0x01, u16 questId, u8 objectiveKind, u32 experience, u32 reserved,
/// [u8 consumedCount, consumedCount x 7 B], u8 chain(0), u8 itemCount, itemCount x 19 B}</c>
/// for ordinary rewards. The consumption prefix is present only for objective kinds 0/0x19.
///
/// <b>Both directions are pinned to a capture taken on 2026-08-27</b>, of the reference server
/// answering the real client finishing quest 1016:
/// <code>
/// C-&gt;S  22 00 F8 03 FF FF 01 00 FF FF
/// S-&gt;C  01 F8 03 04 65 00 00 00 00 00 00 00 00 03 (+ 3 x 19 B)
/// </code>
/// See protocol/game/client-to-server/0x0022.md.
///
/// The met-NPC capture has chain=0 at offset 12, not a universal consumption count.
/// A21 reader RVA 0x00D40B23..0x00D40C87 reads the conditional prefix, then chain independently;
/// RVA 0x00D4181A opens a special window for chain=3. Material quests must not put reward count there.
/// Career completion uses the reference
/// server's chain-1 shape; this branch is not yet ClientVerified.
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

        if (message.Awakening is not null)
        {
            // ServerS4A21 QuestAckBuilder: chain 2, reserved zero, then both compact skill pages.
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((byte)1);
            writer.Write(message.QuestId);
            writer.Write(message.ObjectiveKind);
            writer.Write(message.Experience);
            writer.Write(0u);
            if (message.ObjectiveKind is 0x00 or 0x19) writer.Write((byte)0);
            writer.Write((byte)2);
            writer.Write((byte)0);
            for (var pageIndex = 0; pageIndex < 2; pageIndex++)
            {
                var skills = message.SkillPages.SingleOrDefault(page => page.PageIndex == pageIndex)?.Skills ?? [];
                writer.Write(checked((byte)skills.Count));
                foreach (var skill in skills)
                {
                    writer.Write(checked((byte)skill.Slot));
                    writer.Write(checked((ushort)skill.SkillId));
                    writer.Write(skill.Level);
                }
            }
            return stream.ToArray();
        }

        if (message.GrowType is { } growType)
        {
            // Reference 86JP QuestAckBuilder.BuildFinish chain 1; A21 live acceptance is pending.
            var career = new byte[17];
            career[0] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(career.AsSpan(1), message.QuestId);
            BinaryPrimitives.WriteUInt32LittleEndian(career.AsSpan(4), message.Experience);
            BinaryPrimitives.WriteUInt32LittleEndian(career.AsSpan(8),
                checked((uint)message.Items.Where(item => item.ItemId == 0).Sum(item => (long)item.Count)));
            career[13] = 1;
            career[14] = growType;
            return career;
        }

        if (message.Items.Count > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"FINISH_QUEST hands over '{message.Items.Count}' items; the count is one byte.");
        }

        var hasConsumptionPrefix = message.ObjectiveKind is 0x00 or 0x19;
        var headerLength = hasConsumptionPrefix ? 15 : 14;
        var payload = new byte[headerLength + (message.Items.Count * ItemEntryLength)];
        payload[0] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1), message.QuestId);
        payload[3] = message.ObjectiveKind;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), message.Experience);
        // Reserved, and observed zero in every captured answer. The reference server has a field
        // here it fills only on a branch this codec does not write.
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), 0);
        var at = 12;
        // Consumption is sent as authoritative inventory deltas after this ACK. Seeking/0x19
        // still require an empty consumption prefix; other objectives read chain immediately.
        if (hasConsumptionPrefix)
            payload[at++] = 0;
        payload[at++] = 0; // Ordinary item reward chain; independent of objective and item count.
        payload[at++] = (byte)message.Items.Count;

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
