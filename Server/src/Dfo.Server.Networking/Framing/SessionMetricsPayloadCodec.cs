using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Networking.Framing;

/// <summary>
/// Decodes the SESSION_METRICS (0x00FA) body: seven <c>{u16 tag, u32 mean}</c> sections, then two
/// counted sections each <c>{u16 tag, u16 count}</c> followed by that many 9-byte records.
/// Body length is therefore <c>50 + 9 * (count7 + count8)</c>.
/// Layout is instruction-confirmed from the builder at MakeHeader site 0x01CD33B7; see
/// protocol/game/client-to-server/0x00FA.md.
/// </summary>
internal static class SessionMetricsPayloadCodec
{
    public const int MeanCount = 7;

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
            if (tag != index)
            {
                // The builder writes the loop counter itself, so these tags are 0..6 by
                // construction. Anything else means the frame is not this message.
                throw new InvalidDataException(
                    $"SESSION_METRICS mean section {index} declares tag '{tag}'; the client writes its own index.");
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
