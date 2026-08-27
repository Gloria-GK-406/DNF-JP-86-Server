using System.Buffers.Binary;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.ClientSpecProfile;

namespace Dfo.Server.Protocol.Tests.Game.ClientSpecProfile;

public sealed class ClientSpecProfilePayloadCodecTests
{
    // The A21 header, from the reference server's own capture of this client on 2026-08-27: the
    // 25-byte header followed by an all-zero 336-byte table and an entry count of 0, i.e. 362.
    private static byte[] ObservedBody()
    {
        var body = new byte[ClientSpecProfilePayloadCodec.MinimumBodyLength];
        byte[] header =
        [
            0x38, 0x0A, 0x00, 0x00,   // +0  cpu clock 2616 MHz
            0x0C, 0x00,               // +4  12 logical processors
            0x86, 0x80,               // +6  GPU PCI vendor 0x8086
            0xA8, 0xA7,               // +8  GPU PCI device 0xA7A8
            0x00, 0x10,               // +10 normalized video memory 4096 MB
            0x00, 0x20,               // +12 normalized total memory 8192 MB
            0x0E,                     // +14 OS 14 (Windows 10/11)
            0x01,                     // +15 A21 only; A12 has no byte here
            0x00, 0x00, 0x09, 0x00,   // +16 once-per-session value
            0x00,                     // +20
            0xFF, 0xFF,               // +21
            0xFF, 0xFF,               // +23
        ];
        header.CopyTo(body, 0);
        return body;
    }

    [Fact]
    public void Decode_InterpretsTheObservedBodyLosslessly()
    {
        var metadata = new GameMessageMetadata(0x01, 0x00C2, 0xEA2B0775, 8);

        var request = ClientSpecProfilePayloadCodec.Decode(metadata, ObservedBody());

        Assert.Equal(ClientMessageType.GameClientSpecProfile, request.MessageType);
        Assert.Equal(2616u, request.CpuClockMhz);
        Assert.Equal(12, request.CpuLogicalCores);
        Assert.Equal(0x8086, request.GpuPciVendorId);
        Assert.Equal(0xA7A8, request.GpuPciDeviceId);
        Assert.Equal(4096, request.NormalizedVideoMemoryMegabytes);
        Assert.Equal(8192, request.NormalizedTotalMemoryMegabytes);
        Assert.Equal(0x0E, request.OperatingSystemKind);
        // 0x00090000 is DirectX 9.0.0; the client's own LagLog.txt prints it as hex "90000".
        Assert.Equal(0x00090000u, request.DirectXVersion);
        Assert.Equal(1, request.Unknown15);
        Assert.Equal(0, request.Unknown20);
        // Both timings are 0xFFFF on the wire, which the client logs as "-1s" -- unmeasured.
        Assert.Equal(-1, request.StartLoadingSeconds);
        Assert.Equal(-1, request.ReturnVillageSeconds);
        Assert.Equal(6, request.Table.Count);
        Assert.All(request.Table, row =>
        {
            Assert.Equal(4, row.Words.Count);
            Assert.Equal(6, row.Pairs.Count);
            Assert.All(row.Words, word => Assert.Equal(0, word));
            Assert.All(row.Pairs, pair => Assert.Equal(new ClientSpecProfilePair(0, 0f), pair));
        });
        Assert.Empty(request.Entries);
    }

    [Fact]
    public void Decode_ReadsTheTableRowByRowAtTheRightStride()
    {
        // Row 3 word 2 and row 5 pair 4 get distinct values; a wrong stride lands them elsewhere.
        var body = ObservedBody();
        var row3 = ClientSpecProfilePayloadCodec.HeaderLength + (3 * ClientSpecProfilePayloadCodec.TableRowLength);
        var row5 = ClientSpecProfilePayloadCodec.HeaderLength + (5 * ClientSpecProfilePayloadCodec.TableRowLength);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(row3 + (2 * 2), 2), 0x1234);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(row5 + 8 + (4 * 8), 4), 0xDEADBEEF);
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(row5 + 8 + (4 * 8) + 4, 4), 1.5f);

        var request = ClientSpecProfilePayloadCodec.Decode(GameMessageMetadata.Empty, body);

        Assert.Equal(0x1234, request.Table[3].Words[2]);
        Assert.Equal(new ClientSpecProfilePair(0xDEADBEEF, 1.5f), request.Table[5].Pairs[4]);
        Assert.Equal(0, request.Table[3].Words[1]);
        Assert.Equal(new ClientSpecProfilePair(0, 0f), request.Table[4].Pairs[4]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void Decode_ReadsTheTrailingEntryList(byte count)
    {
        var body = new byte[ClientSpecProfilePayloadCodec.MinimumBodyLength
            + (count * ClientSpecProfilePayloadCodec.EntryLength)];
        ObservedBody().CopyTo(body, 0);
        body[ClientSpecProfilePayloadCodec.MinimumBodyLength - 1] = count;
        for (var index = 0; index < count; index++)
        {
            var offset = ClientSpecProfilePayloadCodec.MinimumBodyLength
                + (index * ClientSpecProfilePayloadCodec.EntryLength);
            body[offset] = (byte)(index + 1);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset + 1, 2), (ushort)(100 * (index + 1)));
        }

        var request = ClientSpecProfilePayloadCodec.Decode(GameMessageMetadata.Empty, body);

        Assert.Equal(count, request.Entries.Count);
        for (var index = 0; index < count; index++)
        {
            Assert.Equal(new ClientSpecProfileEntry((byte)(index + 1), (ushort)(100 * (index + 1))), request.Entries[index]);
        }
    }

    [Fact]
    public void Decode_RejectsBodiesShorterThanTheFixedPart()
    {
        Assert.Throws<InvalidDataException>(
            () => ClientSpecProfilePayloadCodec.Decode(GameMessageMetadata.Empty, ObservedBody().AsSpan(0, 361)));
        Assert.Throws<InvalidDataException>(
            () => ClientSpecProfilePayloadCodec.Decode(GameMessageMetadata.Empty, []));
    }

    [Fact]
    public void Decode_RejectsAnEntryCountTheClientWouldHaveClamped()
    {
        var body = ObservedBody();
        body[ClientSpecProfilePayloadCodec.MinimumBodyLength - 1] = 7;

        Assert.Throws<InvalidDataException>(
            () => ClientSpecProfilePayloadCodec.Decode(GameMessageMetadata.Empty, body));
    }

    [Fact]
    public void Decode_RejectsALengthThatDisagreesWithTheEntryCount()
    {
        // This is also the shape of the client's abort path, which appends one stray 0 byte when
        // its two parallel entry arrays disagree; it must not be parsed as an entry.
        var body = new byte[ClientSpecProfilePayloadCodec.MinimumBodyLength + 1];
        ObservedBody().CopyTo(body, 0);
        body[ClientSpecProfilePayloadCodec.MinimumBodyLength - 1] = 2;

        Assert.Throws<InvalidDataException>(
            () => ClientSpecProfilePayloadCodec.Decode(GameMessageMetadata.Empty, body));
    }

    [Fact]
    public void Decode_ObservedBodyLengthMatchesTheDocumentedArithmetic()
    {
        // 25-byte header + 6 rows x (4 u16 + 6 x (u32 + f32)) + 1 count byte = 362.
        Assert.Equal(56, ClientSpecProfilePayloadCodec.TableRowLength);
        Assert.Equal(336, ClientSpecProfilePayloadCodec.TableLength);
        Assert.Equal(362, ClientSpecProfilePayloadCodec.MinimumBodyLength);
    }

    /// <summary>
    /// The whole captured body, table included, exactly as the client sent it.
    /// </summary>
    /// <remarks>
    /// This is the frame that dropped the connection on 2026-08-27. Read at A12's offsets it is one
    /// byte too long, and a decoder that throws takes the session with it: the client sent this on
    /// arriving back in town after the tutorial, and everything it said afterwards -- including the
    /// request to leave the game -- went to a connection that was already gone.
    /// <para>
    /// The table is the part that settles where the extra byte goes. At A12's offsets its rows
    /// straddle their own fields; one byte later every row reads as four zero words and then pairs
    /// of a small count with a plausible duration, which is what the client's own lag log records.
    /// </para>
    /// </remarks>
    [Fact]
    public void Decode_ReadsTheCapturedA21BodyIncludingItsTable()
    {
        var body = Convert.FromHexString(
            "380A00000C008680A8A700100020" + "0E" + "01" + "00000900" + "00" + "FFFF" + "FFFF"
            + "0000000000000000"
            + "05000000" + "1F85AB3F" + "04000000" + "7D3FA53F" + "04000000" + "7D3FA53F"
            + "03000000" + "1383903F" + "00000000" + "00000000" + "00000000" + "00000000"
            + "5F00" + "000000000000"
            + "49000000" + "AF477141" + "3C000000" + "032B6941" + "2B000000" + "CBA15541"
            + "1E000000" + "6BBC3641" + "09000000" + "6BBC9C40" + "00000000" + "00000000"
            + new string('0', 2 * 4 * ClientSpecProfilePayloadCodec.TableRowLength)
            + "00");

        var request = ClientSpecProfilePayloadCodec.Decode(GameMessageMetadata.Empty, body);

        Assert.Equal(362, body.Length);
        Assert.Equal(0x00090000u, request.DirectXVersion);
        Assert.Equal(-1, request.StartLoadingSeconds);
        Assert.Equal(-1, request.ReturnVillageSeconds);

        // Row 0: no words, and counts paired with durations.
        Assert.All(request.Table[0].Words, word => Assert.Equal(0, word));
        Assert.Equal(5u, request.Table[0].Pairs[0].Integer);
        Assert.Equal(1.34f, request.Table[0].Pairs[0].Real, 2);

        // Row 1 is the one with a word set, which is what pins the stride against the header.
        Assert.Equal(0x5F, request.Table[1].Words[0]);
        Assert.Equal(73u, request.Table[1].Pairs[0].Integer);
        Assert.Equal(15.08f, request.Table[1].Pairs[0].Real, 2);
        Assert.Equal(9u, request.Table[1].Pairs[4].Integer);

        Assert.All(request.Table.Skip(2), row =>
        {
            Assert.All(row.Words, word => Assert.Equal(0, word));
            Assert.All(row.Pairs, pair => Assert.Equal(new ClientSpecProfilePair(0, 0f), pair));
        });
        Assert.Empty(request.Entries);
    }
}
