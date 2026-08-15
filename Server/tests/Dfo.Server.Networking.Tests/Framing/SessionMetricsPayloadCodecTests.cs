using System.Buffers.Binary;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;

namespace Dfo.Server.Networking.Tests.Framing;

public sealed class SessionMetricsPayloadCodecTests
{
    // Real captured body (protocol record E-001): seven means, all zero because the session never
    // entered the game, then both counted sections empty.
    private static byte[] ObservedBody()
    {
        var body = new byte[SessionMetricsPayloadCodec.MinimumBodyLength];
        for (var index = 0; index < SessionMetricsPayloadCodec.MeanCount; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                body.AsSpan(index * SessionMetricsPayloadCodec.MeanLength), (ushort)index);
        }

        var sections = SessionMetricsPayloadCodec.MeanCount * SessionMetricsPayloadCodec.MeanLength;
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(sections), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(sections + 4), 8);
        return body;
    }

    [Fact]
    public void ObservedBodyMatchesTheCapturedBytes()
    {
        // Guards the builder above against drift: this is the exact 50-byte body seen in all four
        // sessions, written out byte by byte.
        byte[] captured =
        [
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x03, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x04, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x05, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x06, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x07, 0x00, 0x00, 0x00,
            0x08, 0x00, 0x00, 0x00,
        ];

        Assert.Equal(50, captured.Length);
        Assert.Equal(captured, ObservedBody());
    }

    [Fact]
    public void Decode_InterpretsTheObservedBodyLosslessly()
    {
        var metadata = new GameMessageMetadata(0x01, 0x00FA, 0xF7BFEDE4, 9);

        var request = SessionMetricsPayloadCodec.Decode(metadata, ObservedBody());

        Assert.Equal(ClientMessageType.GameSessionMetrics, request.MessageType);
        Assert.Equal(metadata, request.Metadata);
        Assert.Equal(7, request.Means.Count);
        Assert.Equal(Enumerable.Range(0, 7), request.Means.Select(mean => (int)mean.Tag));
        Assert.All(request.Means, mean => Assert.Equal(0u, mean.Mean));
        Assert.Empty(request.Section7);
        Assert.Empty(request.Section8);
    }

    [Fact]
    public void Decode_ReadsPopulatedCountedSections()
    {
        // One record in each section, with a distinct value per field so a shifted offset or a
        // swapped pair fails loudly. Note the record is 9 bytes on the wire, not 10.
        byte[] record1 = [0x34, 0x12, 0x07, 0x78, 0x56, 0xBC, 0x9A, 0xF0, 0xDE];
        byte[] record2 = [0x01, 0x00, 0x02, 0x03, 0x00, 0x04, 0x00, 0x05, 0x00];
        var body = BuildBody(means: new uint[7], [record1], [record2]);

        var request = SessionMetricsPayloadCodec.Decode(GameMessageMetadata.Empty, body);

        var first = Assert.Single(request.Section7);
        Assert.Equal(0x1234, first.Word0);
        Assert.Equal(0x07, first.Byte2);
        Assert.Equal(0x5678, first.Word3);
        Assert.Equal(0x9ABC, first.Word5);
        Assert.Equal(0xDEF0, first.Word7);

        var second = Assert.Single(request.Section8);
        Assert.Equal(1, second.Word0);
        Assert.Equal(2, second.Byte2);
        Assert.Equal(3, second.Word3);
        Assert.Equal(4, second.Word5);
        Assert.Equal(5, second.Word7);
    }

    [Fact]
    public void Decode_AcceptsTheDocumentedLengthFormula()
    {
        // 50 + 9 * (count7 + count8)
        var body = BuildBody(new uint[7], NineByteRecords(2), NineByteRecords(3));

        Assert.Equal(50 + (9 * 5), body.Length);
        var request = SessionMetricsPayloadCodec.Decode(GameMessageMetadata.Empty, body);
        Assert.Equal(2, request.Section7.Count);
        Assert.Equal(3, request.Section8.Count);
    }

    [Fact]
    public void Decode_PreservesMeansIncludingLargeValues()
    {
        // The client computes each mean as total/samples truncated toward zero, so any u32 is
        // reachable and none may be rejected.
        var body = BuildBody([0, 1, uint.MaxValue, 42, 0, 999999, 7], [], []);

        var request = SessionMetricsPayloadCodec.Decode(GameMessageMetadata.Empty, body);

        Assert.Equal(uint.MaxValue, request.Means[2].Mean);
        Assert.Equal(999999u, request.Means[5].Mean);
    }

    [Fact]
    public void Decode_RejectsBodiesShorterThanTheFixedPart()
    {
        Assert.Throws<InvalidDataException>(
            () => SessionMetricsPayloadCodec.Decode(GameMessageMetadata.Empty, []));
        Assert.Throws<InvalidDataException>(
            () => SessionMetricsPayloadCodec.Decode(GameMessageMetadata.Empty, ObservedBody().AsSpan(0, 49)));
    }

    [Fact]
    public void Decode_RejectsMeanTagsThatAreNotTheirOwnIndex()
    {
        var body = ObservedBody();
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(3 * SessionMetricsPayloadCodec.MeanLength), 9);

        Assert.Throws<InvalidDataException>(
            () => SessionMetricsPayloadCodec.Decode(GameMessageMetadata.Empty, body));
    }

    [Theory]
    [InlineData(42, 6)]   // section 7's tag
    [InlineData(46, 9)]   // section 8's tag
    public void Decode_RejectsWrongCountedSectionTags(int offset, ushort tag)
    {
        var body = ObservedBody();
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset), tag);

        Assert.Throws<InvalidDataException>(
            () => SessionMetricsPayloadCodec.Decode(GameMessageMetadata.Empty, body));
    }

    [Fact]
    public void Decode_RejectsACountThatOverrunsTheBody()
    {
        var body = ObservedBody();
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(44), 3);

        Assert.Throws<InvalidDataException>(
            () => SessionMetricsPayloadCodec.Decode(GameMessageMetadata.Empty, body));
    }

    [Fact]
    public void Decode_RejectsTrailingBytesAfterTheLastSection()
    {
        var body = BuildBody(new uint[7], [], []);

        Assert.Throws<InvalidDataException>(
            () => SessionMetricsPayloadCodec.Decode(GameMessageMetadata.Empty, [.. body, 0x00]));
    }

    private static byte[][] NineByteRecords(int count) =>
        [.. Enumerable.Range(0, count).Select(index => new byte[9].Select((_, i) => (byte)(index + i)).ToArray())];

    private static byte[] BuildBody(uint[] means, byte[][] section7, byte[][] section8)
    {
        var body = new List<byte>();
        for (var index = 0; index < SessionMetricsPayloadCodec.MeanCount; index++)
        {
            body.AddRange([(byte)index, 0x00]);
            body.AddRange(BitConverter.GetBytes(means[index]));
        }

        AppendSection(body, SessionMetricsPayloadCodec.FirstCountedSectionTag, section7);
        AppendSection(body, SessionMetricsPayloadCodec.SecondCountedSectionTag, section8);
        return [.. body];
    }

    private static void AppendSection(List<byte> body, ushort tag, byte[][] records)
    {
        body.AddRange(BitConverter.GetBytes(tag));
        body.AddRange(BitConverter.GetBytes((ushort)records.Length));
        foreach (var record in records)
        {
            body.AddRange(record);
        }
    }
}
