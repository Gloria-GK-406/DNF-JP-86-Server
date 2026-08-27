using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.SessionMetrics;

/// <summary>
/// Decodes the SESSION_METRICS (0x00FA) body: eight <c>{u16 tag, u32 mean}</c> sections, then two
/// counted sections each <c>{u16 tag, u16 count}</c> followed by that many 9-byte records.
/// Body length is therefore <c>56 + 9 * (count7 + count8)</c>.
/// Layout is instruction-confirmed from the builder at MakeHeader site 0x01CD33B7; see
/// protocol/game/client-to-server/0x00FA.md.
/// </summary>
/// <remarks>
/// <b>A21 writes eight means where A12 wrote seven.</b> The first seven carry their own index as
/// the tag; the eighth carries the literal 9, and it sits before the counted sections rather than
/// after them. An A21 body is therefore 56 bytes and not 50, and reading it at A12's offsets finds
/// tag 9 where tag 7 was expected.
/// <para>
/// This mattered because it is on the way out of the game. The client flushes five telemetry
/// reports and then asks to leave; this is one of the five. On 2026-08-27 the frame before it,
/// <c>CMD 0x00C2</c>, was also mis-sized and had already taken the connection down, so this one
/// never arrived to fail -- it was found by replaying the captured burst against a server that
/// could get past 0x00C2. See protocol/patterns/data/traces/a21-exit-and-return-20260827.md.
/// </para>
/// <para>
/// <b>Only the means are evidenced on A21.</b> The one captured body declares both counted
/// sections empty, so the 9-byte record shape below is still A12's and unconfirmed here.
/// </para>
/// </remarks>
internal static class SessionMetricsPayloadCodec
{
    /// <summary>The tag each mean section carries, in the order the client writes them.</summary>
    /// <remarks>
    /// Zero through six are the loop counter the builder writes for itself. Nine is an immediate,
    /// and what it measures is unknown: one sample, value 60950. It is read and kept rather than
    /// skipped so a second sample can contradict it.
    /// </remarks>
    public static ReadOnlySpan<ushort> MeanTags => [0, 1, 2, 3, 4, 5, 6, 9];

    public const int MeanCount = 8;

    public const int MeanLength = 6;

    public const int RecordLength = 9;

    /// <summary>A counted section's <c>{u16 tag, u16 count}</c> preamble.</summary>
    private const int SectionHeaderLength = 4;

    /// <summary>Body length when both counted sections are empty.</summary>
    public const int MinimumBodyLength = (MeanCount * MeanLength) + (2 * SectionHeaderLength);

    /// <summary>The builder writes these tags as immediates, in this order.</summary>
    public const ushort FirstCountedSectionTag = 7;

    public const ushort SecondCountedSectionTag = 8;

    public static SessionMetricsReportRequest Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body)
    {
        if (body.Length < MinimumBodyLength)
        {
            throw new InvalidDataException(
                $"SESSION_METRICS requires at least {MinimumBodyLength} body bytes; received '{body.Length}'.");
        }

        var means = new SessionMetricMean[MeanCount];
        for (var index = 0; index < MeanCount; index++)
        {
            var section = body.Slice(index * MeanLength, MeanLength);
            var tag = BinaryPrimitives.ReadUInt16LittleEndian(section);
            if (tag != MeanTags[index])
            {
                // The first seven tags are the loop counter the builder writes for itself and the
                // eighth is an immediate, so the whole sequence is fixed by construction. Anything
                // else means the frame is not this message.
                throw new InvalidDataException(
                    $"SESSION_METRICS mean section {index} declares tag '{tag}'; the client writes "
                    + $"'{MeanTags[index]}' there.");
            }

            means[index] = new SessionMetricMean(tag, BinaryPrimitives.ReadUInt32LittleEndian(section[2..]));
        }

        var offset = MeanCount * MeanLength;
        var section7 = ReadCountedSection(body, ref offset, FirstCountedSectionTag);
        var section8 = ReadCountedSection(body, ref offset, SecondCountedSectionTag);

        if (offset != body.Length)
        {
            throw new InvalidDataException(
                $"SESSION_METRICS declares {offset} body bytes; received '{body.Length}'.");
        }

        return new SessionMetricsReportRequest(metadata, means, section7, section8);
    }

    private static SessionMetricRecord[] ReadCountedSection(
        ReadOnlySpan<byte> body,
        ref int offset,
        ushort expectedTag)
    {
        var tag = BinaryPrimitives.ReadUInt16LittleEndian(body[offset..]);
        if (tag != expectedTag)
        {
            throw new InvalidDataException(
                $"SESSION_METRICS expected counted section tag '{expectedTag}'; received '{tag}'.");
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(body[(offset + 2)..]);
        offset += SectionHeaderLength;

        var required = count * RecordLength;
        if (body.Length - offset < required)
        {
            throw new InvalidDataException(
                $"SESSION_METRICS section '{expectedTag}' declares '{count}' records needing {required} bytes; "
                + $"only {body.Length - offset} remain.");
        }

        var records = new SessionMetricRecord[count];
        for (var index = 0; index < count; index++)
        {
            var record = body.Slice(offset + (index * RecordLength), RecordLength);
            records[index] = new SessionMetricRecord(
                BinaryPrimitives.ReadUInt16LittleEndian(record),
                record[2],
                BinaryPrimitives.ReadUInt16LittleEndian(record[3..]),
                BinaryPrimitives.ReadUInt16LittleEndian(record[5..]),
                BinaryPrimitives.ReadUInt16LittleEndian(record[7..]));
        }

        offset += required;
        return records;
    }
}
