using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Networking.Outbound;

namespace Dfo.Server.Networking.Tests.Outbound;

/// <summary>
/// RQ-036 / AC-013: the queue byte limit is at least twice the largest outbox one CMD registers for
/// one connection. That maximum is a SELECT_CHARACTER with every growing list at its capacity bound;
/// the rows are the per-frame-type inputs of the derivation, whose sources are listed row by row in
/// the serial-gameplay-executor task's verification/measurement/selection-outbound-bound.md.
/// </summary>
public sealed class OutboundQueueLimitTests
{
    private const int GameFrameHeader = 15;

    // (frame type, measured body bytes on a new character, maximum body bytes, measured frames, maximum frames)
    private static readonly (string Type, long MeasuredBody, long MaximumBody, int MeasuredFrames, int MaximumFrames)[] Selection =
    [
        ("CMD0004", 270, 2557, 1, 1),
        ("0002", 835, 19012, 4, 4),
        ("0166", 175, 13175, 5, 5),
        ("0167", 16, 3040, 1, 1),
        ("0015", 50, 13770, 2, 2),
        ("0164", 30004, 30004, 1, 1),
        ("0013", 70, 2050, 1, 1),
        ("0069", 1, 4231, 1, 1),
        ("0003", 4, 4, 1, 1),
        ("00CA", 1, 1, 1, 1),
        ("0005", 2, 5237, 1, 1),
        ("000D", 1545, 120763, 6, 6),
        ("0465", 5, 5685, 1, 1),
        ("021E", 2, 1814, 2, 4),
        ("0286", 22, 22, 1, 1),
        ("00AD", 162, 762, 1, 1),
        ("01C7", 5, 65541, 1, 1),
        ("006C", 808, 808, 1, 1),
        ("042D", 450, 450, 1, 1),
        ("033F", 112, 112, 7, 7),
        ("04B8", 3, 3, 1, 1),
        ("04BA", 46, 46, 1, 1),
        ("04B9", 90, 90, 1, 1),
        ("0187", 4, 516, 1, 1),
        ("01B9", 1, 129, 1, 1),
        ("015F", 1, 1, 1, 1),
        ("00AC", 1, 66706, 1, 34),
        ("00AE", 1, 1, 1, 1),
        ("017B", 11, 11, 1, 1),
        ("0381", 16, 16, 2, 2),
        ("021F", 4, 4, 1, 1),
        ("03C1", 8, 2184, 1, 1),
        ("00FB", 4, 4, 2, 2),
        ("00CD", 2, 12, 1, 1),
        ("00B1", 1, 1, 1, 1),
        ("036A", 2, 2, 1, 1),
        ("03D8", 204, 204, 1, 1),
        ("03D0", 15, 15, 1, 1),
        ("03EB", 32, 248, 4, 4),
        ("03DF", 7, 7, 1, 1),
        ("025B", 9, 9, 1, 1),
        ("0331", 1, 1, 1, 1),
        ("01EB", 3, 3, 1, 1),
        ("0061", 6, 53596, 1, 1),
        ("0158", 1, 1, 1, 1),
        ("02D5", 48, 48, 3, 3),
        ("01A8", 1, 1, 1, 1),
        ("0009", 2, 2, 1, 1),
        ("0344", 4, 508, 1, 1),
        ("007C", 8, 8, 1, 1),
        ("CMD01BA", 7, 7, 1, 1),
        ("0035", 13, 13, 1, 1),
        ("0111", 8, 8, 1, 1),
        ("0016", 9, 9, 1, 1),
        ("000E", 609, 36363, 1, 1),
        ("0165", 3, 3, 1, 1),
        ("CMD0239", 5, 5, 1, 1),
        ("0289", 8, 8, 1, 1),
        ("0021", 1, 1, 1, 1),
        ("CMD036F", 77, 77, 1, 1),
        ("0326", 8, 8, 1, 1),
        ("0279", 8, 8, 1, 1),
        ("0227", 525, 525, 105, 105),
        ("042B", 4, 4, 1, 1),
        ("opt0245", 0, 20, 0, 1),
        ("opt023F", 0, 184, 0, 1),
        ("opt006B0067", 0, 10, 0, 2),
        ("optSAO", 0, 8, 0, 1),
        ("optCleanup", 0, 4, 0, 2),
    ];

    [Fact]
    public void The_selection_bound_is_the_sum_of_its_inputs()
    {
        Assert.Equal(69, Selection.Length);
        Assert.Equal(450_680, Selection.Sum(row => row.MaximumBody));
        Assert.Equal(237, Selection.Sum(row => row.MaximumFrames));
        Assert.Equal(450_680 + 237 * GameFrameHeader, OutboundLimits.SelectionOutboundBound);
        Assert.All(Selection, row =>
        {
            Assert.True(row.MaximumBody >= row.MeasuredBody, row.Type);
            Assert.True(row.MaximumFrames >= row.MeasuredFrames, row.Type);
        });
    }

    [Fact]
    public void The_same_rows_reproduce_the_measured_selection()
    {
        // TASK-03: 39,285 B in 195 frames on a new character, the fence reply excluded.
        Assert.Equal(195, Selection.Sum(row => row.MeasuredFrames));
        Assert.Equal(39_285, Selection.Sum(row => row.MeasuredBody) + 195 * GameFrameHeader);
    }

    [Fact]
    public void The_queue_limit_holds_twice_the_largest_selection()
    {
        Assert.True(LimitCoversTwice(OutboundLimits.QueueByteLimit, OutboundLimits.SelectionOutboundBound));
        Assert.Equal(OutboundLimits.QueueByteLimit, OutboundSendOptions.Default.QueueByteLimit);
    }

    [Fact]
    public void The_criterion_rejects_a_limit_below_twice_the_bound()
    {
        Assert.False(LimitCoversTwice(2 * OutboundLimits.SelectionOutboundBound - 1, OutboundLimits.SelectionOutboundBound));
        Assert.False(LimitCoversTwice(OutboundLimits.QueueByteLimit, OutboundLimits.QueueByteLimit / 2 + 1));
        Assert.True(LimitCoversTwice(2 * OutboundLimits.SelectionOutboundBound, OutboundLimits.SelectionOutboundBound));
    }

    [Fact]
    public async Task Two_largest_selections_queued_back_to_back_do_not_trip_the_limit()
    {
        var transport = new FakeTransport();
        var observer = new RecordingObserver();
        var a = Outbound.Context("a");
        var connection = transport.Add(a);
        var release = new TaskCompletionSource();
        connection.Gate = _ => release.Task;
        await using var sender = Outbound.Sender(transport, observer,
            OutboundSendOptions.Default with { DeliveryTimeout = TimeSpan.FromSeconds(30) });
        var selection = new string('s', checked((int)OutboundLimits.SelectionOutboundBound));

        sender.Submit([Outbound.Batch(a, selection)]);
        sender.Submit([Outbound.Batch(a, selection)]);
        await Task.Delay(100);
        Assert.Empty(observer.Reports);
        release.SetResult();

        await observer.WaitForAsync(2);
        Assert.All(observer.Reports, report => Assert.Equal(OutboundOutcome.Delivered, report.Outcome));
    }

    private static bool LimitCoversTwice(long limit, long largestOutbox) => limit >= 2 * largestOutbox;
}
