using System.Buffers.Binary;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;

namespace Dfo.Server.Networking.Tests.Framing;

public sealed class ClientSpecProfilePayloadCodecTests
{
    // Real captured body (protocol record E-001, 2026-08-14 Windows client): the 24-byte header
    // followed by an all-zero 336-byte table and an entry count of 0, i.e. 361 bytes.
    private static byte[] ObservedBody()
    {
        var body = new byte[ClientSpecProfilePayloadCodec.MinimumBodyLength];
        byte[] header =
        [
            0x33, 0x0A, 0x00, 0x00,   // +0  cpu clock 2611 MHz
            0x0C, 0x00,               // +4  12 logical processors
            0x86, 0x80,               // +6  GPU PCI vendor 0x8086
            0xA8, 0xA7,               // +8  GPU PCI device 0xA7A8
            0x00, 0x10,               // +10 normalized video memory 4096 MB
            0x00, 0x20,               // +12 normalized total memory 8192 MB
            0x0E,                     // +14 OS 14 (Windows 10/11)
            0x00, 0x00, 0x09, 0x00,   // +15 once-per-session value
            0x06,                     // +19
            0xFF, 0xFF,               // +20
            0xFF, 0xFF,               // +22
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
        Assert.Equal(2611u, request.CpuClockMhz);
        Assert.Equal(12, request.CpuLogicalCores);
        Assert.Equal(0x8086, request.GpuPciVendorId);
        Assert.Equal(0xA7A8, request.GpuPciDeviceId);
        Assert.Equal(4096, request.NormalizedVideoMemoryMegabytes);
        Assert.Equal(8192, request.NormalizedTotalMemoryMegabytes);
        Assert.Equal(0x0E, request.OperatingSystemKind);
        // 0x00090000 is DirectX 9.0.0; the client's own LagLog.txt prints it as hex "90000".
        Assert.Equal(0x00090000u, request.DirectXVersion);
        Assert.Equal(6, request.Unknown19);
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
            () => ClientSpecProfilePayloadCodec.Decode(GameMessageMetadata.Empty, ObservedBody().AsSpan(0, 360)));
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
        // 24-byte header + 6 rows x (4 u16 + 6 x (u32 + f32)) + 1 count byte = 361.
        Assert.Equal(56, ClientSpecProfilePayloadCodec.TableRowLength);
        Assert.Equal(336, ClientSpecProfilePayloadCodec.TableLength);
        Assert.Equal(361, ClientSpecProfilePayloadCodec.MinimumBodyLength);
    }
}
