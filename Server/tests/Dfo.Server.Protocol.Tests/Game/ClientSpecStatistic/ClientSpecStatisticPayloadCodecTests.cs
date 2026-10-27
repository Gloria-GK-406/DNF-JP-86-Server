using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.ClientSpecStatistic;

namespace Dfo.Server.Protocol.Tests.Game.ClientSpecStatistic;

[Trait("Category", "Unit")]
public sealed class ClientSpecStatisticPayloadCodecTests
{
    // Real captured body (protocol record E-001, 2026-08-13 Windows client):
    // trigger=0x80, reported=0, count=4,
    // slot0 CPU (12 logical processors, Intel bucket 0, clock bucket 3),
    // slot1 GPU (PCI 0x8086:0xA7A8), slot2 OS (14 = Windows 10/11, SP 0),
    // slot3 memory (v1=0, bucket 7).
    private static readonly byte[] ObservedBody =
    [
        0x80, 0x00, 0x04,
        0x00, 0x0C, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00,
        0x01, 0x86, 0x80, 0x00, 0x00, 0xA8, 0xA7, 0x00, 0x00,
        0x02, 0x0E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x03, 0x00, 0x00, 0x00, 0x00, 0x07, 0x00, 0x00, 0x00,
    ];

    [Fact]
    public void Decode_InterpretsTheObservedBodyLosslessly()
    {
        var metadata = new GameMessageMetadata(0x01, 0x00AB, 0xDE638E7A, 1);

        var request = ClientSpecStatisticPayloadCodec.Decode(metadata, ObservedBody);

        Assert.Equal(ClientMessageType.GameClientSpecStatistic, request.MessageType);
        Assert.Equal(0x80, request.TriggerPath);
        Assert.Equal(0, request.ReportedFlag);
        Assert.Equal(4, request.Slots.Count);
        Assert.Equal(new ClientSpecStatisticSlot(0, 12, 3), request.Slots[0]);
        Assert.Equal(new ClientSpecStatisticSlot(1, 0x8086, 0xA7A8), request.Slots[1]);
        Assert.Equal(new ClientSpecStatisticSlot(2, 14, 0), request.Slots[2]);
        Assert.Equal(new ClientSpecStatisticSlot(3, 0, 7), request.Slots[3]);
    }

    [Fact]
    public void Decode_DecodesVendorBucketEncodingIntoRawSlotValue()
    {
        // 6 cores with the AMD vendor bucket (1 * 0x1000000).
        var body = (byte[])ObservedBody.Clone();
        body[4] = 0x06;
        body[7] = 0x01;

        var request = ClientSpecStatisticPayloadCodec.Decode(GameMessageMetadata.Empty, body);

        Assert.Equal(0x0100_0006u, request.Slots[0].Value1);
    }

    [Fact]
    public void Decode_RejectsBodiesThatAreNotThirtyNineBytes()
    {
        Assert.Throws<InvalidDataException>(
            () => ClientSpecStatisticPayloadCodec.Decode(GameMessageMetadata.Empty, ObservedBody.AsSpan(0, 38)));
        Assert.Throws<InvalidDataException>(
            () => ClientSpecStatisticPayloadCodec.Decode(GameMessageMetadata.Empty, [.. ObservedBody, 0x00]));
    }

    [Fact]
    public void Decode_RejectsASlotCountOtherThanFour()
    {
        var body = (byte[])ObservedBody.Clone();
        body[2] = 0x03;

        Assert.Throws<InvalidDataException>(
            () => ClientSpecStatisticPayloadCodec.Decode(GameMessageMetadata.Empty, body));
    }

    [Fact]
    public void Decode_RejectsNonSequentialSlotTags()
    {
        var body = (byte[])ObservedBody.Clone();
        body[12] = 0x05;

        Assert.Throws<InvalidDataException>(
            () => ClientSpecStatisticPayloadCodec.Decode(GameMessageMetadata.Empty, body));
    }
}
