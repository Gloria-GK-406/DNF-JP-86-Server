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

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class EffectOperationReplayTests
{
    private sealed class LoveContent : IMarriageCeremonyContentSource
    { public MarriageCeremonyContent Load() => new([], []) { LovePointItems = new Dictionary<int, int> { [1000] = 1 }, MaximumLovePoints = 999 }; }
    [Fact]
    public async Task Love_coupon_real_dispatcher_lost_ack_replays_current_inventory_and_points_without_second_deduction()
    {
        using var f = new ShopFixture(s => {
            s.AddSingleton<IItemUseRuleDefinitionSource>(new FakeItemUseRules((1000, ItemUseRules.Potion(0) with { Action = "[love point coupon]" })));
            s.AddSingleton<IMarriageCeremonyContentSource, LoveContent>();
        });
        f.Sql("INSERT INTO accounts(account_id,m_id) VALUES(2,'love-peer'); INSERT INTO characters(character_id,account_id,name,job,level,slot_index) VALUES(3,2,'peer',0,50,0)");
        var peer = SelectedSessionIdentity.Create(new(2), new(3), new("offline-peer"), 1, 10011);
        var at = f.Clock.GetUtcNow();
        var relation = new MarriageProposal(Guid.NewGuid(), f.Session, peer, MarriageProposalState.Pending, 1, at, at.AddMinutes(1));
        var ceremony = new MarriageCeremony(Guid.NewGuid(), relation.Id, f.Session, peer, 0, 0, MarriageCeremonyState.Pending, 1, at, at.AddMinutes(1));
        await using (var tx = ((Dfo.Server.Application.Transactions.DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin())
        {
            var relations = f.Services.GetRequiredService<IMarriageProposalDomainStore>(); await relations.CreateAsync(relation, default);
            Assert.True(await relations.TransitionAsync(relation, MarriageProposalState.Engaged, 0, default));
            await f.Services.GetRequiredService<IMarriageCeremonyDomainStore>().CreateAsync(ceremony, default);
            var weddings = f.Services.GetRequiredService<IMarriageWeddingDomainStore>(); await weddings.CreateAsync(new(ceremony, MarriageWeddingPhase.Preparing, 0), default);
            Assert.True(await weddings.FinishAsync(ceremony.Id, MarriageWeddingPhase.Completed, at, default)); await tx.CommitAsync(default);
        }
        f.Sql($"INSERT INTO marriage_relations(id,stage,revision,stage_since,ceremony_id) VALUES('{relation.Id:N}',4,1,{at.ToUnixTimeMilliseconds()},'{ceremony.Id:N}')");
        await f.Seed(69, 1000, 3);
        var counter = new GameInboundFrameCounter();
        WeddingLovePointRequest Request()
        {
            var (ordinal, fresh) = counter.Observe(1); var body = new byte[18]; BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(14), 69);
            return new((new GameMessageMetadata(1, 0x03F5, 0, 1) { InboundOrdinal = ordinal, SequenceIsFresh = fresh }).WithRawBody(body), 69);
        }
        var dispatcher = f.Services.GetRequiredService<INetworkMessageDispatcher>(); f.Sender.FailExplicit = true;
        await Assert.ThrowsAsync<IOException>(async () => await dispatcher.DispatchAsync(new(f.Connection), Request(), default));
        Assert.Equal(2, f.Count(1000)); Assert.Equal(1, f.Scalar("SELECT points FROM marriage_personal_points WHERE character_id=1"));
        await dispatcher.DispatchAsync(new(f.Connection), Request(), default);
        Assert.Equal(2, f.Count(1000)); Assert.Equal(1, f.Scalar("SELECT points FROM marriage_personal_points WHERE character_id=1"));
        var batch = Assert.Single(f.Sender.Batches);
        Assert.DoesNotContain(batch.Frames, frame => frame[0] == 1 && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == 0x03F5);
        var points = Assert.Single(batch.Frames, frame => frame[0] == 0 && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == 0x03D3);
        Assert.Equal(Convert.FromHexString("0100000001000000"), points[15..]);
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
        fixture.Sender.FailExplicit = true;
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
        await Assert.ThrowsAsync<IOException>(async () => await dispatcher.DispatchAsync(new(fixture.Connection), Request(), default));
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
    [Fact]
    public async Task An_inherited_closed_operation_cannot_downgrade_to_trusted_direct_bus()
    {
        using var f = Fixture(); await f.Seed(69, 1000, 3); var stream = new Stream();
        var inherited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? child = null;
        f.Sender.BeforeExplicit = () => child = Task.Run(async () =>
        {
            inherited.SetResult(); await release.Task;
            await f.Services.GetRequiredService<ICommandBus>().SendAsync(new UseStackableCommand(f.Session,
                new InventorySlotMappingRuleService().ToBusinessSlot(new(InventoryContainerType.Main, 69)), 1000));
        });
        await Dispatch(f, stream.Request(1)); await inherited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult(); await Assert.ThrowsAsync<InvalidOperationException>(async () => await child!);
        Assert.Equal(2, f.Count(1000));
    }
    [Fact]
    public async Task Later_readonly_transactions_do_not_replace_the_primary_footprint_or_commit_state()
    {
        using var f = Fixture(); await f.Seed(69, 1000, 1); var stream = new Stream();
        f.Sender.BeforeExplicit = () => f.Services.GetRequiredService<ICommandBus>().SendAsync(
            new ReadReplayStateCommand(f.Session, new(ReplayProjectionNeeds.Inventory, []))).AsTask().GetAwaiter().GetResult();
        await Dispatch(f, stream.Request(1)); Assert.Equal(0, f.Count(1000));
        await Dispatch(f, stream.Request(1)); Assert.Equal(0, f.Count(1000));
        var batch = Assert.Single(f.Sender.Batches);
        var cleared = Assert.Single(batch.Frames, x => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(1)) == 0x000E);
        Assert.Equal(69, BinaryPrimitives.ReadInt16LittleEndian(cleared.AsSpan(18)));
        Assert.Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(cleared.AsSpan(20)));
    }
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
        await Dispatch(f, stream.Request(1)); f.Sender.FailBatch = true;
        await Assert.ThrowsAsync<IOException>(() => Dispatch(f, stream.Request(1)));
        Assert.Equal(2, f.Count(1000)); await f.Seed(69, 1000, 7); f.Sender.FailBatch = false;
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
        var fault = new ProbeFault();
        using var f = new ShopFixture(services => {
            services.AddSingleton(fault);
            services.AddCommandHandler<SettlementProbe, ProbeResult, SettlementProbeHandler>(CommandExecutionKind.Transactional,
                accountAccessMode: UserAccessMode.Shared);
        });
        var safety = f.Services.GetRequiredService<SelectedSessionSafety>();
        var entry = new EffectOperationEntry(f.Session, 1, 1, 0x002C, 1, new string('A', 64), 11);
        using var operation = f.Services.GetRequiredService<EffectOperationContext>().Enter(entry, safety.Capture(f.Session), ReplayProjectionNeeds.Inventory);
        fault.CloseOnCommit = operation.Dispose;
        var error = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(async () =>
            await f.Services.GetRequiredService<ICommandBus>().SendAsync(new SettlementProbe(f.Session)));
        Assert.Equal(TransactionUncertaintyReason.PostCommitPublicationFailure, error.Reason);
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Contains("already closed", error.InnerException!.Message);
        Assert.Equal(100001, f.Gold); Assert.Equal(EffectOperationState.Blocked, entry.State);
        Assert.True(safety.IsBlocked(f.Connection)); Assert.True(safety.IsBlocked(f.Session));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await f.Services.GetRequiredService<ICommandBus>().SendAsync(new SettlementProbe(f.Session)));
        Assert.Equal(100001, f.Gold);
    }
    [Fact]
    public async Task Invalid_projection_freeze_aborts_real_SQL_before_durability_and_records_confirmed_rollback()
    {
        using var f = new ShopFixture(services => {
            services.AddSingleton(new ProbeFault());
            services.AddCommandHandler<SettlementProbe, ProbeResult, SettlementProbeHandler>(CommandExecutionKind.Transactional,
                accountAccessMode: UserAccessMode.Shared);
        });
        var entry = new EffectOperationEntry(f.Session, 1, 1, 0x002C, 1, new string('A', 64), 11);
        using var operation = f.Services.GetRequiredService<EffectOperationContext>().Enter(entry,
            f.Services.GetRequiredService<SelectedSessionSafety>().Capture(f.Session), (ReplayProjectionNeeds)4096);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await f.Services.GetRequiredService<ICommandBus>().SendAsync(new SettlementProbe(f.Session)));
        Assert.Equal(100000, f.Gold); Assert.Equal(EffectOperationState.ConfirmedRollback, entry.State);
        Assert.Null(entry.Projection);
    }
    [Fact]
    public async Task An_inherited_readonly_scope_does_not_clear_the_primary_execution_on_the_parent_flow()
    {
        using var f = Fixture();
        var context = f.Services.GetRequiredService<EffectOperationContext>();
        var entry = new EffectOperationEntry(f.Session, 1, 1, 0x002C, 1, new string('A', 64), 11);
        using var operation = context.Enter(entry, f.Services.GetRequiredService<SelectedSessionSafety>().Capture(f.Session), ReplayProjectionNeeds.Inventory);
        var command = new UseStackableCommand(f.Session, new(BusinessInventorySpace.Consumable, 4), 1000);
        var execution = new CommandExecutionContext(Guid.NewGuid(), command.GetType(), typeof(object), CommandExecutionKind.Transactional);
        using var main = context.BeginMain(execution, command);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var child = Task.Run(async () => {
            var read = new ReadReplayStateCommand(f.Session, new(ReplayProjectionNeeds.Inventory, []));
            using var readonlyScope = context.BeginMain(new(Guid.NewGuid(), read.GetType(), typeof(object), CommandExecutionKind.Transactional), read);
            entered.SetResult(); await release.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { context.TransactionStarted(execution, command); context.Freeze(execution, command); context.Settled(execution, command); }
        finally { release.SetResult(); }
        await child; Assert.Equal(EffectOperationState.Committed, entry.State); Assert.NotNull(entry.Projection);
    }

    [Fact]
    public async Task Waiting_on_the_connection_gate_keeps_the_original_selected_session()
    {
        using var f = Fixture(); await f.Seed(69, 1000, 3); var stream = new Stream();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        f.Sender.BeforeExplicit = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); };
        var first = Task.Run(async () => await Dispatch(f, stream.Request(1)));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        // The synchronous invocation reaches Gate.WaitAsync after capturing the original session.
        var pending = Dispatch(f, stream.Request(1)); Assert.False(pending.IsCompleted);
        f.Select(2); release.Set(); await first;
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Equal(0, f.Count(1000)); Assert.Empty(f.Sender.Batches);
        Assert.Equal(2, f.Scalar("SELECT count FROM item_slots WHERE owner_id=1 AND list_type=0 AND slot_index=69"));
    }
    [Fact]
    public async Task Second_primary_command_cannot_pay_or_overwrite_the_first_committed_entry()
    {
        using var f = Fixture(); await f.Seed(69, 1000, 3); var stream = new Stream();
        f.Sender.BeforeExplicit = () => f.Services.GetRequiredService<ICommandBus>().SendAsync(new UseStackableCommand(f.Session,
            new InventorySlotMappingRuleService().ToBusinessSlot(new(InventoryContainerType.Main, 69)), 1000)).AsTask().GetAwaiter().GetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Dispatch(f, stream.Request(1)));
        Assert.Equal(2, f.Count(1000)); f.Sender.BeforeExplicit = null;
        await Dispatch(f, stream.Request(1)); Assert.Equal(2, f.Count(1000)); Assert.Single(f.Sender.Batches);
    }
    [Fact]
    public async Task Same_ID_new_physical_context_has_an_independent_ledger_and_does_not_clear_the_old_fence()
    {
        using var f = Fixture(); await f.Seed(69, 1000, 3); var stream = new Stream();
        await Dispatch(f, stream.Request(1));
        var safety = f.Services.GetRequiredService<SelectedSessionSafety>();
        await safety.BlockAsync(safety.Capture(f.Session), new TransactionOutcomeUncertainException(
            TransactionUncertaintyReason.RollbackFailure, "test physical fence", new IOException("unknown")), "probe");
        var registry = f.Services.GetRequiredService<OnlineStateRegistry>();
        Assert.True(registry.TryDisconnect(f.OnlineUser, f.Connection.ConnectionId, registry.GetRequiredUser(f.OnlineUser).Generation));
        var replacement = f.Connection with { };
        var user = registry.RegisterConnection(replacement); registry.Authenticate(user.OnlineUserId, new(1));
        registry.Publish(SelectedSessionIdentity.Create(new(1), new(2), replacement.ConnectionId,
            registry.GetRequiredUser(user.OnlineUserId).Generation, 10011));
        var session = SelectedSessionIdentity.Create(new(1), new(1), replacement.ConnectionId,
            registry.GetRequiredUser(user.OnlineUserId).Generation, 10011);
        registry.Publish(session); f.Services.GetRequiredService<ISelectedSessionDomainService>().Select(session);
        f.Services.GetRequiredService<ITownAreaDomainService>().Enter(new TownAreaMember(session.CharacterId, session.TownAreaLease(),
            new TownAreaKey(1, 0, 0), new CharacterPositionState(0, 0, 0, 0, 0), true));
        Assert.True(safety.IsBlocked(f.Connection)); Assert.False(safety.IsBlocked(replacement));
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(replacement), new Stream().Request(1), default);
        Assert.Equal(1, f.Count(1000)); Assert.Empty(f.Sender.Batches);
        Assert.True(safety.IsBlocked(f.Connection));
        await Dispatch(f, stream.Request(2)); Assert.Equal(1, f.Count(1000));
    }
    [Fact]
    public async Task Unknown_applied_effect_change_is_rejected_before_SQL_commit()
    {
        using var f = new ShopFixture(services => {
            services.AddSingleton(new ProbeFault()); services.AddSingleton<IEffectHandler, UnknownHandler>();
            services.AddCommandHandler<SettlementProbe, ProbeResult, SettlementProbeHandler>(CommandExecutionKind.Transactional,
                accountAccessMode: UserAccessMode.Shared);
        });
        var entry = new EffectOperationEntry(f.Session, 1, 1, 0x002C, 1, new string('A', 64), 11);
        using var operation = f.Services.GetRequiredService<EffectOperationContext>().Enter(entry,
            f.Services.GetRequiredService<SelectedSessionSafety>().Capture(f.Session), ReplayProjectionNeeds.Inventory);
        var error = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await f.Services.GetRequiredService<ICommandBus>().SendAsync(new SettlementProbe(f.Session, Unknown: true)));
        Assert.Contains("UnknownEffect/UnknownChange", error.Message);
        Assert.Equal(100000, f.Gold); Assert.Equal(EffectOperationState.ConfirmedRollback, entry.State);
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
    public async Task Refusal_only_unselected_operation_cannot_pay_after_a_selection_arrives()
    {
        using var f = Fixture(); await f.Seed(69, 1000, 3);
        f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(f.Session);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = f.Services.GetRequiredService<EffectOperationLedger>().DispatchAsync(f.Connection, new Stream().Request(1), async () => {
            entered.SetResult(); await release.Task;
            await f.Services.GetRequiredService<ICommandBus>().SendAsync(new UseStackableCommand(f.Session,
                new InventorySlotMappingRuleService().ToBusinessSlot(new(InventoryContainerType.Main, 69)), 1000));
        }, default).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(f.Session.CharacterId);
        f.Select(1); release.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Equal(3, f.Count(1000)); Assert.Empty(f.Sender.Messages);
    }
    private sealed record SettlementProbe(SelectedSessionIdentity Session, bool Unknown = false) : ICommand<ProbeResult>, IRequestSettlementCommand, IAccountCommand
    { public AccountId AccountId => Session.AccountId; }
    private sealed record ProbeResult;
    private sealed class ProbeFault { public Action? CloseOnCommit; }
    private sealed class SettlementProbeHandler(ICharacterCarriedGoldTable gold, IDomainTransactionAccessor transactions, ProbeFault fault, IApplyEffectsDomainService effects)
        : ICommandHandler<SettlementProbe, ProbeResult>
    {
        public async ValueTask<ProbeResult> HandleAsync(CommandExecutionContext context, SettlementProbe command, CancellationToken ct)
        {
            await gold.AddAsync(command.Session.CharacterId, 1, int.MaxValue, ct);
            if (command.Unknown) await effects.ApplyAsync(new(command.Session.AccountId, command.Session.CharacterId,
                DateTimeOffset.UtcNow, command.Session), [new UnknownEffect()], ct);
            if (fault.CloseOnCommit is { } close) transactions.Current!.Enlist(this, () => new CloseFrame(close));
            return new();
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
