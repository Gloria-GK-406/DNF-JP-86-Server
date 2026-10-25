using System.Collections;
using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Networking.Outbound;
using Dfo.Server.Protocol.Tests.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Users;
using Dfo.Server.Protocol.Game;
using System.Reflection;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Networking;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol.Game.CompoundItem;
using Dfo.Server.Protocol.Game.LotteryOpening;
using Dfo.Server.Protocol.Game.VendingMachine;
using Dfo.Server.Protocol.Game.EpicCraft;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class RemainingOpeningProtocolCommandFlowTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    [InlineData(3, false)] [InlineData(3, true)]
    public async Task Actual_action_and_native_commit_rollback_preserve_assets_ack_and_original_entry_retry(int protocol, bool native)
    {
        using var f = await Fixture.Create(protocol);
        var before = Assets(f.Host);
        f.Host.Sql(native
            ? "CREATE TABLE remaining_parent(id INTEGER PRIMARY KEY); CREATE TABLE remaining_child(id INTEGER REFERENCES remaining_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER remaining_failure AFTER INSERT ON items BEGIN INSERT INTO remaining_child VALUES(404); END"
            : "CREATE TRIGGER remaining_failure BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT,'remaining action SQL'); END");
        await EffectRequestTransport.Send(f.Host, f.Input);
        var attempt = Assert.Single(f.Trace.Attempts);
        var cause = Assert.IsType<SqliteException>(native ? attempt.Transaction.CommitFailure!.Cause : attempt.Failure);
        Assert.Equal(19, cause.SqliteErrorCode); Assert.Equal(native ? 787 : 1811, cause.SqliteExtendedErrorCode);
        Assert.Equal(1, attempt.ForeignKeys); Assert.Equal(native, attempt.Returned);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, attempt.Transaction.Completion);
        if (native) { Assert.True(attempt.Children > 0); Assert.Contains("SqliteTransaction.Commit", cause.StackTrace!); }
        else Assert.DoesNotContain("SqliteTransaction.Commit", cause.StackTrace!);
        Assert.Equal(before, Assets(f.Host));
        var entry = Entry(f.Host); var ordinal = entry.Ordinal;
        Assert.Equal(EffectOperationState.ConfirmedRollback, entry.State);
        var frame = Assert.Single(f.Host.Sender.Outbound.Frames);
        Assert.Equal((byte)1, frame[0]); Assert.Equal(f.Input.Metadata.Type, BitConverter.ToUInt16(frame, 1));
        Assert.Equal(new byte[] { 0, protocol == 0 ? (byte)0x11 : protocol == 3 ? (byte)2 : (byte)0 }, frame[15..]);
        Assert.False(f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Host.Connection));
        Assert.Empty(f.Host.Sender.Outbound.Closes);
        f.Host.Sql("DROP TRIGGER remaining_failure");
        await EffectRequestTransport.Send(f.Host, f.Input);
        Assert.Same(entry, Entry(f.Host)); Assert.Equal(ordinal, entry.Ordinal); Assert.Equal(EffectOperationState.Committed, entry.State);
        Assert.Equal(2, f.Trace.Attempts.Count); Assert.Equal(DomainTransactionCompletion.Committed, f.Trace.Attempts[1].Transaction.Completion);
        var committed = Assets(f.Host); var acknowledgements = f.Host.Sender.Messages.Count(IsAcknowledgement);
        await EffectRequestTransport.Send(f.Host, f.Input);
        Assert.Equal(committed, Assets(f.Host)); Assert.Equal(2, f.Trace.Attempts.Count);
        Assert.Equal(acknowledgements, f.Host.Sender.Messages.Count(IsAcknowledgement));
        Assert.True(EffectRequestTransport.IsStateReplay(f.Host.Sender.Batches.Last()));
        output.WriteLine($"protocol={protocol} native={native} cause={cause} ordinal={ordinal} completion={attempt.Transaction.Completion} retry={entry.State}");
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Actual_committed_CurrentStateBatch_encoder_failure_keeps_raw_cause_entry_and_current_only_retry(int protocol)
    {
        using var f = await Fixture.Create(protocol); await EffectRequestTransport.Send(f.Host, f.Input);
        Assert.True(Assert.Single(f.Trace.Attempts).Returned);
        var entry = Entry(f.Host); var ordinal = entry.Ordinal; var committed = Assets(f.Host);
        var acks = f.Host.Sender.Messages.Count(IsAcknowledgement); var cause = new IOException("actual remaining current batch encoder"); var calls = 0;
        f.Host.Sender.Outbound.Encoder.Fail((target, _) => { Assert.Same(f.Host.Connection, target); return calls++ == 0 ? cause : null; });
        var thrown = await Record.ExceptionAsync(() => EffectRequestTransport.Send(f.Host, f.Input).AsTask());
        Assert.Same(cause, thrown); Assert.Equal(1, calls); Assert.Same(entry, Entry(f.Host)); Assert.Equal(ordinal, entry.Ordinal);
        Assert.Equal(EffectOperationState.Committed, entry.State); Assert.Equal(committed, Assets(f.Host)); Assert.Single(f.Trace.Attempts);
        Assert.False(f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Host.Connection));
        Assert.Same(f.Host.Connection, Assert.Single(f.Host.Sender.Outbound.Entries, x => x.IsClose).Connection);
        f.Host.Sql("UPDATE character_stats SET carried_gold=98765 WHERE character_id=1");
        await EffectRequestTransport.Send(f.Host, f.Input);
        Assert.Equal(98765, EffectRequestTransport.MainValue(f.Host, 0)); Assert.Single(f.Trace.Attempts);
        Assert.Equal(acks, f.Host.Sender.Messages.Count(IsAcknowledgement));
        Assert.Equal(committed with { Gold = 98765 }, Assets(f.Host));
        output.WriteLine($"protocol={protocol} actualCurrentEncoderCalls={calls} sameCause={ReferenceEquals(cause, thrown)} state={entry.State} ordinal={ordinal} registeredClose=True");
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Whole_scope_disposal_after_Main_keeps_default_typed_exact_participant_protection(int protocol)
    {
        var trace = new Trace { ScopeFailure = new IOException("remaining whole scope disposal") };
        using var f = await Fixture.Create(protocol, trace);
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => EffectRequestTransport.Send(f.Host, f.Input).AsTask());
        Assert.Same(trace.ScopeFailure, failure.InnerException);
        Assert.Equal(EffectOperationState.Committed, Entry(f.Host).State);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Attempts).Transaction.Completion);
        Assert.True(f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Host.Connection));
        Assert.All(f.Host.Sender.Outbound.Entries.Where(x => x.IsClose), x => Assert.Same(f.Host.Connection, x.Connection));
        Assert.NotEmpty(f.Host.Sender.Outbound.Closes); Assert.Single(f.Host.Sender.Messages, IsAcknowledgement);
        Assert.NotEmpty(f.Host.Sender.Outbound.Frames);
    }

    [Fact]
    public async Task Lottery_independent_premium_ReadOnly_failure_after_Main_keeps_reward_and_default_exact_fence()
    {
        var trace = new Trace { PremiumFailure = new IOException("independent premium read after Main") };
        using var f = await Fixture.Create(4, trace);
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => EffectRequestTransport.Send(f.Host, f.Input).AsTask());
        Assert.Same(trace.PremiumFailure, failure.InnerException); Assert.True(trace.PremiumFailed);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Attempts).Transaction.Completion);
        Assert.Equal(EffectOperationState.Committed, Entry(f.Host).State); Assert.Equal(2, f.Host.Count(20002)); Assert.Equal(2, f.Host.Count(7110));
        Assert.Empty(f.Host.Sender.Messages); Assert.True(f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Host.Connection));
        Assert.NotEmpty(f.Host.Sender.Outbound.Closes); Assert.All(f.Host.Sender.Outbound.Entries.Where(x => x.IsClose), x => Assert.Same(f.Host.Connection, x.Connection));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Cancellation_and_typed_uncertain_domain_failures_never_become_normal_opening_ACK(bool uncertain)
    {
        var cause = new IOException("opening domain uncertainty");
        var trace = new Trace { DomainFailure = uncertain ? new TransactionOutcomeUncertainException(TransactionUncertaintyReason.RollbackFailure, "typed domain failure", cause) : new OperationCanceledException("opening domain cancelled") };
        using var f = await Fixture.Create(0, trace); var before = Assets(f.Host);
        var failure = await Record.ExceptionAsync(() => EffectRequestTransport.Send(f.Host, f.Input).AsTask());
        Assert.Same(trace.DomainFailure, failure); Assert.Equal(before, Assets(f.Host)); Assert.Empty(f.Host.Sender.Messages);
        Assert.Equal(uncertain, f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Host.Connection));
        Assert.NotEmpty(f.Host.Sender.Outbound.Closes); Assert.All(f.Host.Sender.Outbound.Entries.Where(x => x.IsClose), x => Assert.Same(f.Host.Connection, x.Connection));
    }

    [Fact]
    public async Task Actual_replay_encoder_fault_registers_close_only_for_original_context_after_same_id_replacement()
    {
        using var f = await Fixture.Create(2); await EffectRequestTransport.Send(f.Host, f.Input);
        var original = f.Host.Connection; var replacement = original with { }; var cause = new IOException("replacement during actual replay encoding");
        var before = Assets(f.Host); var entriesBefore = f.Host.Sender.Outbound.Entries.Count;
        f.Host.Sender.Outbound.Encoder.Fail((target, _) =>
        {
            Assert.Same(original, target); f.Host.Sender.Outbound.Connections.Remove(original.ConnectionId);
            f.Host.Sender.Outbound.Connections.Add(replacement); return cause;
        });
        Assert.Same(cause, await Record.ExceptionAsync(() => EffectRequestTransport.Send(f.Host, f.Input).AsTask()));
        Assert.True(f.Host.Sender.Outbound.Connections.TryGet(original.ConnectionId, out var current)); Assert.Same(replacement, current);
        Assert.All(f.Host.Sender.Outbound.Entries.Skip(entriesBefore), entry => Assert.Same(original, entry.Connection));
        Assert.DoesNotContain(f.Host.Sender.Outbound.Entries, entry => ReferenceEquals(replacement, entry.Connection));
        Assert.Equal(EffectOperationState.Committed, Entry(f.Host).State); Assert.Equal(before, Assets(f.Host)); Assert.Single(f.Trace.Attempts);
        Assert.False(f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(replacement));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Real_action_SQL_and_independent_rollback_failure_keep_two_occurrences_and_never_ACK(bool sameObject)
    {
        var trace = new Trace { FailRollback = true, RollbackSameObject = sameObject };
        using var f = await Fixture.Create(0, trace); var before = Assets(f.Host);
        f.Host.Sql("CREATE TRIGGER remaining_failure BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT,'remaining action SQL'); END");
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => EffectRequestTransport.Send(f.Host, f.Input).AsTask());
        var original = Assert.Single(trace.Attempts).Failure; Assert.Equal(1811, Assert.IsType<SqliteException>(original).SqliteExtendedErrorCode);
        var causes = Assert.IsType<AggregateException>(failure.InnerException).InnerExceptions;
        Assert.Equal(2, causes.Count); Assert.Same(original, causes[0]); Assert.Same(sameObject ? original : trace.RollbackCause, causes[1]);
        Assert.Equal(before, Assets(f.Host)); Assert.Empty(f.Host.Sender.Messages); Assert.Equal(EffectOperationState.Blocked, Entry(f.Host).State);
        Assert.True(f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Host.Connection));
        Assert.NotEmpty(f.Host.Sender.Outbound.Closes); Assert.All(f.Host.Sender.Outbound.Entries.Where(x => x.IsClose), x => Assert.Same(f.Host.Connection, x.Connection));
    }
    private sealed class RollbackFault(Trace trace, Attempt attempt) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => throw (trace.RollbackSameObject ? attempt.Failure! : trace.RollbackCause);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Original_argument_and_vending_input_rejections_do_not_claim_Main(bool vending)
    {
        using var f = await Fixture.Create(vending ? 2 : 0);
        IEffectSettlementRequest input = vending ? new VendingMachineRequest(new(1, 0xD7, 0, 7), uint.MaxValue, 1, 121)
            : new CompoundItemRequest(new(1, 0x19, 0, 7), -1, 0, 1);
        var before = Assets(f.Host); await EffectRequestTransport.Send(f.Host, input);
        Assert.Empty(f.Trace.Attempts); Assert.Equal(EffectOperationState.Rejected, Entry(f.Host).State);
        Assert.Equal(before, Assets(f.Host)); Assert.Single(f.Host.Sender.Messages, IsAcknowledgement); Assert.Empty(f.Host.Sender.Outbound.Closes);
    }

    private sealed class ObserveWhole<TCommand, THandler>(THandler actual, Trace trace) : ICommandHandler<TCommand, bool>, IDisposable
        where TCommand : class, ICommand<bool> where THandler : ICommandHandler<TCommand, bool>
    {
        public ValueTask<bool> HandleAsync(CommandExecutionContext context, TCommand command, CancellationToken ct) => actual.HandleAsync(context, command, ct);
        public void Dispose() { if (trace.ScopeFailure is { } cause) throw cause; }
    }
    public class PremiumObserver : DispatchProxy
    {
        internal object Actual = null!; internal Trace Trace = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "ReadAsync" && Trace.Attempts.LastOrDefault()?.Transaction.Completion == DomainTransactionCompletion.Committed)
            { Trace.PremiumFailed = true; throw Trace.PremiumFailure!; }
            try { return method.Invoke(Actual, args); }
            catch (TargetInvocationException ex) when (ex.InnerException is { } cause) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cause).Throw(); throw; }
        }
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Production_sender_replay_actual_encoder_failure_closes_original_transport_after_preceding_delivery(int protocol)
    {
        await using var delivery = new SenderProbe(); var trace = new Trace { Delivery = delivery };
        using var f = await Fixture.Create(protocol, trace); var physical = delivery.Add(f.Host.Connection);
        var route = f.Host.Services.GetRequiredService<Dfo.Server.Application.Dispatching.MessageHandlerRegistry>();
        Assert.True(route.TryGet(f.Input.MessageType, out var registered));
        var expected = Environment.GetEnvironmentVariable("TASK35_EXPECT_ROUTE") ?? "whole";
        Assert.Equal(expected == "whole", registered!.ProtocolCommandType is not null);
        if (expected == "whole") Assert.Equal("Game" + f.Input.GetType().Name.Replace("Request", "Command"), registered.ProtocolCommandType!.Name);
        await EffectRequestTransport.Send(f.Host, f.Input); await delivery.Settled();
        var entry = Entry(f.Host); var ordinal = entry.Ordinal; var before = Assets(f.Host);
        var first = Assert.Single(physical.Sends); Assert.Equal((byte)1, first[0]); Assert.Equal(f.Input.Metadata.Type, BitConverter.ToUInt16(first, 1));
        Assert.NotEmpty(delivery.Recorder.Entries); Assert.All(delivery.Reports, report => Assert.Equal(OutboundOutcome.Delivered, report.Outcome));
        var cause = new IOException("actual current-state batch encoder before production sender"); var calls = 0;
        NetworkConnectionContext? encodedFor = null;
        f.Host.Sender.Outbound.Encoder.Fail((target, message) =>
        {
            encodedFor = target; Assert.Contains("InventorySnapshot", message.GetType().Name); calls++; return cause;
        });
        var failure = await Record.ExceptionAsync(() => EffectRequestTransport.Send(f.Host, f.Input).AsTask());
        Assert.Same(cause, failure); Assert.Equal(1, calls); Assert.Same(f.Host.Connection, encodedFor);
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled();
        Assert.Equal(1, physical.CloseCalls); Assert.Single(physical.Sends);
        Assert.Equal(new[] { "send", "close" }, physical.Events.Select(x => x.Kind));
        Assert.Same(entry, Entry(f.Host)); Assert.Equal(ordinal, entry.Ordinal); Assert.Equal(EffectOperationState.Committed, entry.State);
        Assert.Equal(before, Assets(f.Host)); Assert.Single(trace.Attempts); Assert.False(f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Host.Connection));
        Assert.Equal(OutboundOutcome.Closed, delivery.Reports.Last().Outcome);
        Assert.All(delivery.Recorder.Entries, submitted => Assert.Same(f.Host.Connection, submitted.Connection));
        output.WriteLine($"route={expected} command={registered.ProtocolCommandType?.Name ?? "legacy"} protocol={protocol} actualEncoderCalls={calls} sameCause=True state={entry.State} ordinal={ordinal} transportCloseCalls={physical.CloseCalls} actualOrder={string.Join(',', physical.Events.Select(x => x.Kind))}");
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Production_sender_whole_scope_and_independent_premium_failures_deliver_only_kept_frames_then_close(int protocol)
    {
        await using var delivery = new SenderProbe(); var cause = new IOException(protocol == 4 ? "premium read after actual Main" : "whole scope after actual Main");
        var trace = new Trace { Delivery = delivery, ScopeFailure = protocol == 4 ? null : cause, PremiumFailure = protocol == 4 ? cause : null };
        using var f = await Fixture.Create(protocol, trace); var physical = delivery.Add(f.Host.Connection);
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => EffectRequestTransport.Send(f.Host, f.Input).AsTask());
        Assert.Same(cause, failure.InnerException);
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled();
        Assert.Equal(1, physical.CloseCalls); Assert.Equal(EffectOperationState.Committed, Entry(f.Host).State);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(trace.Attempts).Transaction.Completion);
        Assert.True(f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Host.Connection));
        Assert.All(delivery.Recorder.Entries, entry => Assert.Same(f.Host.Connection, entry.Connection));
        if (protocol == 4)
        {
            Assert.True(trace.PremiumFailed); Assert.Empty(physical.Sends); Assert.Equal(new[] { "close" }, physical.Events.Select(x => x.Kind));
            Assert.Equal(2, f.Host.Count(20002)); Assert.Equal(2, f.Host.Count(7110));
        }
        else
        {
            var sent = Assert.Single(physical.Sends); Assert.Equal((byte)1, sent[0]); Assert.Equal(f.Input.Metadata.Type, BitConverter.ToUInt16(sent, 1));
            Assert.Equal(new[] { "send", "close" }, physical.Events.Select(x => x.Kind));
            Assert.Equal(OutboundOutcome.Delivered, delivery.Reports.First().Outcome);
        }
        Assert.Contains(delivery.Reports, report => report.Outcome == OutboundOutcome.Closed);
        output.WriteLine($"protocol={protocol} sameCause=True committed=True transportCloseCalls={physical.CloseCalls} actualOrder={string.Join(',', physical.Events.Select(x => x.Kind))}");
    }

    [Fact]
    public async Task Production_sender_replay_close_preserves_same_id_replacement_transport()
    {
        await using var delivery = new SenderProbe(); var trace = new Trace { Delivery = delivery };
        using var f = await Fixture.Create(2, trace); var original = delivery.Add(f.Host.Connection);
        await EffectRequestTransport.Send(f.Host, f.Input); await delivery.Settled();
        var replacement = f.Host.Connection with { }; var nextPhysical = delivery.Add(replacement); var cause = new IOException("replace physical context during actual replay encoding");
        f.Host.Sender.Outbound.Encoder.Fail((target, _) =>
        {
            Assert.Same(f.Host.Connection, target); f.Host.Sender.Outbound.Connections.Remove(target.ConnectionId);
            f.Host.Sender.Outbound.Connections.Add(replacement); return cause;
        });
        Assert.Same(cause, await Record.ExceptionAsync(() => EffectRequestTransport.Send(f.Host, f.Input).AsTask()));
        await original.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await delivery.Settled();
        Assert.Equal(1, original.CloseCalls); Assert.Equal(0, nextPhysical.CloseCalls); Assert.False(nextPhysical.Closed.Task.IsCompleted);
        Assert.Equal(new[] { "send", "close" }, original.Events.Select(x => x.Kind)); Assert.Empty(nextPhysical.Events);
        Assert.Same(nextPhysical, delivery.Resolve(replacement)); Assert.Null(delivery.Resolve(f.Host.Connection));
        Assert.All(delivery.Recorder.Entries, entry => Assert.Same(f.Host.Connection, entry.Connection));
        Assert.Equal(EffectOperationState.Committed, Entry(f.Host).State); Assert.Single(trace.Attempts);
        Assert.False(f.Host.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(replacement));
        output.WriteLine("production sender originalCloseCalls=1 replacementCloseCalls=0 sameId=True differentContext=True");
    }

    internal sealed class SenderProbe : IOutboundTransport, IOutboundDeliveryObserver, IAsyncDisposable
    {
        private readonly ConcurrentDictionary<NetworkConnectionContext, PhysicalConnection> connections = new(ReferenceEqualityComparer.Instance);
        private readonly OutboundSender sender;
        internal XoSubmissions Recorder { get; }
        internal ConcurrentQueue<OutboundDeliveryReport> Reports { get; } = new();
        internal SenderProbe()
        {
            sender = new OutboundSender(this, [this], NullLogger<OutboundSender>.Instance, OutboundSendOptions.Default);
            Recorder = new XoSubmissions(sender);
        }
        internal PhysicalConnection Add(NetworkConnectionContext context) => connections.GetOrAdd(context, key => new PhysicalConnection(key));
        public IOutboundConnection? Resolve(NetworkConnectionContext expected) => connections.TryGetValue(expected, out var physical) && !physical.Closed.Task.IsCompleted ? physical : null;
        public void OnEntryCompleted(OutboundDeliveryReport report) => Reports.Enqueue(report);
        public void OnConnectionFailed(NetworkConnectionContext connection, OutboundOutcome reason) { }
        internal async Task Settled()
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (Reports.Count < Recorder.Entries.Count)
            { if (DateTime.UtcNow > deadline) throw new TimeoutException("production outbound entries did not settle"); await Task.Delay(5); }
        }
        public ValueTask DisposeAsync() => sender.DisposeAsync();
    }
    internal sealed class PhysicalConnection(NetworkConnectionContext context) : IOutboundConnection
    {
        internal NetworkConnectionContext Context = context;
        internal ConcurrentQueue<byte[]> Sends { get; } = new();
        internal ConcurrentQueue<(string Kind, NetworkConnectionContext Context)> Events { get; } = new();
        internal TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int CloseCalls;
        public ValueTask SendAsync(ReadOnlyMemory<byte> frames, CancellationToken ct)
        {
            Assert.False(Closed.Task.IsCompleted); Sends.Enqueue(frames.ToArray()); Events.Enqueue(("send", Context)); return ValueTask.CompletedTask;
        }
        public bool HasClosedSendSide(Exception error) => false;
        public ValueTask CloseAsync()
        { Interlocked.Increment(ref CloseCalls); Events.Enqueue(("close", Context)); Closed.TrySetResult(); return ValueTask.CompletedTask; }
    }

    private static bool IsAcknowledgement(IServerNetworkMessage message) => message is CompoundItemResponse or LotteryOpeningResponse or VendingMachineResponse or EpicCraftResponse;
    private sealed record AssetState(int Items, int Counts, int Shared, int Pieces, int Gold, int Counters, int Events, int Deliveries);
    private static AssetState Assets(ShopFixture f) => new(f.Scalar("SELECT count(*) FROM items"), f.Scalar("SELECT COALESCE(SUM(count),0) FROM item_slots"),
        f.SharedCount(3262), f.Scalar("SELECT COALESCE(SUM(count),0) FROM account_epic_pieces"), f.Gold,
        f.Scalar("SELECT COALESCE(SUM(value),0) FROM character_daily_counters"), f.Scalar("SELECT count(*) FROM domain_events"), f.Scalar("SELECT count(*) FROM domain_event_deliveries"));
    private static EffectOperationEntry Entry(ShopFixture f)
    {
        var ledger = f.Services.GetRequiredService<EffectOperationLedger>();
        var table = typeof(EffectOperationLedger).GetField("ledgers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ledger)!;
        object?[] args = [f.Connection, null]; Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!);
        return Assert.Single(((IDictionary)args[1]!.GetType().GetProperty("Entries")!.GetValue(args[1])!).Values.Cast<EffectOperationEntry>());
    }
    internal sealed class Trace { internal List<Attempt> Attempts { get; } = []; internal Exception? ScopeFailure, PremiumFailure, DomainFailure; internal bool PremiumFailed, FailRollback, RollbackSameObject; internal SenderProbe? Delivery; internal IOException RollbackCause = new("independent remaining rollback failure"); }
    internal sealed class Attempt(DomainTransaction transaction) { internal DomainTransaction Transaction = transaction; internal bool Returned; internal Exception? Failure; internal int ForeignKeys, Children; }
    public class DomainObserver : DispatchProxy
    {
        internal object Actual = null!; internal IServiceProvider Services = null!; private Trace trace = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => typeof(DomainObserver).GetMethod(nameof(Observe), BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(method!.ReturnType.GenericTypeArguments[0]).Invoke(this, [method, args]);
        private async ValueTask<T> Observe<T>(MethodInfo method, object?[] args)
        {
            var attempt = new Attempt((DomainTransaction)Services.GetRequiredService<IDomainTransactionAccessor>().Current!); trace.Attempts.Add(attempt);
            if (trace.FailRollback) attempt.Transaction.Enlist(trace, () => new RollbackFault(trace, attempt));
            attempt.ForeignKeys = await Scalar("PRAGMA foreign_keys");
            try
            {
                if (trace.DomainFailure is { } injected) throw injected;
                var result = await (ValueTask<T>)method.Invoke(Actual, args)!; attempt.Returned = true;
                if (await Scalar("SELECT count(*) FROM sqlite_master WHERE name='remaining_child'") != 0) attempt.Children = await Scalar("SELECT count(*) FROM remaining_child");
                return result;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is { } cause) { attempt.Failure = cause; System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cause).Throw(); throw; }
            catch (Exception cause) { attempt.Failure = cause; throw; }
        }
        private ValueTask<int> Scalar(string sql) => Services.GetRequiredService<SqliteDatabase>().ReadAsync(async (cmd, ct) => { cmd.CommandText = sql; return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)); }, default);
        internal static void Add<T>(IServiceCollection services, Trace trace) where T : class
        {
            var descriptor = services.Last(x => x.ServiceType == typeof(T));
            services.AddSingleton<T>(p =>
            {
                var proxy = Create<T, DomainObserver>(); var observer = (DomainObserver)(object)proxy;
                observer.Actual = descriptor.ImplementationInstance ?? descriptor.ImplementationFactory?.Invoke(p) ?? ActivatorUtilities.CreateInstance(p, descriptor.ImplementationType!);
                observer.Services = p; observer.trace = trace; return proxy;
            });
        }
    }
    private sealed class Fixture(IDisposable owner, ShopFixture host, IEffectSettlementRequest input, Trace trace) : IDisposable
    {
        internal ShopFixture Host = host; internal IEffectSettlementRequest Input = input; internal Trace Trace = trace;
        public void Dispose() => owner.Dispose();
        internal static async Task<Fixture> Create(int protocol, Trace? suppliedTrace = null)
        {
            var trace = suppliedTrace ?? new Trace();
            void Configure(IServiceCollection s)
            {
                if (trace.Delivery is { } delivery) s.AddSingleton<IOutboundSender>(delivery.Recorder);
                if (trace.ScopeFailure is not null)
                {
                    s.AddScoped<GameCompoundItemCommandHandler>(); s.AddScoped<ICommandHandler<GameCompoundItemCommand, bool>, ObserveWhole<GameCompoundItemCommand, GameCompoundItemCommandHandler>>();
                    s.AddScoped<GameLotteryOpeningCommandHandler>(); s.AddScoped<ICommandHandler<GameLotteryOpeningCommand, bool>, ObserveWhole<GameLotteryOpeningCommand, GameLotteryOpeningCommandHandler>>();
                    s.AddScoped<GameVendingMachineCommandHandler>(); s.AddScoped<ICommandHandler<GameVendingMachineCommand, bool>, ObserveWhole<GameVendingMachineCommand, GameVendingMachineCommandHandler>>();
                    s.AddScoped<GameEpicCraftCommandHandler>(); s.AddScoped<ICommandHandler<GameEpicCraftCommand, bool>, ObserveWhole<GameEpicCraftCommand, GameEpicCraftCommandHandler>>();
                    s.AddSingleton(trace);
                }
                if (trace.PremiumFailure is not null)
                {
                    var descriptor = s.Last(x => x.ServiceType == typeof(Dfo.Server.Abstractions.Accounts.IPremiumServiceUsageDomainService));
                    s.AddSingleton<Dfo.Server.Abstractions.Accounts.IPremiumServiceUsageDomainService>(p =>
                    {
                        var proxy = DispatchProxy.Create<Dfo.Server.Abstractions.Accounts.IPremiumServiceUsageDomainService, PremiumObserver>();
                        var observer = (PremiumObserver)(object)proxy; observer.Trace = trace;
                        observer.Actual = descriptor.ImplementationInstance ?? descriptor.ImplementationFactory?.Invoke(p) ?? ActivatorUtilities.CreateInstance(p, descriptor.ImplementationType!);
                        return proxy;
                    });
                }
                if (protocol == 0) DomainObserver.Add<IRecipeCraftDomainService>(s, trace);
                else if (protocol is 1 or 4) DomainObserver.Add<ILotteryOpeningDomainService>(s, trace);
                else if (protocol == 2) DomainObserver.Add<IVendingMachineDomainService>(s, trace);
                else DomainObserver.Add<IEpicCraftDomainService>(s, trace);
            }
            if (protocol == 0) { var h = RecipeCraftTests.ProtocolFixture(Configure); await RecipeCraftTests.Seed(h, 2); return new(h, h, new CompoundItemRequest(new(1, 0x19, 0, 7), 101, 0, 1), trace); }
            if (protocol == 3) { var h = EpicCraftTests.Fixture(Configure); await EpicCraftTests.Seed(h, 2); return new(h, h, new EpicCraftRequest(new(1, 0x313, 0, 7), 3000), trace); }
            var shop = protocol == 2 ? VendingMachineTests.ProtocolFixture(Configure) : LotteryOpeningTests.ProtocolFixture(Configure);
            await shop.Host.Seed(protocol == 2 ? 121 : 65, protocol == 2 ? 7454 : 7110, 3);
            if (protocol == 4) shop.Host.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,581,4000000000)");
            return new(shop, shop.Host, protocol == 2 ? new VendingMachineRequest(new(1, 0xD7, 0, 7), 1, 1, 121) : new LotteryOpeningRequest(new(1, 0x1B, 0, 7), protocol == 4 ? (ushort)1 : (ushort)0, 65), trace);
        }
    }
}
