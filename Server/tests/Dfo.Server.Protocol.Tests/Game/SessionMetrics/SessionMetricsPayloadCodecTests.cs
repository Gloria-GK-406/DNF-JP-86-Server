using System.Buffers.Binary;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.SessionMetrics;

namespace Dfo.Server.Protocol.Tests.Game.SessionMetrics;

public sealed class SessionMetricsPayloadCodecTests
{
    /// <summary>The shape without values: every tag in place, every mean zero, both sections empty.</summary>
    private static byte[] ObservedBody()
    {
        var body = new byte[SessionMetricsPayloadCodec.MinimumBodyLength];
        for (var index = 0; index < SessionMetricsPayloadCodec.MeanCount; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                body.AsSpan(index * SessionMetricsPayloadCodec.MeanLength),
                SessionMetricsPayloadCodec.MeanTags[index]);
        }

        var sections = SessionMetricsPayloadCodec.MeanCount * SessionMetricsPayloadCodec.MeanLength;
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(sections), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(sections + 4), 8);
        return body;
    }

    /// <summary>The body the A21 client sent live, byte for byte.</summary>
    private static byte[] CapturedA21Body() =>
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x03, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x04, 0x00, 0x6E, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x09, 0x00, 0x16, 0xEE, 0x00, 0x00,
        0x07, 0x00, 0x00, 0x00,
        0x08, 0x00, 0x00, 0x00,
    ];

    /// <summary>
    /// The captured A21 body is 56 bytes, and the eighth mean carries the tag nine.
    /// </summary>
    /// <remarks>
    /// A12 sent 50 here: seven means and then the two counted section headers. A21 inserts an
    /// eighth mean before them, so a decoder reading A12's layout meets tag 9 where it expects
    /// tag 7 and throws -- which drops the connection on the way out of the game.
    /// </remarks>
    [Fact]
    public void TheCapturedA21BodyCarriesEightMeansEndingInTagNine()
    {
        var captured = CapturedA21Body();

        Assert.Equal(56, captured.Length);
        Assert.Equal(SessionMetricsPayloadCodec.MinimumBodyLength, captured.Length);
        Assert.Equal<ushort[]>([0, 1, 2, 3, 4, 5, 6, 9], [.. SessionMetricsPayloadCodec.MeanTags]);

        // The same shape once the capture's two measured values are cleared.
        var cleared = captured.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(cleared.AsSpan((4 * 6) + 2), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(cleared.AsSpan((7 * 6) + 2), 0);
        Assert.Equal(cleared, ObservedBody());
    }

    [Fact]
    public void Decode_ReadsTheCapturedA21Body()
    {
        var request = SessionMetricsPayloadCodec.Decode(
            GameMessageMetadata.Empty, CapturedA21Body());

        Assert.Equal<ushort[]>([0, 1, 2, 3, 4, 5, 6, 9],
            [.. request.Means.Select(mean => mean.Tag)]);
        Assert.Equal(366u, request.Means[4].Mean);
        Assert.Equal(60950u, request.Means[7].Mean);
        Assert.All(
            request.Means.Where((_, index) => index is not (4 or 7)),
            mean => Assert.Equal(0u, mean.Mean));
        Assert.Empty(request.Section7);
        Assert.Empty(request.Section8);
    }

    [Fact]
    public void Decode_InterpretsTheObservedBodyLosslessly()
    {
        var metadata = new GameMessageMetadata(0x01, 0x00FA, 0xF7BFEDE4, 9);

        var request = SessionMetricsPayloadCodec.Decode(metadata, ObservedBody());

        Assert.Equal(ClientMessageType.GameSessionMetrics, request.MessageType);
        Assert.Equal(metadata, request.Metadata);
        Assert.Equal(8, request.Means.Count);
        Assert.Equal<ushort[]>([0, 1, 2, 3, 4, 5, 6, 9],
            [.. request.Means.Select(mean => mean.Tag)]);
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
        var body = BuildBody(means: new uint[8], [record1], [record2]);

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
        // 56 + 9 * (count7 + count8)
        var body = BuildBody(new uint[8], NineByteRecords(2), NineByteRecords(3));

        Assert.Equal(56 + (9 * 5), body.Length);
        var request = SessionMetricsPayloadCodec.Decode(GameMessageMetadata.Empty, body);
        Assert.Equal(2, request.Section7.Count);
        Assert.Equal(3, request.Section8.Count);
    }

    [Fact]
    public void Decode_PreservesMeansIncludingLargeValues()
    {
        // The client computes each mean as total/samples truncated toward zero, so any u32 is
        // reachable and none may be rejected.
        var body = BuildBody([0, 1, uint.MaxValue, 42, 0, 999999, 7, 60950], [], []);

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
            () => SessionMetricsPayloadCodec.Decode(GameMessageMetadata.Empty, ObservedBody().AsSpan(0, 55)));
    }

    /// <summary>Both halves of the tag sequence are fixed, and both are enforced.</summary>
    [Theory]
    [InlineData(3, 9)]
    [InlineData(7, 7)]
    public void Decode_RejectsMeanTagsThatAreNotTheOnesTheClientWrites(int slot, ushort tag)
    {
        var body = ObservedBody();
        BinaryPrimitives.WriteUInt16LittleEndian(
            body.AsSpan(slot * SessionMetricsPayloadCodec.MeanLength), tag);

        Assert.Throws<InvalidDataException>(
            () => SessionMetricsPayloadCodec.Decode(GameMessageMetadata.Empty, body));
    }

    [Theory]
    [InlineData(48, 6)]   // section 7's tag
    [InlineData(52, 9)]   // section 8's tag
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
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(50), 3);

        Assert.Throws<InvalidDataException>(
            () => SessionMetricsPayloadCodec.Decode(GameMessageMetadata.Empty, body));
    }

    [Fact]
    public void Decode_RejectsTrailingBytesAfterTheLastSection()
    {
        var body = BuildBody(new uint[8], [], []);

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
            body.AddRange(BitConverter.GetBytes(SessionMetricsPayloadCodec.MeanTags[index]));
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
