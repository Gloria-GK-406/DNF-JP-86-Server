using System.Diagnostics;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Dfo.Server.Networking.Outbound;
using Microsoft.Extensions.Logging.Abstractions;
using static Dfo.Server.Networking.Tests.Outbound.Outbound;

namespace Dfo.Server.Networking.Tests.Outbound;

public sealed class OutboundSenderTests
{
    private readonly FakeTransport transport = new();
    private readonly RecordingObserver observer = new();

    [Fact]
    public async Task One_connection_receives_its_entries_in_submission_order()
    {
        var a = Context("a");
        var connection = transport.Add(a);
        connection.Gate = _ => Task.Delay(Random.Shared.Next(0, 3));
        await using var sender = Sender(transport, observer);

        var expected = new List<string>();
        for (var cmd = 0; cmd < 20; cmd++)
        {
            var entries = Enumerable.Range(0, 5).Select(i => Batch(a, $"{cmd}.{i}")).ToArray();
            expected.AddRange(entries.Select(entry => ((TextMessage)entry.Messages[0]).Text));
            sender.Submit(entries);
        }

        await observer.WaitForAsync(100);
        Assert.Equal(expected, connection.Sent);
        Assert.All(observer.Reports, report => Assert.Equal(OutboundOutcome.Delivered, report.Outcome));
    }

    [Fact]
    public async Task A_close_request_runs_after_the_frames_registered_before_it()
    {
        var a = Context("a");
        var connection = transport.Add(a);
        connection.Gate = _ => Task.Delay(20);
        await using var sender = Sender(transport, observer);
        var late = Batch(a, "late");

        sender.Submit([Batch(a, "one"), Batch(a, "two")]);
        sender.Submit([OutboundEntry.Close(a), late]);

        await observer.WaitForAsync(4);
        Assert.Equal(
            ["a:start one", "a:sent one", "a:start two", "a:sent two", "a:close"],
            transport.Events);
        Assert.Equal(OutboundOutcome.Discarded, observer.OutcomeOf(late));
        Assert.Empty(observer.Failures);
    }

    [Fact]
    public async Task An_ordered_entry_waits_for_a_slow_predecessor_within_the_limit()
    {
        var (follower, requester) = (Context("follower"), Context("requester"));
        transport.Add(follower).Gate = _ => Task.Delay(300);
        transport.Add(requester);
        await using var sender = Sender(transport, observer,
            OutboundSendOptions.Default with { OrderedWaitLimit = TimeSpan.FromSeconds(1) });
        var group = new OutboundOrderGroup("XO-1");

        sender.Submit([
            Batch(follower, "enter", OutboundDelivery.BestEffort, new OutboundOrder(group, 1)),
            Batch(requester, "enter", OutboundDelivery.Required, new OutboundOrder(group, 2)),
        ]);

        await observer.WaitForAsync(2);
        var events = transport.Events.ToList();
        Assert.True(events.IndexOf("follower:sent enter") < events.IndexOf("requester:start enter"),
            string.Join(", ", events));
        Assert.All(observer.Reports, report => Assert.Equal(OutboundOutcome.Delivered, report.Outcome));
        Assert.Empty(observer.Failures);
    }

    [Fact]
    public async Task A_predecessor_slower_than_the_limit_is_closed_and_the_group_goes_on()
    {
        var (follower, requester) = (Context("follower"), Context("requester"));
        var stuck = transport.Add(follower);
        stuck.Gate = NeverAsync;
        var target = transport.Add(requester);
        await using var sender = Sender(transport, observer, OutboundSendOptions.Default with
        {
            OrderedWaitLimit = TimeSpan.FromMilliseconds(300),
            DeliveryTimeout = TimeSpan.FromSeconds(30),
        });
        var group = new OutboundOrderGroup("XO-1");
        var slow = Batch(follower, "enter", OutboundDelivery.BestEffort, new OutboundOrder(group, 1));
        var after = Batch(follower, "after");
        var clock = Stopwatch.StartNew();

        sender.Submit([slow, Batch(requester, "enter", OutboundDelivery.Required, new OutboundOrder(group, 2)), after]);

        await observer.WaitForAsync(3);
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(250), clock.Elapsed.ToString());
        Assert.Equal(["enter"], target.Sent);
        Assert.Empty(stuck.Sent);
        await stuck.Closed.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(OutboundOutcome.TimedOut, observer.OutcomeOf(slow));
        Assert.Equal(OutboundOutcome.Discarded, observer.OutcomeOf(after));
        Assert.Equal((follower, OutboundOutcome.TimedOut), Assert.Single(observer.Failures));
    }

    public enum EarlyMiddle
    {
        ConnectionAlreadyFailed,
        OverLimitOnAdmission,
        DroppedWhileQueued,
    }

    /// <summary>
    /// A → B → C where B completes without ever waiting for A: the limit must still apply to A, the
    /// earliest unsettled entry, rather than leaving C to wait for A's own queue to drain.
    /// </summary>
    [Theory]
    [InlineData(EarlyMiddle.ConnectionAlreadyFailed)]
    [InlineData(EarlyMiddle.OverLimitOnAdmission)]
    [InlineData(EarlyMiddle.DroppedWhileQueued)]
    public async Task A_slow_entry_before_an_early_finished_middle_one_is_still_cut_at_the_limit(EarlyMiddle shape)
    {
        var (c1, c2, c3) = (Context("c1"), Context("c2"), Context("c3"));
        var slow = transport.Add(c1);
        slow.Gate = NeverAsync;
        var middle = transport.Add(c2);
        var last = transport.Add(c3);
        await using var sender = Sender(transport, observer, OutboundSendOptions.Default with
        {
            QueueByteLimit = 100,
            OrderedWaitLimit = TimeSpan.FromMilliseconds(300),
            DeliveryTimeout = TimeSpan.FromSeconds(30),
        });
        var expectedReports = 3;
        if (shape == EarlyMiddle.ConnectionAlreadyFailed)
        {
            middle.FailWith = new IOException("gone");
            sender.Submit([Batch(c2, "earlier")]);
            await observer.WaitForAsync(1);
            expectedReports++;
        }
        else if (shape == EarlyMiddle.DroppedWhileQueued)
        {
            middle.Gate = NeverAsync;
            sender.Submit([Batch(c2, "stuck")]);
            expectedReports += 2;
        }

        var group = new OutboundOrderGroup("A-B-C");
        var a = Batch(c1, "a", OutboundDelivery.BestEffort, new OutboundOrder(group, 1));
        var b = Batch(c2, shape == EarlyMiddle.OverLimitOnAdmission ? new string('b', 101) : "b",
            OutboundDelivery.BestEffort, new OutboundOrder(group, 2));
        var c = Batch(c3, "c", OutboundDelivery.Required, new OutboundOrder(group, 3));
        var clock = Stopwatch.StartNew();
        sender.Submit([a, b, c]);
        if (shape == EarlyMiddle.DroppedWhileQueued)
            sender.Submit([Batch(c2, new string('x', 100))]);

        await observer.WaitForAsync(expectedReports);
        Assert.Equal(OutboundOutcome.Delivered, observer.OutcomeOf(c));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), clock.Elapsed.ToString());
        Assert.NotEqual(OutboundOutcome.Delivered, observer.OutcomeOf(b));
        Assert.Equal(OutboundOutcome.TimedOut, observer.OutcomeOf(a));
        await slow.Closed.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains((c1, OutboundOutcome.TimedOut), observer.Failures);
        Assert.Equal(["c"], last.Sent);
    }

    [Fact]
    public async Task A_required_failure_stops_its_group_and_a_best_effort_failure_does_not()
    {
        var (owner, peer, party) = (Context("owner"), Context("peer"), Context("party"));
        var broken = transport.Add(owner);
        broken.FailWith = new IOException("socket gone");
        var peerConnection = transport.Add(peer);
        var partyConnection = transport.Add(party);
        await using var sender = Sender(transport, observer);
        var cleanup = new OutboundOrderGroup("XO-5");
        var ownerNotice = Batch(owner, "owner", OutboundDelivery.Required, new OutboundOrder(cleanup, 1));
        var peerNotice = Batch(peer, "peer", OutboundDelivery.BestEffort, new OutboundOrder(cleanup, 2));
        var partyNotice = Batch(party, "party", OutboundDelivery.BestEffort, new OutboundOrder(cleanup, 3));
        var ownerLater = Batch(owner, "owner later");

        sender.Submit([ownerNotice, peerNotice, partyNotice, ownerLater]);
        await observer.WaitForAsync(4);

        Assert.Equal(OutboundOutcome.Failed, observer.OutcomeOf(ownerNotice));
        Assert.Equal(OutboundOutcome.Skipped, observer.OutcomeOf(peerNotice));
        Assert.Equal(OutboundOutcome.Skipped, observer.OutcomeOf(partyNotice));
        Assert.Equal(OutboundOutcome.Discarded, observer.OutcomeOf(ownerLater));
        Assert.True(broken.IsClosed);
        Assert.False(peerConnection.IsClosed);
        Assert.False(partyConnection.IsClosed);
        Assert.Empty(peerConnection.Sent);

        var explicitDelivery = new OutboundOrderGroup("explicit");
        var (requester, other) = (Context("requester"), Context("other"));
        var requesterConnection = transport.Add(requester);
        transport.Add(other).FailWith = new IOException("optional recipient gone");
        var optional = Batch(other, "optional", OutboundDelivery.BestEffort, new OutboundOrder(explicitDelivery, 1));
        var direct = Batch(requester, "direct", OutboundDelivery.Required, new OutboundOrder(explicitDelivery, 2));

        sender.Submit([optional, direct]);
        await observer.WaitForAsync(6);

        Assert.Equal(OutboundOutcome.Failed, observer.OutcomeOf(optional));
        Assert.Equal(OutboundOutcome.Delivered, observer.OutcomeOf(direct));
        Assert.Equal(["direct"], requesterConnection.Sent);
    }

    [Fact]
    public async Task A_close_request_is_carried_out_even_when_its_group_broke()
    {
        var (owner, peer) = (Context("owner"), Context("peer"));
        transport.Add(owner).FailWith = new IOException("socket gone");
        var peerConnection = transport.Add(peer);
        await using var sender = Sender(transport, observer);
        var group = new OutboundOrderGroup("g");
        var close = OutboundEntry.Close(peer, new OutboundOrder(group, 2));

        sender.Submit([Batch(owner, "owner", OutboundDelivery.Required, new OutboundOrder(group, 1)), close]);
        await observer.WaitForAsync(2);

        Assert.Equal(OutboundOutcome.Closed, observer.OutcomeOf(close));
        Assert.True(peerConnection.IsClosed);
    }

    [Fact]
    public async Task A_batch_that_cannot_be_encoded_sends_none_of_it()
    {
        var a = Context("a");
        var connection = transport.Add(a);
        await using var sender = Sender(transport, observer);
        var whole = OutboundEntry.Batch(a, [new TextMessage("good"), new TextMessage("unencodable")]);
        var next = Batch(a, "next");

        sender.Submit([Batch(a, "before"), whole, next]);
        await observer.WaitForAsync(3);

        Assert.Equal(["before"], connection.Sent);
        var report = observer.Reports.Single(item => ReferenceEquals(item.Entry, whole));
        Assert.Equal(OutboundOutcome.EncodingFailed, report.Outcome);
        Assert.IsType<InvalidDataException>(report.Error);
        Assert.Equal(OutboundOutcome.Discarded, observer.OutcomeOf(next));
        Assert.True(connection.IsClosed);
    }

    [Fact]
    public async Task An_entry_for_a_replaced_connection_is_dropped_and_never_reaches_the_new_one()
    {
        var old = Context("a");
        transport.Add(old);
        transport.Remove(old);
        var replacement = Context("a");
        var current = transport.Add(replacement);
        await using var sender = Sender(transport, observer);
        var stale = Batch(old, "for the old connection");

        sender.Submit([stale, Batch(replacement, "for the new one")]);
        await observer.WaitForAsync(2);

        Assert.Equal(OutboundOutcome.Stale, observer.OutcomeOf(stale));
        Assert.Equal(["for the new one"], current.Sent);
        Assert.False(current.IsClosed);
        Assert.Empty(observer.Failures);
    }

    [Fact]
    public async Task A_delivery_that_exceeds_its_deadline_closes_the_connection()
    {
        var a = Context("a");
        var connection = transport.Add(a);
        connection.Gate = NeverAsync;
        await using var sender = Sender(transport, observer,
            OutboundSendOptions.Default with { DeliveryTimeout = TimeSpan.FromMilliseconds(200) });
        var first = Batch(a, "first");
        var second = Batch(a, "second");

        sender.Submit([first, second]);
        await observer.WaitForAsync(2);

        Assert.Equal(OutboundOutcome.TimedOut, observer.OutcomeOf(first));
        Assert.Equal(OutboundOutcome.Discarded, observer.OutcomeOf(second));
        await connection.Closed.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal((a, OutboundOutcome.TimedOut), Assert.Single(observer.Failures));
    }

    [Fact]
    public async Task A_send_that_ignores_its_cancellation_still_times_out()
    {
        var a = Context("a");
        var connection = transport.Add(a);
        var release = new TaskCompletionSource();
        connection.Gate = _ => release.Task;
        await using var sender = Sender(transport, observer,
            OutboundSendOptions.Default with { DeliveryTimeout = TimeSpan.FromMilliseconds(200) });
        var first = Batch(a, "first");

        sender.Submit([first]);
        await observer.WaitForAsync(1);
        release.SetResult();

        Assert.Equal(OutboundOutcome.TimedOut, observer.OutcomeOf(first));
        await connection.Closed.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Exceeding_the_queue_byte_limit_closes_the_connection()
    {
        var a = Context("a");
        var connection = transport.Add(a);
        connection.Gate = NeverAsync;
        await using var sender = Sender(transport, observer, OutboundSendOptions.Default with
        {
            QueueByteLimit = 100,
            DeliveryTimeout = TimeSpan.FromSeconds(30),
        });
        var inFlight = Batch(a, new string('x', 60));
        var overflow = Batch(a, new string('y', 41));

        sender.Submit([inFlight]);
        sender.Submit([overflow]);
        await observer.WaitForAsync(2);

        Assert.Equal(OutboundOutcome.OverLimit, observer.OutcomeOf(overflow));
        Assert.Equal(OutboundOutcome.Discarded, observer.OutcomeOf(inFlight));
        await connection.Closed.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal((a, OutboundOutcome.OverLimit), Assert.Single(observer.Failures));
    }

    [Fact]
    public async Task Up_to_the_limit_is_admitted()
    {
        var a = Context("a");
        var connection = transport.Add(a);
        var release = new TaskCompletionSource();
        connection.Gate = _ => release.Task;
        await using var sender = Sender(transport, observer, OutboundSendOptions.Default with { QueueByteLimit = 100 });

        sender.Submit([Batch(a, new string('x', 60)), Batch(a, new string('y', 40))]);
        release.SetResult();
        await observer.WaitForAsync(2);

        Assert.All(observer.Reports, report => Assert.Equal(OutboundOutcome.Delivered, report.Outcome));
    }

    [Fact]
    public async Task A_stuck_connection_neither_delays_others_nor_blocks_the_submitter()
    {
        var (slow, fast) = (Context("slow"), Context("fast"));
        var stuck = transport.Add(slow);
        var release = new TaskCompletionSource();
        stuck.Gate = _ => release.Task;
        var healthy = transport.Add(fast);
        await using var sender = Sender(transport, observer,
            OutboundSendOptions.Default with { DeliveryTimeout = TimeSpan.FromSeconds(30) });

        for (var i = 0; i < 50; i++)
            sender.Submit([Batch(slow, $"s{i}"), Batch(fast, $"f{i}")]);

        await observer.WaitForAsync(50);
        Assert.Equal(Enumerable.Range(0, 50).Select(i => $"f{i}"), healthy.Sent);
        Assert.Empty(stuck.Sent);
        Assert.False(release.Task.IsCompleted);
        release.SetResult();
        await observer.WaitForAsync(100);
        Assert.Equal(Enumerable.Range(0, 50).Select(i => $"s{i}"), stuck.Sent);
    }

    [Fact]
    public async Task Delivery_runs_off_the_executor_even_when_submitted_from_a_job()
    {
        var a = Context("a");
        var connection = transport.Add(a);
        await using var sender = Sender(transport, observer);
        await using var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);

        await executor.RunAsync(new GameplayWork(
            GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection),
            "SubmitFromJob",
            _ =>
            {
                Assert.True(GameplayJob.IsOnExecutor);
                sender.Submit([Batch(a, "from a job")]);
                return ValueTask.CompletedTask;
            }), CancellationToken.None);

        await observer.WaitForAsync(1);
        Assert.Equal([false], connection.SentOnExecutor);
    }

    [Fact]
    public async Task Sequences_of_an_ordered_group_must_increase()
    {
        var a = Context("a");
        var connection = transport.Add(a);
        await using var sender = Sender(transport, observer);
        var group = new OutboundOrderGroup("g");

        sender.Submit([Batch(a, "1", order: new OutboundOrder(group, 1))]);
        Assert.Throws<ArgumentException>(() => sender.Submit([
            Batch(a, "fine", order: new OutboundOrder(group, 5)),
            Batch(a, "repeat", order: new OutboundOrder(group, 5)),
        ]));
        Assert.Throws<ArgumentException>(() => sender.Submit([Batch(a, "0", order: new OutboundOrder(group, 0))]));
        Assert.Throws<ArgumentNullException>(() => sender.Submit([null!]));
        sender.Submit([Batch(a, "2", order: new OutboundOrder(group, 2))]);

        await observer.WaitForAsync(2);
        Assert.Equal(["1", "2"], connection.Sent);
    }

    [Fact]
    public async Task An_observer_that_throws_does_not_stop_delivery()
    {
        var a = Context("a");
        var connection = transport.Add(a);
        await using var sender = new OutboundSender(transport, new TextEncoder(),
            [new ThrowingObserver(), observer], NullLogger<OutboundSender>.Instance, OutboundSendOptions.Default);

        sender.Submit([Batch(a, "one"), Batch(a, "two")]);

        await observer.WaitForAsync(2);
        Assert.Equal(["one", "two"], connection.Sent);
    }

    private sealed class ThrowingObserver : IOutboundDeliveryObserver
    {
        public void OnEntryCompleted(OutboundDeliveryReport report) => throw new InvalidOperationException("observer");

        public void OnConnectionFailed(Dfo.Server.Data.Networking.NetworkConnectionContext connection, OutboundOutcome reason) =>
            throw new InvalidOperationException("observer");
    }
}
