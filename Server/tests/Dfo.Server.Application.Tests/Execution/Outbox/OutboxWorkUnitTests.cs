using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Tests.Execution.Outbox;

/// <summary>
/// AC-011 items 1 and 5, plan "组装点": a writable domain transaction on a job owns that job's outbox
/// work unit. What is registered while it is open goes out only if it commits, compositions see the
/// committed state, an uncertain outcome drops them, and a read-only transaction opens no unit.
/// </summary>
public sealed class OutboxWorkUnitTests : IAsyncDisposable
{
    private static readonly NetworkConnectionContext Source = new(new NetworkConnectionId("source"), NetworkEndpointKind.Game, 10011);

    private readonly GameplayExecutor executor = ExecutionTest.Executor().Executor;
    private readonly Recorder sender = new();
    private readonly GameplayOutboxes outboxes;
    private readonly DomainTransactionScope scope;

    public OutboxWorkUnitTests()
    {
        outboxes = new GameplayOutboxes(sender, new RecordingExecutionLogger<GameplayOutbox>());
        scope = new DomainTransactionScope(outboxes: outboxes);
    }

    public ValueTask DisposeAsync() => executor.DisposeAsync();

    [Fact]
    public async Task Of_two_writable_transactions_only_the_one_that_commits_is_flushed()
    {
        await RunAsync(async ct =>
        {
            await using (var first = scope.Begin())
            {
                outboxes.Current.Register(Batch("first"));
                await first.CommitAsync(ct);
            }

            await using (scope.Begin())
                outboxes.Current.Register(Batch("rolled back"));

            await using (var refused = scope.Begin())
            {
                outboxes.Current.Register(Batch("refused"));
                scope.Current!.RequireRollback();
                await refused.CommitAsync(ct);
            }
        });

        Assert.Equal(["first"], sender.Flushed());
    }

    [Fact]
    public async Task A_composition_in_the_unit_sees_the_committed_state_and_not_a_later_stage()
    {
        var state = "before the commit";
        await RunAsync(async ct =>
        {
            await using (var transaction = scope.Begin())
            {
                scope.Current!.Enlist("state", () => new Participant(() => state = "committed"));
                await outboxes.Current.ComposeAsync<NetworkConnectionContext>([Source],
                    (recipient, _) => ValueTask.FromResult<IReadOnlyList<OutboundEntry>>([Batch(state)]), ct);
                await transaction.CommitAsync(ct);
            }

            state = "a later stage";
        });

        Assert.Equal(["committed"], sender.Flushed());
    }

    [Fact]
    public async Task Entries_of_a_transaction_whose_outcome_is_uncertain_are_dropped()
    {
        await RunAsync(async ct =>
        {
            await using (var transaction = scope.Begin())
            {
                outboxes.Current.Register(Batch("uncertain"));
                scope.Current!.Enlist("broken", () => new Participant(() => throw new InvalidOperationException("broken")));
                await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => transaction.CommitAsync(ct).AsTask());
            }

            outboxes.Current.Register(Batch("after"));
        });

        Assert.Equal(["after"], sender.Flushed());
    }

    [Fact]
    public async Task What_is_registered_under_a_read_only_transaction_counts_as_outside_a_unit()
    {
        await RunAsync(async _ =>
        {
            await using (scope.Begin(readOnly: true))
                outboxes.Current.Register(Batch("read only"));
        });

        Assert.Equal(["read only"], sender.Flushed());
    }

    private Task RunAsync(Func<CancellationToken, Task> body) =>
        executor.RunAsync(ExecutionTest.Work(async ct => await body(ct)), CancellationToken.None).AsTask();

    private static OutboundEntry Batch(string text) =>
        OutboundEntry.Batch(Source, [new Text(text)], System.Text.Encoding.UTF8.GetBytes(text));

    private sealed record Text(string Value) : IServerNetworkMessage
    {
        public ServerMessageType MessageType => default;
    }

    /// <summary>A participant whose commit cannot fail by contract and runs <paramref name="commit"/>.</summary>
    private sealed class Participant(Action commit) : ITransactionParticipant
    {
        public bool CommitCanFail => false;

        public ValueTask PrepareAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            commit();
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class Recorder : IOutboundSender
    {
        public ConcurrentQueue<OutboundEntry> Entries { get; } = new();

        public void Submit(IReadOnlyList<OutboundEntry> entries)
        {
            foreach (var entry in entries) Entries.Enqueue(entry);
        }

        public IReadOnlyList<string> Flushed() =>
            [.. Entries.Select(entry => entry.IsClose ? "close" : string.Join("+", entry.Messages.Cast<Text>().Select(m => m.Value)))];
    }
}
