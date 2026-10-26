using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Execution.Outbox;

public sealed class GameplayOutboxTests : IAsyncDisposable
{
    private static readonly NetworkConnectionContext Source = Connection("source");
    private static readonly NetworkConnectionContext Peer = Connection("peer");
    private static readonly NetworkConnectionContext Third = Connection("third");

    private readonly (GameplayExecutor Executor, RecordingExecutionLogger<GameplayExecutor> Log) execution = ExecutionTest.Executor();
    private GameplayExecutor executor => execution.Executor;
    private readonly RecordingOutboundSender sender = new();
    private readonly RecordingExecutionLogger<GameplayOutbox> log = new();
    private readonly GameplayOutboxes outboxes;

    public GameplayOutboxTests() => outboxes = new GameplayOutboxes(sender, log);

    public ValueTask DisposeAsync() => executor.DisposeAsync();

    [Fact]
    public async Task Only_the_committed_unit_is_flushed_when_the_second_rolls_back()
    {
        await RunCmdAsync(async ct =>
        {
            var outbox = outboxes.Current;
            using (var first = outbox.BeginUnit())
            {
                outbox.Register(Batch(Source, "first"));
                await first.CommitAsync(ct);
            }

            using var second = outbox.BeginUnit();
            outbox.Register(Batch(Source, "second"));
            second.Rollback();
        });

        Assert.Equal(["source:first"], sender.Flushed());
        Assert.Single(sender.Submissions);
    }

    [Fact]
    public async Task A_failing_cmd_flushes_committed_and_unscoped_entries_in_order_and_then_closes_its_source()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => RunCmdAsync(async ct =>
        {
            var outbox = outboxes.Current;
            using (var committed = outbox.BeginUnit())
            {
                outbox.Register(Batch(Source, "committed"));
                await committed.CommitAsync(ct);
            }

            outbox.Register(Batch(Peer, "unscoped"));
            using (var rolledBack = outbox.BeginUnit())
            {
                outbox.Register(Batch(Source, "rolled back"));
                rolledBack.Rollback();
            }

            outbox.RequestClose(Third);
            throw new InvalidOperationException("cmd failed");
        }, Source).AsTask());

        Assert.Equal("cmd failed", failure.Message);
        Assert.Equal(["source:committed", "peer:unscoped", "third:close", "source:close"], sender.Flushed());
    }

    [Fact]
    public async Task A_cmd_that_completes_normally_does_not_close_its_source()
    {
        await RunCmdAsync(_ =>
        {
            outboxes.Current.Register(Batch(Source, "reply"));
            return ValueTask.CompletedTask;
        }, Source);

        Assert.Equal(["source:reply"], sender.Flushed());
    }

    [Fact]
    public async Task A_recipient_whose_composition_fails_loses_only_his_entries()
    {
        OutboxComposition<NetworkConnectionContext>? composition = null;
        IReadOnlyList<OutboxRecipientResult<NetworkConnectionContext>>? seenAfterCommit = null;
        await RunCmdAsync(async ct =>
        {
            var outbox = outboxes.Current;
            using var unit = outbox.BeginUnit();
            composition = await outbox.ComposeAsync<NetworkConnectionContext>(
                [Source, Peer, Third],
                (recipient, _) => recipient == Peer
                    ? throw new InvalidDataException("peer cannot be composed")
                    : ValueTask.FromResult<IReadOnlyList<OutboundEntry>>(
                        [Batch(recipient, "a"), Batch(recipient, "b")]),
                ct);
            Assert.False(composition.IsComposed);
            await unit.CommitAsync(ct);
            seenAfterCommit = composition.Results;
        });

        Assert.Equal(["source:a", "source:b", "third:a", "third:b"], sender.Flushed());
        Assert.DoesNotContain("peer:close", sender.Flushed());
        Assert.NotNull(seenAfterCommit);
        Assert.Collection(seenAfterCommit!,
            result => Assert.True(result.Composed),
            result =>
            {
                Assert.Same(Peer, result.Recipient);
                Assert.False(result.Composed);
                Assert.IsType<InvalidDataException>(result.Failure);
                Assert.Empty(result.Entries);
            },
            result => Assert.True(result.Composed));
        var warning = Assert.Single(log.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.IsType<InvalidDataException>(warning.Exception);
        Assert.Contains("peer", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Composition_reads_the_state_as_committed_not_later_changes()
    {
        var state = "committed state";
        await RunCmdAsync(async ct =>
        {
            var outbox = outboxes.Current;
            using (var unit = outbox.BeginUnit())
            {
                await outbox.ComposeAsync<NetworkConnectionContext>(
                    [Source],
                    (recipient, _) => ValueTask.FromResult<IReadOnlyList<OutboundEntry>>([Batch(recipient, state)]),
                    ct);
                state = "still inside the unit";
                await unit.CommitAsync(ct);
            }

            state = "a later stage";
        });

        Assert.Equal(["source:still inside the unit"], sender.Flushed());
    }

    [Fact]
    public async Task Outside_a_unit_composition_happens_at_registration()
    {
        var state = "at registration";
        await RunCmdAsync(async ct =>
        {
            var composition = await outboxes.Current.ComposeAsync<NetworkConnectionContext>(
                [Peer],
                (recipient, _) => ValueTask.FromResult<IReadOnlyList<OutboundEntry>>([Batch(recipient, state)]),
                ct);
            Assert.True(composition.IsComposed);
            state = "afterwards";
        });

        Assert.Equal(["peer:at registration"], sender.Flushed());
    }

    [Fact]
    public async Task Entries_of_an_uncertain_or_unfinished_unit_are_dropped()
    {
        var composed = 0;
        await RunCmdAsync(async ct =>
        {
            var outbox = outboxes.Current;
            using (var uncertain = outbox.BeginUnit())
            {
                outbox.Register(Batch(Source, "uncertain"));
                await outbox.ComposeAsync<NetworkConnectionContext>([Peer], (recipient, _) =>
                {
                    composed++;
                    return ValueTask.FromResult<IReadOnlyList<OutboundEntry>>([Batch(recipient, "x")]);
                }, ct);
                uncertain.MarkUncertain();
            }

            outbox.Register(Batch(Source, "kept"));
            _ = outbox.BeginUnit();
            outbox.Register(Batch(Source, "never committed"));
        });

        Assert.Equal(["source:kept"], sender.Flushed());
        Assert.Equal(0, composed);
        Assert.Contains(log.Entries, entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains("still open", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_close_request_inside_a_unit_survives_its_rollback()
    {
        await RunCmdAsync(_ =>
        {
            var outbox = outboxes.Current;
            using var unit = outbox.BeginUnit();
            outbox.Register(Batch(Peer, "rolled back"));
            outbox.RequestClose(Peer);
            unit.Rollback();
            return ValueTask.CompletedTask;
        });

        Assert.Equal(["peer:close"], sender.Flushed());
    }

    [Fact]
    public async Task Registering_outside_a_job_is_a_program_error()
    {
        var seen = ViolationCapture.Attach();
        Assert.Throws<GameplayOffExecutorException>(() => outboxes.Current);
        var violation = Assert.Single(seen);
        Assert.Equal(GameplayWriteAssertion.OffExecutorKind, violation.Kind);
        Assert.Equal("Writing GameplayOutbox.Register", violation.Operation);

        GameplayOutbox? kept = null;
        await RunCmdAsync(_ =>
        {
            kept = outboxes.Current;
            return ValueTask.CompletedTask;
        });
        Assert.Throws<GameplayOffExecutorException>(() => kept!.Register(Batch(Source, "late")));

        await RunCmdAsync(_ =>
        {
            GameplayJob.RunDetached(() =>
                Assert.Throws<GameplayOffExecutorException>(() => outboxes.Current));
            return ValueTask.CompletedTask;
        });
        Assert.Empty(sender.Flushed());
    }

    [Fact]
    public async Task The_outbox_of_a_finished_job_refuses_more_work_even_on_another_job()
    {
        GameplayOutbox? first = null;
        await RunCmdAsync(_ =>
        {
            first = outboxes.Current;
            return ValueTask.CompletedTask;
        });

        await executor.RunAsync(ExecutionTest.Work(_ =>
        {
            Assert.NotSame(first, outboxes.Current);
            Assert.Throws<InvalidOperationException>(() => first!.Register(Batch(Source, "late")));
            return ValueTask.CompletedTask;
        }), CancellationToken.None);
    }

    [Fact]
    public async Task Units_are_sequential_and_end_once()
    {
        await RunCmdAsync(async ct =>
        {
            var outbox = outboxes.Current;
            using var unit = outbox.BeginUnit();
            Assert.Throws<InvalidOperationException>(() => outbox.BeginUnit());
            await unit.CommitAsync(ct);
            Assert.Throws<InvalidOperationException>(() => unit.Rollback());
            await Assert.ThrowsAsync<InvalidOperationException>(() => unit.CommitAsync(ct).AsTask());
            using var next = outbox.BeginUnit();
            next.Rollback();
        });
    }

    [Fact]
    public async Task One_job_shares_one_outbox_and_flushes_once()
    {
        await RunCmdAsync(_ =>
        {
            Assert.Same(outboxes.Current, outboxes.Current);
            outboxes.Current.Register(Batch(Source, "one"));
            outboxes.Current.Register(Batch(Peer, "two"));
            return ValueTask.CompletedTask;
        });

        var submission = Assert.Single(sender.Submissions);
        Assert.Equal(2, submission.Length);
    }

    [Fact]
    public async Task A_failed_flush_of_a_failing_cmd_still_closes_its_connections()
    {
        var refusing = new RefusingOnceSender();
        var boxes = new GameplayOutboxes(refusing, log);

        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => executor.RunAsync(Cmd(_ =>
            {
                boxes.Current.Register(Batch(Peer, "lost"));
                boxes.Current.RequestClose(Peer);
                throw new InvalidDataException("cmd failed");
            }, Source), CancellationToken.None).AsTask());

        Assert.Equal("cmd failed", failure.Message);
        Assert.Equal(["peer:close", "source:close"], refusing.Accepted.Flushed());
        Assert.Contains(log.Entries, entry => entry.Level == LogLevel.Error && entry.Exception is ArgumentException);
        Assert.Same(failure,Assert.Single(execution.Log.Entries,entry=>entry.Level==LogLevel.Error).Exception);
    }

    [Fact]
    public async Task A_failed_flush_of_a_normal_cmd_closes_its_source_and_fails_the_cmd()
    {
        var refusing = new RefusingOnceSender();
        var boxes = new GameplayOutboxes(refusing, log);

        var failure=await Assert.ThrowsAsync<ArgumentException>(() => executor.RunAsync(Cmd(_ =>
            {
                boxes.Current.Register(Batch(Peer, "lost"));
                boxes.Current.Register(OutboundEntry.Close(Third, new OutboundOrder(new OutboundOrderGroup("g"), 1)));
                return ValueTask.CompletedTask;
            }, Source), CancellationToken.None).AsTask());

        var closes = Assert.Single(refusing.Accepted.Submissions);
        Assert.Equal(["third:close", "source:close"], refusing.Accepted.Flushed());
        Assert.All(closes, entry => Assert.Null(entry.Order));
        var warning=Assert.Single(log.Entries,entry=>entry.Level==LogLevel.Warning);Assert.Null(warning.Exception);
        Assert.DoesNotContain(log.Entries,entry=>entry.Level==LogLevel.Error);
        Assert.Same(failure,Assert.Single(execution.Log.Entries,entry=>entry.Level==LogLevel.Error).Exception);
    }

    /// <summary>The body as one CMD on the executor; its outbox is flushed when the job ends.</summary>
    private ValueTask RunCmdAsync(Func<CancellationToken, ValueTask> body, NetworkConnectionContext? source = null) =>
        executor.RunAsync(Cmd(body, source), CancellationToken.None);

    private static GameplayWork Cmd(Func<CancellationToken, ValueTask> body, NetworkConnectionContext? source) =>
        new(GameplayShardKey.World, ExecutionTest.Root(), "TestCommand", body, source);

    private static NetworkConnectionContext Connection(string name) =>
        new(new NetworkConnectionId(name), NetworkEndpointKind.Game, 10011);

    private static OutboundEntry Batch(NetworkConnectionContext connection, string text) =>
        OutboundEntry.Batch(connection, [new TestMessage(text)], System.Text.Encoding.UTF8.GetBytes(text));

    private sealed record TestMessage(string Text) : IServerNetworkMessage
    {
        public ServerMessageType MessageType => default;
    }

    /// <summary>Refuses the first submission, as a sender rejecting a program error would.</summary>
    private sealed class RefusingOnceSender : IOutboundSender
    {
        private int calls;

        public RecordingOutboundSender Accepted { get; } = new();

        public void Submit(IReadOnlyList<OutboundEntry> entries)
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new ArgumentException("refused submission");
            Accepted.Submit(entries);
        }
    }

    private sealed class RecordingOutboundSender : IOutboundSender
    {
        public ConcurrentQueue<OutboundEntry[]> Submissions { get; } = new();

        public void Submit(IReadOnlyList<OutboundEntry> entries) => Submissions.Enqueue([.. entries]);

        public IReadOnlyList<string> Flushed() =>
        [
            .. Submissions.SelectMany(batch => batch).Select(entry => entry.IsClose
                ? $"{entry.Connection.ConnectionId}:close"
                : $"{entry.Connection.ConnectionId}:{string.Join("+", entry.Messages.Cast<TestMessage>().Select(m => m.Text))}"),
        ];
    }
}
