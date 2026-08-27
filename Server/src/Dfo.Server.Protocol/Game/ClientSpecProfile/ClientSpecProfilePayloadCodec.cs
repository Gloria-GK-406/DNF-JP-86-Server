using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ClientSpecProfile;

/// <summary>
/// Decodes the CLIENT_SPEC_PROFILE (0x00C2) body:
/// a 25-byte header, a fixed 336-byte table of 6 rows x (4 x u16 + 6 x (u32 + f32)),
/// then a u8 entry count followed by that many 3-byte entries.
/// Body length is therefore <c>362 + 3 * count</c> with count in 0..6.
/// The table and the entry list are instruction-confirmed from A12's serializer at
/// RVA 0x01CD6260; see protocol/game/client-to-server/0x00C2.md.
/// </summary>
/// <remarks>
/// <b>The header is twenty-five bytes on A21 and was twenty-four on A12.</b> One byte sits between
/// the operating-system kind and the DirectX version, and everything after it moves down by one.
/// Read at A12's offsets, an A21 body is one byte too long and this decoder threw -- which drops
/// the connection. Live on 2026-08-27: the client sent this once, on arriving back in town after
/// the tutorial, and from that moment it was talking to nothing. The player finished the run, went
/// to leave, and the exit dialog hung; the exit protocol was answered correctly and had no one to
/// answer.
/// <para>
/// The boundary is evidenced rather than guessed. The same machine produced both samples: A12's
/// 361 bytes and A21's 362 carry the same twelve cores, the same GPU 8086:A7A8 and the same
/// 4096/8192 MB. On A21 every field from the DirectX version onwards reads as noise at A12's
/// offsets and reads exactly right one byte later -- DirectX becomes 0x00090000 (9.0.0, which is
/// what the client's own LagLog.txt prints), both timings become the -1 the client writes for
/// unmeasured, and the table resolves into plausible counts paired with plausible durations
/// instead of straddling its own fields.
/// </para>
/// <para>
/// <b>What the new byte means is not known.</b> One sample, value 1. It is reported rather than
/// skipped so that a second sample can contradict it.
/// </para>
/// </remarks>
internal static class ClientSpecProfilePayloadCodec
{
    public const int TableRowCount = 6;

    public const int TableRowWords = 4;

    public const int TableRowPairs = 6;

    public const int TableRowLength = (TableRowWords * 2) + (TableRowPairs * 8);

    public const int HeaderLength = 25;

    public const int TableLength = TableRowCount * TableRowLength;

    /// <summary>Body length when the trailing entry list is empty.</summary>
    public const int MinimumBodyLength = HeaderLength + TableLength + 1;

    public const int EntryLength = 3;

    /// <summary>The client clamps the entry count to this maximum, mapping anything larger to 0.</summary>
    public const int MaximumEntryCount = 6;

    public static ClientSpecProfileReportRequest Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body)
    {
        if (body.Length < MinimumBodyLength)
        {
            throw new InvalidDataException(
                $"CLIENT_SPEC_PROFILE requires at least {MinimumBodyLength} body bytes; received '{body.Length}'.");
        }

        var entryCount = body[MinimumBodyLength - 1];
        if (entryCount > MaximumEntryCount)
        {
            throw new InvalidDataException(
                $"CLIENT_SPEC_PROFILE declares '{entryCount}' entries; the client clamps the count to {MaximumEntryCount}.");
        }

        var expectedLength = MinimumBodyLength + (entryCount * EntryLength);
        if (body.Length != expectedLength)
        {
            // The client also has an abort path that appends a single 0 byte when its two parallel
            // entry arrays disagree in length, which lands here rather than being parsed as data.
            throw new InvalidDataException(
                $"CLIENT_SPEC_PROFILE with '{entryCount}' entries requires a {expectedLength}-byte body; received '{body.Length}'.");
        }

        var table = new ClientSpecProfileRow[TableRowCount];
        for (var row = 0; row < TableRowCount; row++)
        {
            var rowBody = body.Slice(HeaderLength + (row * TableRowLength), TableRowLength);
            var words = new ushort[TableRowWords];
            for (var word = 0; word < TableRowWords; word++)
            {
                words[word] = BinaryPrimitives.ReadUInt16LittleEndian(rowBody.Slice(word * 2, 2));
            }

            var pairs = new ClientSpecProfilePair[TableRowPairs];
            for (var pair = 0; pair < TableRowPairs; pair++)
            {
                var pairBody = rowBody.Slice((TableRowWords * 2) + (pair * 8), 8);
                pairs[pair] = new ClientSpecProfilePair(
                    BinaryPrimitives.ReadUInt32LittleEndian(pairBody[..4]),
                    BinaryPrimitives.ReadSingleLittleEndian(pairBody[4..]));
            }

            table[row] = new ClientSpecProfileRow(words, pairs);
        }

        var entries = new ClientSpecProfileEntry[entryCount];
        for (var index = 0; index < entryCount; index++)
        {
            var entryBody = body.Slice(MinimumBodyLength + (index * EntryLength), EntryLength);
            entries[index] = new ClientSpecProfileEntry(
                entryBody[0],
                BinaryPrimitives.ReadUInt16LittleEndian(entryBody[1..]));
        }

        return new ClientSpecProfileReportRequest(
            metadata,
            BinaryPrimitives.ReadUInt32LittleEndian(body[..4]),
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(4, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(6, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(8, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(10, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(12, 2)),
            body[14],
            body[15],
            BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(16, 4)),
            body[20],
            // Signed: the client logs both timings as "-1s" when unmeasured, i.e. 0xFFFF.
            BinaryPrimitives.ReadInt16LittleEndian(body.Slice(21, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(body.Slice(23, 2)),
            table,
            entries);
    }
}
