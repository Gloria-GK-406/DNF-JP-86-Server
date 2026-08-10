using System.Buffers.Binary;
using Dfo.Server.Networking.Framing;
using TouchSocket.Core;

namespace Dfo.Server.Networking.Tests.Framing;

public sealed class FrameAdapterTests
{
    public static TheoryData<int> ChannelBufferLengths => new()
    {
        1,
        2,
        ChannelFrameRequestInfo.HeaderLength - 1,
        ChannelFrameRequestInfo.HeaderLength,
        ChannelFrameRequestInfo.HeaderLength + 1,
        17,
        31
    };

    [Theory]
    [MemberData(nameof(ChannelBufferLengths))]
    public async Task Channel_adapter_preserves_frames_across_partial_and_sticky_reads(int bufferLength)
    {
        var frame = BuildChannelFrame(new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50 });
        var received = new List<ChannelFrameRequestInfo>();
        var tester = SingleStreamDataAdapterTester.CreateTester(
            new ChannelFrameDataHandlingAdapter(),
            (_, requestInfo) =>
            {
                received.Add(Assert.IsType<ChannelFrameRequestInfo>(requestInfo));
                return Task.CompletedTask;
            });

        await tester.RunAsync(frame, testCount: 3, expectedCount: 3, bufferLength, millisecondsTimeout: 5_000);

        Assert.Equal(3, received.Count);
        Assert.All(received, request => Assert.Equal(new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50 }, request.Body.ToArray()));
    }

    [Fact]
    public async Task Game_client_adapter_handles_one_byte_at_a_time()
    {
        var frame = BuildGameClientFrame(new byte[] { 0xAA, 0xBB, 0xCC });
        GameClientFrameRequestInfo? received = null;
        var tester = SingleStreamDataAdapterTester.CreateTester(
            new GameClientFrameDataHandlingAdapter(),
            (_, requestInfo) =>
            {
                received = Assert.IsType<GameClientFrameRequestInfo>(requestInfo);
                return Task.CompletedTask;
            });

        await tester.RunAsync(frame, testCount: 1, expectedCount: 1, bufferLength: 1, millisecondsTimeout: 5_000);

        Assert.NotNull(received);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, received.Body.ToArray());
    }

    private static byte[] BuildChannelFrame(byte[] body)
    {
        var frame = new byte[ChannelFrameRequestInfo.HeaderLength + body.Length];
        frame[0] = 0x7C;
        frame[1] = 0x01;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(2), (uint)frame.Length);
        frame[10] = 0x01;
        body.CopyTo(frame, ChannelFrameRequestInfo.HeaderLength);
        return frame;
    }

    private static byte[] BuildGameClientFrame(byte[] body)
    {
        var frame = new byte[GameClientFrameRequestInfo.HeaderLength + body.Length];
        frame[0] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), 0x0100);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), (uint)frame.Length);
        body.CopyTo(frame, GameClientFrameRequestInfo.HeaderLength);
        return frame;
    }
}
