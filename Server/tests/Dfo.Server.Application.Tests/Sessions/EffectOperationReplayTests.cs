using Dfo.Server.Application.Dispatching;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Protocol.Game.MarriageProposal;
using Dfo.Server.Application.Users;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Effects;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Microsoft.Data.Sqlite;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol.Game.UseStackable;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class EffectOperationReplayTests
{
    private sealed class LoveContent : IMarriageCeremonyContentSource
    { public MarriageCeremonyContent Load() => new([], []) { LovePointItems = new Dictionary<int, int> { [1000] = 1 }, MaximumLovePoints = 999 }; }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Love_coupon_real_dispatcher_lost_ack_replays_current_inventory_and_points_without_second_deduction(bool encodingFailure)
    {
        LossyLoveSender transport = null!;
        using var f = new ShopFixture(s => {
            transport = new((IOutboundSender)s.Last(x => x.ServiceType == typeof(IOutboundSender)).ImplementationInstance!);
            s.AddSingleton<IOutboundSender>(transport);
            s.AddSingleton<IItemUseRuleDefinitionSource>(new FakeItemUseRules((1000, ItemUseRules.Potion(0) with { Action = "[love point coupon]" })));
            s.AddSingleton<IMarriageCeremonyContentSource, LoveContent>();
        });
        f.Sql("INSERT INTO accounts(account_id,m_id) VALUES(2,'love-peer'); INSERT INTO characters(character_id,account_id,name,job,level,slot_index) VALUES(3,2,'peer',0,50,0)");
        var peer = SelectedSessionIdentity.Create(new(2), new(3), new("offline-peer"), 1, 10011);
        var at = f.Clock.GetUtcNow();
        var relation = new MarriageProposal(Guid.NewGuid(), f.Session, peer, MarriageProposalState.Pending, 1, at, at.AddMinutes(1));
        var ceremony = new MarriageCeremony(Guid.NewGuid(), relation.Id, f.Session, peer, 0, 0, MarriageCeremonyState.Pending, 1, at, at.AddMinutes(1));
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = ((Dfo.Server.Application.Transactions.DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin())
            {
                var relations = f.Services.GetRequiredService<IMarriageProposalDomainStore>(); await relations.CreateAsync(relation, default);
                Assert.True(await relations.TransitionAsync(relation, MarriageProposalState.Engaged, 0, default));
                await f.Services.GetRequiredService<IMarriageCeremonyDomainStore>().CreateAsync(ceremony, default);
                var weddings = f.Services.GetRequiredService<IMarriageWeddingDomainStore>(); await weddings.CreateAsync(new(ceremony, MarriageWeddingPhase.Preparing, 0), default);
                Assert.True(await weddings.FinishAsync(ceremony.Id, MarriageWeddingPhase.Completed, at, default)); await tx.CommitAsync(default);
            }
        });
        f.Sql($"INSERT INTO marriage_relations(id,stage,revision,stage_since,ceremony_id) VALUES('{relation.Id:N}',4,1,{at.ToUnixTimeMilliseconds()},'{ceremony.Id:N}')");
        await f.Seed(69, 1000, 3);
        var counter = new GameInboundFrameCounter();
        WeddingLovePointRequest Request()
        {
            var (ordinal, fresh) = counter.Observe(1); var body = new byte[18]; BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(14), 69);
            return new((new GameMessageMetadata(1, 0x03F5, 0, 1) { InboundOrdinal = ordinal, SequenceIsFresh = fresh }).WithRawBody(body), 69);
        }
        var dispatcher = f.Services.GetRequiredService<INetworkMessageDispatcher>();
        if (encodingFailure)
        {
            f.Sender.FailExplicit = true;
            var error = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(async () => await dispatcher.DispatchAsync(new(f.Connection), Request(), default));
            Assert.IsType<IOException>(error.InnerException);
            Assert.Equal(2, f.Count(1000)); Assert.Equal(1, f.Scalar("SELECT points FROM marriage_personal_points WHERE character_id=1"));
            Assert.True(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
            Assert.True(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
            Assert.Contains(f.Sender.Outbound.Entries, x => x.IsClose && ReferenceEquals(x.Connection, f.Connection));
            Assert.Empty(f.Sender.Batches); return;
        }
        await dispatcher.DispatchAsync(new(f.Connection), Request(), default);
        Assert.Single(transport.Dropped);
        Assert.Same(f.Connection, transport.Dropped[0].Connection);
        Assert.Contains(transport.Dropped[0].Messages, x => x.GetType().Name == "WeddingLovePointAck");
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.Equal(2, f.Count(1000)); Assert.Equal(1, f.Scalar("SELECT points FROM marriage_personal_points WHERE character_id=1"));
        await dispatcher.DispatchAsync(new(f.Connection), Request(), default);
        Assert.Equal(2, f.Count(1000)); Assert.Equal(1, f.Scalar("SELECT points FROM marriage_personal_points WHERE character_id=1"));
        var batch = Assert.Single(f.Sender.Batches);
        Assert.DoesNotContain(batch.Frames, frame => frame[0] == 1 && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == 0x03F5);
        var points = Assert.Single(batch.Frames, frame => frame[0] == 0 && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == 0x03D3);
        Assert.Equal(Convert.FromHexString("0100000001000000"), points[15..]);
    }

    private sealed class LossyLoveSender(IOutboundSender inner) : IOutboundSender
    {
        internal List<OutboundEntry> Dropped { get; } = [];
        public void Submit(IReadOnlyList<OutboundEntry> entries)
        {
            var kept = new List<OutboundEntry>();
            foreach (var entry in entries)
            {
                if (Dropped.Count == 0 && entry.Messages.Any(x => x.GetType().Name == "WeddingLovePointAck")) Dropped.Add(entry);
                else kept.Add(entry);
            }
            inner.Submit(kept);
        }
    }

    [Fact]
    public async Task Shared_effect_real_dispatcher_commits_once_and_replay_only_refreshes_inventory()
    {
        using var f = new ShopFixture(services => services.AddSingleton<IItemUseRuleDefinitionSource>(
            new FakeItemUseRules((1000, ItemUseRules.Potion(0) with { SharedEffectSelector = 0 }))));
        await f.Seed(69, 1000, 3);
        var counter = new GameInboundFrameCounter();
        SharedEffectRequest Request()
        {
            var (ordinal, fresh) = counter.Observe(1); var body = new byte[6];
            BinaryPrimitives.WriteInt32LittleEndian(body, 1000); BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4), 69);
            return new((new GameMessageMetadata(1, 0x00FB, 0, 1) { InboundOrdinal = ordinal, SequenceIsFresh = fresh }).WithRawBody(body), 1000, 69);
        }
        var dispatcher = f.Services.GetRequiredService<INetworkMessageDispatcher>();
        await dispatcher.DispatchAsync(new(f.Connection), Request(), default);
        Assert.Equal(2, f.Count(1000));
        await dispatcher.DispatchAsync(new(f.Connection), Request(), default);
        Assert.Equal(2, f.Count(1000)); Assert.Single(f.Sender.Batches);
    }

    [Fact]
    public async Task A_use_already_durable_before_sender_failure_cannot_consume_twice_on_the_same_raw_request()
    {
        using var fixture = new ShopFixture(services => services.AddSingleton<IItemUseRuleDefinitionSource>(
            new FakeItemUseRules((1000, ItemUseRules.Potion(0)))));
        await fixture.Seed(69, 1000, 3);
        var cause = new IOException("Fixture Explicit publication failed after settlement.");
        fixture.Sender.Outbound.FailDirect(fixture.Connection.ConnectionId, () => cause);
        var counter = new GameInboundFrameCounter();
        var dispatcher = fixture.Services.GetRequiredService<INetworkMessageDispatcher>();
        UseStackableRequest Request()
        {
            var (ordinal, fresh) = counter.Observe(1);
            var body = new byte[11]; body[0] = 0;
            BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(1), 69);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(7), 1000);
            return new((new GameMessageMetadata(1, 0x002C, 0, 1) { InboundOrdinal = ordinal, SequenceIsFresh = fresh }).WithRawBody(body),
                new(InventoryContainerType.Main, 69), 0, 1000);
        }
        // Inject the Explicit I/O failure only after the actual command has committed.
        Assert.Same(cause, await Assert.ThrowsAsync<IOException>(async () => await dispatcher.DispatchAsync(new(fixture.Connection), Request(), default)));
        Assert.False(fixture.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(fixture.Connection));
        Assert.True(fixture.Sender.Outbound.Entries.Last().IsClose);
        Assert.Same(fixture.Connection, fixture.Sender.Outbound.Entries.Last().Connection);
        Assert.Equal(2, fixture.Count(1000));
        await dispatcher.DispatchAsync(new(fixture.Connection), Request(), default);
        var batch = Assert.Single(fixture.Sender.Batches);
        Assert.Same(fixture.Connection, batch.Connection);
        var main = batch.Frames.Single(frame => frame[0] == 0 && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == 0x000D && frame[15] == 0);
        var entryOffset = 15 + 5 + 3 * 101;
        Assert.Equal(69, BinaryPrimitives.ReadInt16LittleEndian(main.AsSpan(entryOffset)));
        Assert.Equal(1000, BinaryPrimitives.ReadInt32LittleEndian(main.AsSpan(entryOffset + 2)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(main.AsSpan(entryOffset + 6)));
        Assert.DoesNotContain(batch.Messages, x => x is UseStackableResponse);
        Assert.Equal(2, fixture.Count(1000));
    }
    [Fact]
    public async Task Identical_parameters_on_the_next_fresh_sequence_are_a_new_use()
    {
        using var f = Fixture(); await f.Seed(69, 1000, 3); var stream = new Stream();
        await Dispatch(f, stream.Request(1)); await Dispatch(f, stream.Request(2));
        Assert.Equal(1, f.Count(1000)); Assert.Empty(f.Sender.Batches);
        await Dispatch(f, stream.Request(2)); Assert.Equal(1, f.Count(1000)); Assert.Single(f.Sender.Batches);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Full_raw_identity_conflicts_never_pay_or_refresh(int conflict)
    {
        using var f = Fixture(); await f.Seed(69, 1000, 3); var stream = new Stream();
        await Dispatch(f, stream.Request(1)); var request = stream.Request(1);
        var metadata = request.Metadata;
        metadata = conflict switch {
            0 => metadata with { Selector = 0 },
            1 => metadata with { Type = 0x9999 },
            2 => metadata.WithRawBody(Body().Concat(new byte[] { 0x99 }).ToArray()),
            _ => metadata with { Sequence = 0x8888 },
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => Dispatch(f, request with { Metadata = metadata }));
        Assert.Equal(2, f.Count(1000)); Assert.Empty(f.Sender.Batches);
    }
    [Fact]
    public async Task Confirmed_SQL_abort_retries_the_exact_same_fingerprint()
    {
        using var f = Fixture(); await f.Seed(69, 1000, 3); var stream = new Stream();
        f.Sql("CREATE TRIGGER fail_use BEFORE UPDATE OF count ON item_slots BEGIN SELECT RAISE(ABORT,'late use'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Dispatch(f, stream.Request(1)));
        Assert.Equal(3, f.Count(1000)); f.Sql("DROP TRIGGER fail_use;");
        await Dispatch(f, stream.Request(1)); Assert.Equal(2, f.Count(1000));
        await Dispatch(f, stream.Request(1)); Assert.Equal(2, f.Count(1000)); Assert.Single(f.Sender.Batches);
    }
    /// <summary>
    /// Successor of the deleted <c>An_inherited_closed_operation_cannot_downgrade_to_trusted_direct_bus</c> (F-58):
    /// a continuation started inside the operation's job without suppressing the flow reaches the bus after the
    /// job, and with it the request frame, has ended. It is refused as a leaked flow instead of becoming a fresh
    /// trusted root command, so nothing is paid.
    /// </summary>
    [Fact]
    public async Task A_flow_leaked_from_a_closed_operation_cannot_reach_the_bus()
    {
        using var f = Fixture(); await f.Seed(69, 1000, 3); var stream = new Stream();
        var inherited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? child = null;
        f.Sender.BeforeExplicit = () => child = Task.Run(async () =>
        {
            inherited.SetResult(); await release.Task;
            await DomainCommandFixture.Bus(f.Services).SendAsync(WholeUse.Command(f.Services, f.Session,
                new InventorySlotMappingRuleService().ToBusinessSlot(new(InventoryContainerType.Main, 69)), 1000));
        });
        await Dispatch(f, stream.Request(1)); await inherited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult(); await Assert.ThrowsAsync<Dfo.Server.Abstractions.Execution.GameplayLeakedFlowException>(async () => await child!);
        Assert.Equal(2, f.Count(1000));
    }
    [Fact]
    public async Task Later_readonly_transactions_do_not_replace_the_primary_footprint_or_commit_state() =>
        await Dfo.Server.Application.Tests.Commands.ExplicitEffectCommandTests.AssertOrdinaryReplayReadPreservesMainAsync();
    [Theory]
    [InlineData(127, true)] [InlineData(128, false)]
    public async Task Window_counts_every_inbound_frame_including_unknown_types(int otherFrames, bool accepted)
    {
        using var f = Fixture(); await f.Seed(69, 1000, 3); var stream = new Stream();
        await Dispatch(f, stream.Request(1));
        for (var n = 0; n < otherFrames - 1; n++) stream.Counter.Observe((ushort)(2 + n));
        if (accepted) { await Dispatch(f, stream.Request(1)); Assert.Single(f.Sender.Batches); }
        else await Assert.ThrowsAsync<InvalidDataException>(() => Dispatch(f, stream.Request(1)));
        Assert.Equal(2, f.Count(1000));
    }
    [Fact]
    public async Task A_full_ushort_round_is_a_legal_new_use_and_65535_to_zero_is_fresh()
    {
        using var f = Fixture(); await f.Seed(69, 1000, 3); var stream = new Stream();
        await Dispatch(f, stream.Request(1));
        for (var n = 2; n <= ushort.MaxValue; n++) stream.Counter.Observe((ushort)n);
        var (_, fresh) = stream.Counter.Observe(0); Assert.True(fresh);
        await Dispatch(f, stream.Request(1)); Assert.Equal(1, f.Count(1000)); Assert.Empty(f.Sender.Batches);
    }
    [Fact]
    public async Task Batch_IO_failure_keeps_committed_state_and_uses_later_current_quantity()
    {
        using var f = Fixture(); await f.Seed(69, 1000, 3); var stream = new Stream();
        await Dispatch(f, stream.Request(1));
        var cause = new IOException("Fixture batch transport failed after preencoding.");
        var failBatch = true;
        f.Sender.Outbound.FailAny(_ => failBatch && f.Sender.Outbound.BatchInFlight, () => cause);
        Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => Dispatch(f, stream.Request(1))));
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.True(f.Sender.Outbound.Entries.Last().IsClose);
        Assert.Same(f.Connection, f.Sender.Outbound.Entries.Last().Connection);
        Assert.Equal(2, f.Count(1000)); await f.Seed(69, 1000, 7); failBatch = false;
        await Dispatch(f, stream.Request(1)); Assert.Equal(7, f.Count(1000));
        var frame = f.Sender.Batches.Last().Frames.Single(x => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(1)) == 0x000D && x[15] == 0);
        Assert.Equal(7, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(15 + 5 + 3 * 101 + 6)));
    }
    [Fact]
    public async Task Metadata_empty_cannot_bypass_real_dispatcher()
    {
        using var f = Fixture(); await f.Seed(69, 1000, 3);
        await Assert.ThrowsAsync<InvalidDataException>(() => Dispatch(f, new(GameMessageMetadata.Empty,
            new(InventoryContainerType.Main, 69), 0, 1000)));
        Assert.Equal(3, f.Count(1000));
    }

    [Fact]
    public async Task Postdurable_operation_record_failure_preserves_typed_cause_and_exact_connection_fence()
    {
        var fault=new ProbeFault {CloseOnCommit=true};using var f=ProbeFixture(fault);
        var error=await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(()=>SendProbe(f));
        Assert.Equal(TransactionUncertaintyReason.PostCommitPublicationFailure,error.Reason);
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Equal(100001,f.Gold);Assert.Equal(EffectOperationState.Committed,fault.State!.Entry.State);
        var safety=f.Services.GetRequiredService<SelectedSessionSafety>();
        Assert.True(safety.IsBlocked(f.Connection));Assert.True(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)?.Fenced==true);
        await SendProbe(f);Assert.Equal(100001,f.Gold);
    }
    [Fact]
    public async Task Invalid_projection_freeze_aborts_real_SQL_before_durability_and_records_confirmed_rollback()
    {
        var fault=new ProbeFault {Minimum=(ReplayProjectionNeeds)4096};using var f=ProbeFixture(fault);
        await Assert.ThrowsAsync<InvalidDataException>(()=>SendProbe(f));
        Assert.Equal(100000,f.Gold);Assert.Equal(EffectOperationState.ConfirmedRollback,fault.State!.Entry.State);Assert.Null(fault.State.Entry.Projection);
    }
    [Fact]
    public async Task An_ordinary_readonly_unit_does_not_clear_the_primary_execution() =>
        await Dfo.Server.Application.Tests.Commands.ExplicitEffectCommandTests.AssertOrdinaryReplayReadPreservesMainAsync();

    [Fact]
    public async Task Second_primary_command_cannot_pay_or_overwrite_the_first_committed_entry() =>
        await Dfo.Server.Application.Tests.Commands.ExplicitEffectCommandTests.AssertSecondMainPreservesEntryAsync();
    [Fact]
    public async Task Same_ID_new_physical_context_has_an_independent_ledger_and_does_not_clear_the_old_fence()
    {
        using var f = Fixture(); await f.Seed(69, 1000, 3); var stream = new Stream();
        await Dispatch(f, stream.Request(1));
        var (safety, replacement) = await TestGameplay.RunAsync(f.Services, async () =>
        {
            var safety = f.Services.GetRequiredService<SelectedSessionSafety>();
            await safety.BlockAsync(safety.Capture(f.Session,null), new TransactionOutcomeUncertainException(
                TransactionUncertaintyReason.RollbackFailure, "test physical fence", new IOException("unknown")), "probe");
            var registry = f.Services.GetRequiredService<OnlineStateRegistry>();
            Assert.True(registry.TryDisconnect(f.OnlineUser, f.Connection.ConnectionId, registry.GetRequiredUser(f.OnlineUser).Generation));
            var replacement = f.Connection with { };
            var user = registry.RegisterConnection(replacement); registry.Authenticate(user.OnlineUserId, new(1));
            var other = SelectedSessionIdentity.Create(new(1), new(2), replacement.ConnectionId,
                registry.GetRequiredUser(user.OnlineUserId).Generation, 10011);
            registry.Select(other);
            registry.EnterWorld(other);
            var session = SelectedSessionIdentity.Create(new(1), new(1), replacement.ConnectionId,
                registry.GetRequiredUser(user.OnlineUserId).Generation, 10011);
            registry.Select(session);
            registry.EnterWorld(session);
            f.Services.GetRequiredService<ITownAreaDomainService>().Enter(new TownAreaMember(session, new TownAreaKey(1, 0, 0), new CharacterPositionState(0, 0, 0, 0, 0), true));
            return (safety, replacement);
        });
        Assert.True(safety.IsBlocked(f.Connection)); Assert.False(safety.IsBlocked(replacement));
        f.Sender.Outbound.Connections.Remove(replacement.ConnectionId);
        f.Sender.Outbound.Connections.Add(replacement);
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(replacement), new Stream().Request(1), default);
        Assert.Equal(1, f.Count(1000)); Assert.Empty(f.Sender.Batches);
        Assert.True(safety.IsBlocked(f.Connection));
        await Dispatch(f, stream.Request(2)); Assert.Equal(1, f.Count(1000));
    }
    [Fact]
    public async Task Unknown_applied_effect_change_is_rejected_before_SQL_commit()
    {
        var fault=new ProbeFault {Unknown=true};using var f=ProbeFixture(fault);
        var error=await Assert.ThrowsAsync<NotSupportedException>(()=>SendProbe(f));
        Assert.Contains("UnknownEffect/UnknownChange",error.Message);
        Assert.Equal(100000,f.Gold);Assert.Equal(EffectOperationState.ConfirmedRollback,fault.State!.Entry.State);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
    }
    private sealed record UnknownEffect : DomainEffect;
    private sealed record UnknownChange : EffectChange;
    private sealed class UnknownHandler : EffectHandler<UnknownEffect>
    {
        public override ValueTask ValidateAsync(EffectExecutionContext context, UnknownEffect effect, CancellationToken ct) => ValueTask.CompletedTask;
        public override ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, UnknownEffect effect, CancellationToken ct) =>
            ValueTask.FromResult<EffectChange>(new UnknownChange());
    }

    [Fact]
    public async Task Operation_identity_survives_confirmed_rollback_and_committed_replay_but_changes_for_new_operations()
    {
        using var f = Fixture();
        var ledger = f.Services.GetRequiredService<EffectOperationLedger>();
        var stream = new Stream();
        await TestGameplay.RunAsync(f.Services, () =>
        {
            var first = Assert.IsType<EffectAdmission.Selected>(ledger.Admit(f.Connection, stream.Request(1)));
            Assert.NotEqual(Guid.Empty, first.Entry.OperationId);
            first.Entry.State = EffectOperationState.ConfirmedRollback;
            var retry = Assert.IsType<EffectAdmission.Selected>(ledger.Admit(f.Connection, stream.Request(1)));
            Assert.Same(first.Entry, retry.Entry);
            Assert.Equal(first.Entry.OperationId, retry.Entry.OperationId);
            retry.Entry.State = EffectOperationState.Committed;
            var replay = Assert.IsType<EffectAdmission.Replay>(ledger.Admit(f.Connection, stream.Request(1)));
            Assert.Equal(first.Entry.OperationId, replay.Entry.OperationId);
            var next = Assert.IsType<EffectAdmission.Selected>(ledger.Admit(f.Connection, stream.Request(2)));
            Assert.NotEqual(first.Entry.OperationId, next.Entry.OperationId);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_recipient_replaced_after_capture_is_rejected_before_the_ordinal_is_consumed()
    {
        using var f = Fixture();
        var users = new ReplacedAfterCapture(f.Services.GetRequiredService<Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService>());
        using var services = new ServiceCollection()
            .AddSingleton(f.Services.GetRequiredService<ISelectedSessionDomainService>())
            .AddSingleton<Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService>(users).BuildServiceProvider();
        var safety = new SelectedSessionSafety(services,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SelectedSessionSafety>.Instance);
        var ledger = new EffectOperationLedger(safety, f.Services.GetRequiredService<ISelectedSessionDomainService>());
        var request = new Stream().Request(1);
        var calls = 0;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => { ledger.Admit(f.Connection,request); calls++; });
            Assert.Contains("captured recipient", error.Message);
            Assert.Equal(2, users.Queries);
            Assert.Equal(0, calls);
            users.ReplaceAfterFirst = false;
            Assert.IsType<EffectAdmission.Selected>(ledger.Admit(f.Connection,request)); calls++;
            await ValueTask.CompletedTask;
            Assert.Equal(1, calls);
            Assert.Equal(4, users.Queries);
        });
        Assert.Empty(f.Sender.Messages);
    }

    private sealed class ReplacedAfterCapture(Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService inner)
        : Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService
    {
        public int Queries { get; private set; }
        public bool ReplaceAfterFirst { get; set; } = true;
        public Dfo.Server.Data.OnlineUsers.OnlineUserSnapshot GetRequired(Dfo.Server.Data.OnlineUsers.OnlineUserId id) => inner.GetRequired(id);
        public bool TryGetByConnection(NetworkConnectionId id, out Dfo.Server.Data.OnlineUsers.OnlineUserSnapshot? user)
        {
            Queries++;
            var found = inner.TryGetByConnection(id, out user);
            if (ReplaceAfterFirst && Queries > 1 && user is not null) user = user with { Connection = user.Connection with { } };
            return found;
        }
    }

    private sealed record ProbeInput(GameMessageMetadata Metadata,ReplayProjectionNeeds MinimumReplayNeeds):IEffectSettlementRequest
    {public ClientMessageType MessageType=>ClientMessageType.GameExit;}
    private sealed record SettlementProbe(NetworkDispatchContext Dispatch,ProbeInput Input):IProtocolCommand<ProbeResult>
    {public IClientNetworkMessage Message=>Input;public ProtocolFrameMetadata Frame=>new ProtocolFrameMetadata.Game(Input.Metadata);}
    private sealed record ProbeResult;
    private sealed class ProbeFault
    {public bool CloseOnCommit;public bool Unknown;public ReplayProjectionNeeds Minimum=ReplayProjectionNeeds.Inventory;public ExplicitEffectRequest? State;}
    private static ShopFixture ProbeFixture(ProbeFault fault)=>new(services=>{
        foreach(var d in services.Where(d=>d.ImplementationInstance is Dfo.Server.Application.Dispatching.MessageHandlerRegistration {MessageType:ClientMessageType.GameExit}
            || d.ImplementationInstance is CommandHandlerRegistration row&&row.CommandType==typeof(Dfo.Server.Protocol.Game.Exit.GameExitCommand)).ToArray())services.Remove(d);
        services.AddSingleton(fault);services.AddSingleton<IEffectHandler,UnknownHandler>();
        services.AddProtocolCommandHandler<SettlementProbe,ProbeResult,SettlementProbeHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameExit,typeof(ProbeInput)));
        services.RegisterProtocolMessageHandler<ProbeEntry,ProbeInput,SettlementProbe>(ClientMessageType.GameExit);
    });
    private sealed class ProbeEntry(ICommandBus bus):ClientMessageHandler<ProbeInput>,Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<SettlementProbe>
    {protected override async ValueTask HandleMessageAsync(NetworkDispatchContext c,ProbeInput q,CancellationToken ct)=>await bus.SendAsync(new SettlementProbe(c,q),ct);}
    private static Task<ProbeResult> SendProbe(ShopFixture f)=>f.Services.GetRequiredService<ICommandBus>().SendAsync(new SettlementProbe(new(f.Connection),
        new((new GameMessageMetadata(1,3,0,1){InboundOrdinal=1,SequenceIsFresh=true}).WithRawBody([1]),f.Services.GetRequiredService<ProbeFault>().Minimum))).AsTask();
    private sealed class SettlementProbeHandler(ICharacterCarriedGoldTable gold,IDomainTransactionAccessor transactions,ProbeFault fault,IApplyEffectsDomainService effects,ICommandWorkUnits units):ICommandHandler<SettlementProbe,ProbeResult>
    {
        public ValueTask<ProbeResult> HandleAsync(CommandExecutionContext context,SettlementProbe command,CancellationToken ct)
        {
            var state=((CommandRequestContext.ProtocolEffectSelected)context.Request!).State;fault.State=state;var session=state.Recipient.Session;
            return units.WriteAsync(context,new("settlement-probe",[state.Recipient],CommandWorkUnitKind.MainSettlement),async(unit,token)=>{
                await gold.AddAsync(session.CharacterId,1,int.MaxValue,token);
                if(fault.Unknown)await effects.ApplyAsync(new(session.AccountId,session.CharacterId,DateTimeOffset.UtcNow,unit.Footprint,session),[new UnknownEffect()],token);
                if(fault.CloseOnCommit)transactions.Current!.Enlist(this,()=>new CloseFrame(context.Request.Close));
                return new ProbeResult();
            },ct);
        }
    }
    private sealed class CloseFrame(Action close) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct) { close(); return ValueTask.CompletedTask; }
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
    private static ShopFixture Fixture() => new(services => services.AddSingleton<IItemUseRuleDefinitionSource>(
        new FakeItemUseRules((1000, ItemUseRules.Potion(0)))));
    private static Task Dispatch(ShopFixture f, UseStackableRequest request) =>
        f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection), request, default).AsTask();
    private static byte[] Body()
    {
        var body = new byte[11]; BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(1), 69);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(7), 1000); return body;
    }
    private sealed class Stream
    {
        public GameInboundFrameCounter Counter { get; } = new();
        public UseStackableRequest Request(ushort sequence)
        {
            var (ordinal, fresh) = Counter.Observe(sequence);
            return new((new GameMessageMetadata(1, 0x002C, 0, sequence) { InboundOrdinal = ordinal, SequenceIsFresh = fresh }).WithRawBody(Body()),
                new(InventoryContainerType.Main, 69), 0, 1000);
        }
    }

}
