using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.UseStackable;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Commands;

public sealed class ExplicitEffectCommandTests
{
    internal static async Task AssertSecondMainPreservesEntryAsync()
    {
        using var world = World(out var settings);
        await world.Seed(69, 1000, 3);
        var error = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => Send(world, Mode.DuplicateMain).AsTask());
        Assert.Equal(2, world.Count(1000));
        Assert.Equal(0, settings.LaterWrites);
        Assert.Equal(EffectOperationState.Committed, settings.State!.Entry.State);
        Assert.Same(settings.OriginalProjection, settings.State.Entry.Projection);
        Assert.True(Contains(error, settings.InitialError!));
        Assert.True(world.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(world.Connection));
        Assert.True(world.Sender.Outbound.Entries.Last().IsClose);
        Assert.Same(world.Connection, world.Sender.Outbound.Entries.Last().Connection);
    }

    internal static async Task AssertOrdinaryReplayReadPreservesMainAsync()
    {
        using var world = World(out var settings);
        await world.Seed(69, 1000, 1);
        var input = WholeUse.Input(new(InventoryContainerType.Main, 69), 1000);
        await Send(world, Mode.ReadAfterMain, input);
        var entry = settings.State!.Entry;
        Assert.Equal(0, world.Count(1000));
        Assert.Same(settings.OriginalProjection, entry.Projection);
        Assert.Contains(new BusinessSlot(BusinessInventorySpace.Consumable, 4), entry.Projection!.TouchedSlots);
        var retry = input with { Metadata = input.Metadata with { InboundOrdinal = input.Metadata.InboundOrdinal + 1, SequenceIsFresh = false } };
        await Send(world, Mode.ReadAfterMain, retry);
        Assert.Equal(0, world.Count(1000));
        var frame = Assert.Single(Assert.Single(world.Sender.Batches).Frames,
            x => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(1)) == 0x000E);
        Assert.Equal(69, System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(18)));
        Assert.Equal(-1, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(20)));
        Assert.Same(settings.OriginalProjection, entry.Projection);
        Assert.Equal(EffectOperationState.Committed, entry.State);
    }

    [Theory]
    [InlineData(Mode.Commit)]
    [InlineData(Mode.Refusal)]
    public async Task A_selected_main_commits_its_minimum_projection_even_for_an_ordinary_refusal(Mode mode)
    {
        using var world = World(out var settings);
        await world.Seed(69, 1000, 3);
        Assert.Same(world.Services.GetRequiredService<ICommandMessageOutbox>(), world.Sender.Outbound.Network);
        Assert.Equal(1, await Send(world, mode));
        Assert.Equal(mode == Mode.Commit ? 2 : 3, world.Count(1000));
        Assert.Equal(EffectOperationState.Committed, settings.State!.Entry.State);
        Assert.True(settings.State.Entry.Projection!.Needs.HasFlag(ReplayProjectionNeeds.Inventory));
        Assert.True(settings.Context!.Request!.IsClosed);
    }

    [Theory]
    [InlineData(Mode.ReadOnlyMain)]
    [InlineData(Mode.MissingReporter)]
    [InlineData(Mode.WrongOwnerEmptySlots)]
    [InlineData(Mode.WrongOwnerRental)]
    public async Task Invalid_main_or_footprint_authority_never_changes_real_storage(Mode mode)
    {
        using var world = World(out var settings);
        await world.Seed(69, 1000, 3);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Send(world, mode).AsTask());
        Assert.Equal(3, world.Count(1000));
        Assert.NotEqual(EffectOperationState.Committed, settings.State!.Entry.State);
    }

    [Fact]
    public async Task An_unclaimed_selected_refusal_is_rejected_without_acquiring_settlement_rights()
    {
        using var world = World(out var settings);
        await world.Seed(69, 1000, 3);
        var input = WholeUse.Input(new(InventoryContainerType.Main, 69), 1000);
        Assert.Equal(1, await Send(world, Mode.NoMain, input));
        Assert.Equal(EffectOperationState.Rejected, settings.State!.Entry.State);
        Assert.Equal(3, world.Count(1000));
        Assert.Null(settings.Footprint);
        var retry = input with { Metadata = input.Metadata with { InboundOrdinal = input.Metadata.InboundOrdinal + 1, SequenceIsFresh = false } };
        await Assert.ThrowsAsync<InvalidDataException>(() => Send(world, Mode.Commit, retry).AsTask());
        Assert.Equal(3, world.Count(1000));
    }

    [Fact]
    public async Task Confirmed_rollback_retry_reuses_the_original_entry_and_ordinal()
    {
        using var world = World(out var settings);
        await world.Seed(69, 1000, 3);
        var input = WholeUse.Input(new(InventoryContainerType.Main, 69), 1000);
        await Send(world, Mode.Rollback, input);
        var entry = settings.State!.Entry;
        Assert.Equal(EffectOperationState.ConfirmedRollback, entry.State);
        Assert.Equal(3, world.Count(1000));
        var retry = input with { Metadata = input.Metadata with { InboundOrdinal = input.Metadata.InboundOrdinal + 1, SequenceIsFresh = false } };
        await Send(world, Mode.Commit, retry);
        Assert.Same(entry, settings.State!.Entry);
        Assert.Equal(input.Metadata.InboundOrdinal, entry.Ordinal);
        Assert.Equal(EffectOperationState.Committed, entry.State);
        Assert.Equal(2, world.Count(1000));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_permitted_encoder_failure_is_raw_only_after_all_scope_cleanup_succeeds(bool cleanupFails)
    {
        using var world = World(out var settings);
        settings.DisposeFailure = cleanupFails ? settings.Cause : null;
        world.Sender.Outbound.FailDirect(world.Connection.ConnectionId, () => settings.Cause);
        await world.Seed(69, 1000, 3);
        var error = await Record.ExceptionAsync(() => Send(world, Mode.Register).AsTask());
        Assert.NotNull(error);
        Assert.Equal(2, world.Count(1000));
        Assert.Equal(EffectOperationState.Committed, settings.State!.Entry.State);
        Assert.Equal(cleanupFails, world.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(world.Connection));
        if (cleanupFails)
        {
            var typed = Assert.IsType<TransactionOutcomeUncertainException>(error);
            var independent = Assert.IsType<AggregateException>(typed.InnerException);
            Assert.Equal(2, independent.InnerExceptions.Count);
            Assert.All(independent.InnerExceptions, cause => Assert.Same(settings.Cause, cause));
        }
        else Assert.Same(settings.Cause, error);
        Assert.True(Contains(error, settings.Cause));
    }

    [Theory]
    [InlineData(Mode.BusinessFailure)]
    [InlineData(Mode.CatchAndWrite)]
    [InlineData(Mode.CatchAndReturn)]
    public async Task Postcommit_business_or_swallowed_registration_errors_keep_the_fence_and_original_cause(Mode mode)
    {
        using var world = World(out var settings);
        await world.Seed(69, 1000, 3);
        if (mode != Mode.BusinessFailure)
            world.Sender.Outbound.FailDirect(world.Connection.ConnectionId, () => settings.Cause);
        var error = await Record.ExceptionAsync(() => Send(world, mode).AsTask());
        Assert.NotNull(error);
        // A caught actual encoder fault stays failed and cannot start a later storage write.
        Assert.Equal(2, world.Count(1000));
        Assert.Equal(0, settings.LaterWrites);
        Assert.True(Contains(error, settings.Cause));
        if (mode == Mode.BusinessFailure)
        {
            Assert.True(TransactionOutcomeUncertainException.Contains(error));
            Assert.True(world.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(world.Connection));
        }
        else Assert.Same(settings.Cause, error);
    }

    [Fact]
    public async Task Wrapper_callback_with_the_same_IO_exception_has_no_encoder_provenance()
    {
        using var world = World(out var settings);
        await world.Seed(69, 1000, 3);
        world.Sender.Outbound.BeforeExplicit = () => throw settings.Cause;
        var error = await Record.ExceptionAsync(() => Send(world, Mode.Register).AsTask());
        Assert.Equal(2, world.Count(1000));
        Assert.Equal(EffectOperationState.Committed, settings.State!.Entry.State);
        Assert.True(world.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(world.Connection));
        Assert.True(TransactionOutcomeUncertainException.Contains(error!));
        Assert.True(Contains(error!, settings.Cause));
    }


    [Fact]
    public async Task An_unselected_request_only_acknowledges_and_does_not_consume_the_ledger_ordinal()
    {
        using var world = World(out _);
        await world.Seed(69, 1000, 3);
        TestGameplay.Run(world.Services, () => world.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(world.Session));
        var input = WholeUse.Input(new(InventoryContainerType.Main, 69), 1000);
        var bus = world.Services.GetRequiredService<ICommandBus>();
        var result = await bus.SendAsync(new GameUseStackableCommand(new(world.Connection), input));
        Assert.Equal(ItemUseStatus.NoSession, result.Status);
        var ack = Assert.IsType<UseStackableResponse>(Assert.Single(world.Sender.Messages));
        Assert.Equal(UseStackableResponse.Silent, ack.ErrorCode);
        Assert.Equal(3, world.Count(1000));
        var restored = world.Session with { ConnectionGeneration = world.Services.GetRequiredService<OnlineStateRegistry>().GetRequiredUser(world.OnlineUser).Generation };
        TestGameplay.Run(world.Services, () => { var sessions = world.Services.GetRequiredService<ISelectedSessionDomainService>(); sessions.Select(restored); sessions.EnterWorld(restored); });
        Assert.Equal(ItemUseStatus.Used, (await bus.SendAsync(new GameUseStackableCommand(new(world.Connection), input))).Status);
        Assert.Equal(2, world.Count(1000));
    }

    [Fact]
    public async Task A_selection_arriving_after_rejected_admission_cannot_acquire_a_main()
    {
        using var world = World(out var settings);
        await world.Seed(69, 1000, 3);
        settings.OriginalRecipient = TestGameplay.Run(world.Services, () => world.Services.GetRequiredService<SelectedSessionSafety>().Capture(world.Session, null));
        TestGameplay.Run(world.Services, () => world.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(world.Session));
        settings.OriginalRecipient = settings.OriginalRecipient with { Session = settings.OriginalRecipient.Session with
            { ConnectionGeneration = world.Services.GetRequiredService<OnlineStateRegistry>().GetRequiredUser(world.OnlineUser).Generation } };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Send(world, Mode.UpgradeRejected).AsTask());
        Assert.Equal("Main requires a selected effect request.", error.Message);
        Assert.Equal(settings.OriginalRecipient.Session, world.Services.GetRequiredService<ISelectedSessionDomainService>().FindByConnection(world.Connection.ConnectionId));
        Assert.Equal(3, world.Count(1000));
        Assert.Equal(0, settings.LaterWrites);
        Assert.IsType<CommandRequestContext.ProtocolEffectRejected>(settings.Context!.Request);
    }

    [Fact]
    public async Task Sequential_requests_share_the_singleton_but_never_share_a_footprint_or_failed_state()
    {
        using var world = World(out var settings);
        await world.Seed(69, 1000, 4);
        await Send(world, Mode.Commit);
        var first = settings.State;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Send(world, Mode.WrongOwnerRental).AsTask());
        var failed = settings.State;
        await Send(world, Mode.Commit);
        Assert.NotSame(first, settings.State);
        Assert.NotSame(failed, settings.State);
        Assert.Equal(2, world.Count(1000));
        Assert.Equal(EffectOperationState.Committed, first!.Entry.State);
        Assert.Equal(EffectOperationState.ConfirmedRollback, failed!.Entry.State);
        Assert.Equal(EffectOperationState.Committed, settings.State!.Entry.State);
        Assert.Throws<InvalidOperationException>(() => settings.Footprint!.RecordInventorySlots(world.Session, []));
    }


    [Fact]
    public async Task An_ordinary_registration_with_a_real_encoder_failure_keeps_the_default_fence()
    {
        using var world = World(out var settings);
        await world.Seed(69, 1000, 3);
        world.Sender.Outbound.FailDirect(world.Connection.ConnectionId, () => settings.Cause);
        var error = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => Send(world, Mode.OrdinaryRegister).AsTask());
        Assert.True(Contains(error, settings.Cause));
        Assert.Equal(2, world.Count(1000));
        Assert.True(world.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(world.Connection));
        Assert.Equal(EffectOperationState.Committed, settings.State!.Entry.State);
    }

    [Fact]
    public async Task A_same_ID_replacement_at_registration_cannot_borrow_the_original_attempt()
    {
        using var world = World(out var settings);
        await world.Seed(69, 1000, 3);
        var replacement = world.Connection with { };
        world.Sender.Outbound.BeforeExplicit = () => {
            world.Sender.Outbound.Connections.Remove(replacement.ConnectionId);
            world.Sender.Outbound.Connections.Add(replacement);
        };
        var error = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => Send(world, Mode.Register).AsTask());
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Equal(2, world.Count(1000));
        var safety = world.Services.GetRequiredService<SelectedSessionSafety>();
        Assert.True(safety.IsBlocked(world.Connection));
        Assert.False(safety.IsBlocked(replacement));
        Assert.Equal(EffectOperationState.Committed, settings.State!.Entry.State);
    }

    [Fact]
    public async Task A_swallowed_wrapper_failure_cannot_finish_successfully_or_open_a_later_unit()
    {
        using var world = World(out var settings);
        await world.Seed(69, 1000, 3);
        world.Sender.Outbound.BeforeExplicit = () => throw settings.Cause;
        await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => Send(world, Mode.CatchAndWrite).AsTask());
        Assert.Equal(2, world.Count(1000));
        Assert.Equal(0, settings.LaterWrites);
        Assert.True(world.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(world.Connection));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_main_can_report_another_accounts_exact_participant_and_default_failure_fences_both(bool fail)
    {
        using var world = World(out var settings);
        await world.Seed(69, 1000, 3);
        world.Sql("INSERT INTO accounts(account_id,m_id) VALUES(2,'effect-peer'); INSERT INTO characters(character_id,account_id,name,job,level,slot_index) VALUES(3,2,'peer',0,50,0)");
        var connection = new NetworkConnectionContext(new("effect-peer"), NetworkEndpointKind.Game, 10011);
        world.Sender.Outbound.Connections.Add(connection);
        settings.OtherRecipient = TestGameplay.Run(world.Services, () => {
            var registry = world.Services.GetRequiredService<OnlineStateRegistry>();
            var user = registry.RegisterConnection(connection); registry.Authenticate(user.OnlineUserId, new(2));
            var peer = Dfo.Server.Data.World.SelectedSessionIdentity.Create(new(2), new(3), connection.ConnectionId,
                registry.GetRequiredUser(user.OnlineUserId).Generation, 10012);
            registry.Select(peer); registry.EnterWorld(peer);
            return world.Services.GetRequiredService<SelectedSessionSafety>().Capture(peer, null);
        });
        if (fail)
        {
            var error = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => Send(world, Mode.MultiFailure).AsTask());
            Assert.True(Contains(error, settings.Cause));
        }
        else Assert.Equal(1, await Send(world, Mode.MultiCommit));
        Assert.Equal(2, world.Count(1000));
        Assert.Equal(EffectOperationState.Committed, settings.State!.Entry.State);
        var safety = world.Services.GetRequiredService<SelectedSessionSafety>();
        Assert.Equal(fail, safety.IsBlocked(world.Connection));
        Assert.Equal(fail, safety.IsBlocked(connection));
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_or_gone_required_delivery_completes_its_attempt_without_encoder_provenance(bool gone)
    {
        using var world = World(out var settings);
        await world.Seed(69, 1000, 3);
        if (gone)
            world.Sender.Outbound.BeforeExplicit = () => {
                world.Sender.Outbound.Connections.AddOnFirstSight = false;
                world.Sender.Outbound.Connections.Remove(world.Connection.ConnectionId);
            };
        Assert.Equal(1, await Send(world, gone ? Mode.Register : Mode.EmptyExplicit));
        Assert.Equal(2, world.Count(1000));
        Assert.Equal(EffectOperationState.Committed, settings.State!.Entry.State);
        Assert.False(world.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(world.Connection));
        Assert.Empty(world.Sender.Outbound.Entries);
    }


    [Fact]
    public async Task A_real_completed_attempt_cannot_register_twice_in_the_same_request()
    {
        using var world = World(out var settings);
        TrackEncoder(world, settings);
        await world.Seed(69, 1000, 3);
        var error = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => Send(world, Mode.DoubleRegister).AsTask());
        Assert.Equal(2, world.Count(1000));
        Assert.Equal(1, settings.EncoderCalls);
        Assert.NotNull(settings.Attempt);
        Assert.Equal(EffectOperationState.Committed, settings.State!.Entry.State);
        Assert.Same(settings.OriginalProjection, settings.State.Entry.Projection);
        Assert.True(Contains(error, settings.InitialError!));
        Assert.True(world.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(world.Connection));
        Assert.True(world.Sender.Outbound.Entries.Last().IsClose);
    }

    [Fact]
    public async Task A_second_real_request_cannot_borrow_the_previous_requests_attempt_or_commit()
    {
        using var world = World(out var settings);
        TrackEncoder(world, settings);
        await world.Seed(69, 1000, 3);
        await Send(world, Mode.Register);
        var previous = settings.State!.Entry;
        var token = settings.Attempt;
        Assert.Equal(1, settings.EncoderCalls);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Send(world, Mode.PreviousAttempt).AsTask());
        Assert.Equal("The command request is closed or belongs to another job.", error.Message);
        Assert.Same(token, settings.Attempt);
        Assert.NotSame(previous, settings.State!.Entry);
        Assert.Equal(EffectOperationState.Committed, previous.State);
        Assert.Equal(EffectOperationState.Rejected, settings.State.Entry.State);
        Assert.Equal(2, world.Count(1000));
        Assert.Equal(1, settings.EncoderCalls);
        Assert.False(world.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(world.Connection));
    }

    [Fact]
    public async Task A_real_live_attempt_is_rejected_on_another_job_before_encoding_and_the_owner_is_drained()
    {
        using var world = World(out var settings);
        TrackEncoder(world, settings);
        await world.Seed(69, 1000, 3);
        var pending = Send(world, Mode.HoldAttempt).AsTask();
        Exception? completed = null;
        InvalidOperationException? foreign = null;
        try
        {
            await settings.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(settings.Context!.Request!.IsClosed);
            Assert.Equal(0, settings.EncoderCalls);
            // Deliberate malicious cross-job use of the same provider's real outbox, not a new production entry.
            foreign = await Assert.ThrowsAsync<InvalidOperationException>(() => TestGameplay.RunAsync(async () => {
                Assert.NotEqual(settings.JobId, GameplayJob.Current!.Value.Id);
                await world.Services.GetRequiredService<IGameCommandOutbox>().RegisterExplicitAsync(world.Connection.ConnectionId,
                    new([UseStackableResponse.Success(new(InventoryContainerType.Main, 69), 0, 1000)], []), settings.Attempt!, default);
            }));
            Assert.Equal(0, settings.EncoderCalls);
        }
        finally
        {
            settings.Release.TrySetResult();
            completed = await Record.ExceptionAsync(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        Assert.IsType<TransactionOutcomeUncertainException>(completed);
        Assert.True(Contains(completed!, foreign!));
        Assert.Equal(0, settings.EncoderCalls);
        Assert.Equal(2, world.Count(1000));
        Assert.Equal(EffectOperationState.Committed, settings.State!.Entry.State);
        Assert.True(world.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(world.Connection));
        Assert.True(pending.IsCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_completed_attempt_is_rejected_after_Close_even_on_its_original_job_during_scope_disposal(bool swallowed)
    {
        using var world = World(out var settings);
        TrackEncoder(world, settings);
        settings.RegisterOnDispose = true;
        settings.SwallowDisposeGuard = swallowed;
        await world.Seed(69, 1000, 3);
        var error = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => Send(world, Mode.Register).AsTask());
        Assert.True(settings.DisposedWhileClosed);
        Assert.Equal(settings.JobId, settings.DisposeJobId);
        Assert.Equal("The command request is closed or belongs to another job.", settings.DisposeCallFailure!.Message);
        Assert.True(Contains(error, settings.DisposeCallFailure));
        Assert.Equal(1, settings.EncoderCalls);
        Assert.Equal(2, world.Count(1000));
        Assert.Equal(EffectOperationState.Committed, settings.State!.Entry.State);
        Assert.Same(settings.DisposeCallFailure, error.InnerException); // one fault propagated from the closed adapter through scope disposal
        Assert.True(world.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(world.Connection));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Encoder_failure_and_a_scope_guard_propagation_count_each_real_cause_once_and_keep_an_extra_scope_cause(bool extra)
    {
        using var world = World(out var settings);
        TrackEncoder(world, settings);
        settings.RegisterOnDispose = true;
        settings.ExtraDisposeFailure = extra ? new IOException("independent scope cause D") : null;
        world.Sender.Outbound.FailDirect(world.Connection.ConnectionId, () => { Interlocked.Increment(ref settings.EncoderCalls); return settings.Cause; });
        await world.Seed(69, 1000, 3);
        var error = await Record.ExceptionAsync(() => Send(world, Mode.Register).AsTask());
        Assert.Equal(2, world.Count(1000));
        Assert.Equal(1, settings.EncoderCalls);
        Assert.Equal(EffectOperationState.Committed, settings.State!.Entry.State);
        Assert.True(world.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(world.Connection));
        Assert.True(settings.DisposedWhileClosed);
        Assert.Equal(settings.JobId, settings.DisposeJobId);
        var typed = Assert.IsType<TransactionOutcomeUncertainException>(error);
        var leaves = ExceptionLeaves(typed.InnerException!).ToArray();
        Assert.Equal(extra ? 3 : 2, leaves.Length);
        Assert.Equal(1, leaves.Count(cause => ReferenceEquals(cause, settings.Cause)));
        Assert.Equal(1, leaves.Count(cause => ReferenceEquals(cause, settings.DisposeCallFailure)));
        if (extra)
        {
            Assert.Equal(1, leaves.Count(cause => ReferenceEquals(cause, settings.ExtraDisposeFailure)));
            Assert.Same(settings.DisposeAggregateFailure, Assert.IsType<AggregateException>(typed.InnerException).InnerExceptions[1]);
        }
    }

    private static IEnumerable<Exception> ExceptionLeaves(Exception error) => error is AggregateException aggregate
        ? aggregate.InnerExceptions.SelectMany(ExceptionLeaves) : [error];

    [Fact]
    public async Task A_real_pending_rollback_cleanup_cannot_issue_an_attempt_and_its_failure_keeps_all_causes_and_the_fence()
    {
        using var world = World(out var settings);
        TrackEncoder(world, settings);
        await world.Seed(69, 1000, 3);
        var error = await Record.ExceptionAsync(() => Send(world, Mode.PendingCleanup).AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.NotNull(error);
        Assert.NotNull(settings.InitialError);
        Assert.Equal("Registration requires every work unit to finish.", settings.InitialError.Message);
        Assert.Null(settings.Attempt);
        Assert.Equal(0, settings.EncoderCalls);
        Assert.Equal(2, world.Count(1000));
        Assert.Equal(100000, world.Gold); // the second transaction's actual SQLite write was rolled back
        Assert.Equal(EffectOperationState.Committed, settings.State!.Entry.State);
        Assert.True(TransactionOutcomeUncertainException.Contains(error));
        Assert.True(Contains(error, settings.InitialError));
        Assert.True(Contains(error, settings.Cause));
        Assert.True(settings.InFlight!.IsCompleted);
        Assert.True(world.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(world.Connection));
    }

    private static void TrackEncoder(ShopFixture world, Settings settings) =>
        world.Sender.Outbound.Encoder.Fail((_, _) => { Interlocked.Increment(ref settings.EncoderCalls); return null; });

    private static bool Contains(Exception error, Exception cause) => ReferenceEquals(error, cause)
        || error is AggregateException aggregate && aggregate.InnerExceptions.Any(item => Contains(item, cause))
        || error.InnerException is { } inner && Contains(inner, cause);
    private static ShopFixture World(out Settings settings)
    {
        var owned = settings = new Settings();
        var fixture = new ShopFixture(services => {
            services.AddSingleton(owned);
            services.AddScoped<DisposeProbe>();
            services.AddSingleton<IItemUseRuleDefinitionSource>(new FakeItemUseRules((1000, ItemUseRules.Potion(0))));
            services.AddProtocolCommandHandler<MatrixProbe, int, MatrixHandler>(
                new[] { CommandRegistrationExit.SettledRequiredExplicit, CommandRegistrationExit.CommittedReplayCurrentStateBatch },
                new ProtocolCommandMessageBinding(MatrixMessageType, typeof(MatrixRequest)));
            services.RegisterProtocolMessageHandler<MatrixThin, MatrixRequest, MatrixProbe>(MatrixMessageType);
        });
        Assert.NotNull(fixture.Services.GetRequiredService<MessageHandlerRegistry>());
        return fixture;
    }
    private static ValueTask<int> Send(ShopFixture world, Mode mode, UseStackableRequest? input = null) =>
        world.Services.GetRequiredService<ICommandBus>().SendAsync(new MatrixProbe(new(world.Connection), input ?? WholeUse.Input(new(InventoryContainerType.Main, 69), 1000), mode));
    public enum Mode { Commit, Refusal, ReadOnlyMain, MissingReporter, WrongOwnerEmptySlots, WrongOwnerRental, NoMain, Rollback, Register, BusinessFailure, CatchAndWrite, CatchAndReturn, UpgradeRejected, DuplicateMain, ReadAfterMain, OrdinaryRegister, MultiCommit, MultiFailure, EmptyExplicit, DoubleRegister, PreviousAttempt, HoldAttempt, PendingCleanup }
    private sealed class Settings
    {
        internal CommandExecutionContext? Context;
        internal ExplicitEffectRequest? State;
        internal readonly IOException Cause = new("same actual test cause");
        internal Exception? DisposeFailure;
        internal int LaterWrites;
        internal SelectedSessionRecipient? OriginalRecipient;
        internal SelectedSessionRecipient? OtherRecipient;
        internal IEffectFootprintRecorder? Footprint;
        internal ReplayProjectionRequest? OriginalProjection;
        internal Exception? InitialError;
        internal ICommandRegistrationAttempt? Attempt;
        internal readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int EncoderCalls;
        internal Guid JobId;
        internal Guid? DisposeJobId;
        internal bool RegisterOnDispose, DisposedWhileClosed, SwallowDisposeGuard;
        internal Exception? DisposeCallFailure, ExtraDisposeFailure;
        internal AggregateException? DisposeAggregateFailure;
        internal NetworkConnectionContext? Connection;
        internal Task<int>? InFlight;
    }
    private sealed class DisposeProbe(Settings settings, IGameCommandOutbox outbox) : IDisposable
    {
        public void Dispose()
        {
            if (settings.RegisterOnDispose)
            {
                settings.DisposedWhileClosed = settings.Context!.Request!.IsClosed;
                settings.DisposeJobId = GameplayJob.Current?.Id;
                try { outbox.RegisterExplicitAsync(settings.Connection!.ConnectionId,
                    new([UseStackableResponse.Success(new(InventoryContainerType.Main, 69), 0, 1000)], []), settings.Attempt!, default).AsTask().GetAwaiter().GetResult(); }
                catch (Exception cause)
                {
                    settings.DisposeCallFailure = cause;
                    if (settings.ExtraDisposeFailure is { } extra)
                    {
                        settings.DisposeAggregateFailure = new AggregateException("scope guard and independent cleanup", cause, extra);
                        throw settings.DisposeAggregateFailure;
                    }
                    if (settings.SwallowDisposeGuard) return;
                    throw;
                }
            }
            if (settings.DisposeFailure is { } error) throw error;
        }
    }
    private sealed class RollbackGate(Settings settings) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public async ValueTask RollbackAsync(CancellationToken ct)
        {
            settings.Ready.TrySetResult();
            await settings.Release.Task;
            throw settings.Cause;
        }
    }
    private const ClientMessageType MatrixMessageType = (ClientMessageType)0x1FFFD;
    private sealed record MatrixRequest(UseStackableRequest Input, Mode Mode) : IEffectSettlementRequest
    {
        public ClientMessageType MessageType => MatrixMessageType;
        public GameMessageMetadata Metadata => Input.Metadata;
        public ReplayProjectionNeeds MinimumReplayNeeds => Input.MinimumReplayNeeds;
    }
    private sealed class MatrixThin(ICommandBus bus) : ClientMessageHandler<MatrixRequest>, IProtocolCommandMessageHandler<MatrixProbe>
    {
        protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, MatrixRequest message, CancellationToken ct) =>
            _ = await bus.SendAsync(new MatrixProbe(context, message.Input, message.Mode), ct);
    }
    private sealed record MatrixProbe(NetworkDispatchContext Dispatch, UseStackableRequest Input, Mode Mode) : IProtocolCommand<int>
    {
        public IClientNetworkMessage Message => new MatrixRequest(Input, Mode);
        public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
    }
    private sealed class MatrixHandler(Settings settings, DisposeProbe disposal, ICommandWorkUnits units,
        IItemUseEffectDomainService effects, IBusinessInventorySlotDomainService inventory, IGameCommandOutbox outbox, ISelectedSessionDomainService sessions, IReplayStateDomainService reader, IDomainTransactionAccessor transactions, ICharacterCarriedGoldTable gold)
        : ICommandHandler<MatrixProbe, int>
    {
        public async ValueTask<int> HandleAsync(CommandExecutionContext context, MatrixProbe command, CancellationToken ct)
        {
            _ = disposal;
            settings.Context = context;
            settings.JobId = GameplayJob.Current!.Value.Id;
            settings.Connection = command.Dispatch.Connection;
            if (command.Mode == Mode.UpgradeRejected)
            {
                var original = settings.OriginalRecipient!;
                sessions.Select(original.Session); sessions.EnterWorld(original.Session);
                return await units.WriteAsync(context, new("rejected-main", [original], CommandWorkUnitKind.MainSettlement),
                    (_, _) => { settings.LaterWrites++; return ValueTask.FromResult(1); }, ct);
            }
            var selected = Assert.IsType<CommandRequestContext.ProtocolEffectSelected>(context.Request);
            settings.State = selected.State;
            var recipient = selected.Recipient;
            var participants = command.Mode == Mode.MissingReporter ? Array.Empty<SelectedSessionRecipient>() : new[] { recipient };
            if (command.Mode is Mode.MultiCommit or Mode.MultiFailure) participants = [recipient, settings.OtherRecipient!];
            var declaration = new CommandWorkUnitDeclaration("matrix-main", participants, CommandWorkUnitKind.MainSettlement);
            if (command.Mode == Mode.NoMain) return 1;
            if (command.Mode == Mode.PreviousAttempt)
            {
                await outbox.RegisterExplicitAsync(command.Dispatch.Connection.ConnectionId,
                    new([UseStackableResponse.Success(command.Input.Slot, 0, 1000)], []), settings.Attempt!, ct);
                return 1;
            }
            if (command.Mode == Mode.ReadOnlyMain) return await units.ReadOnlyAsync(context, declaration, _ => ValueTask.FromResult(1), ct);
            await units.WriteAsync(context, declaration, async (unit, token) => {
                settings.Footprint = unit.Footprint;
                if (command.Mode == Mode.WrongOwnerEmptySlots)
                    unit.Footprint.RecordInventorySlots(recipient.Session with { CharacterId = new(2) }, []);
                if (command.Mode == Mode.WrongOwnerRental)
                    unit.Footprint.RecordRentalState(recipient.Session with { CharacterId = new(2) });
                if (command.Mode != Mode.Refusal)
                {
                    var slot = new BusinessSlot(BusinessInventorySpace.Consumable, 4);
                    var location = inventory.Find(recipient.Session.CharacterId, slot)!;
                    var item = (await location.Container.GetAsync(location.Slot.Index, token))!;
                    await effects.UseAsync(recipient.Session, slot, item, unit.Footprint, token);
                }
                if (command.Mode == Mode.Rollback) unit.RequireRollback();
                return 1;
            }, ct);
            settings.OriginalProjection = selected.State.Entry.Projection;
            if (command.Mode == Mode.DuplicateMain)
            {
                try
                {
                    return await units.WriteAsync(context, declaration, (_, _) => { settings.LaterWrites++; return ValueTask.FromResult(2); }, ct);
                }
                catch (Exception error) { settings.InitialError = error; throw; }
            }
            if (command.Mode == Mode.ReadAfterMain)
                await units.WriteAsync(context, new("after-main-current-read", [recipient]), (_, token) =>
                    reader.ReadAsync(recipient.Session, new(ReplayProjectionNeeds.Inventory, []), command.Input.Metadata, token), ct);
            if (command.Mode == Mode.PendingCleanup)
            {
                settings.InFlight = units.WriteAsync(context, new("pending-real-sqlite-rollback", [recipient]), async (unit, token) => {
                    await gold.AddAsync(recipient.Session.CharacterId, 100, int.MaxValue, token);
                    transactions.Current!.Enlist("rollback-gate", () => new RollbackGate(settings));
                    unit.RequireRollback();
                    return 1;
                }, ct).AsTask();
                await settings.Ready.Task;
                try
                {
                    settings.Attempt = selected.BeginRegistrationAttempt(CommandRegistrationExit.SettledRequiredExplicit, command.Dispatch.Connection);
                    throw new InvalidOperationException("Pending cleanup incorrectly issued an attempt.");
                }
                catch (Exception error) { settings.InitialError = error; throw; }
                finally { settings.Release.TrySetResult(); }
            }
            if (command.Mode is Mode.BusinessFailure or Mode.MultiFailure) throw settings.Cause;
            if (command.Mode == Mode.OrdinaryRegister)
                await outbox.RegisterExplicitAsync(command.Dispatch.Connection.ConnectionId,
                    new([UseStackableResponse.Success(command.Input.Slot, 0, 1000)], []), ct);
            if (command.Mode is Mode.Register or Mode.CatchAndWrite or Mode.CatchAndReturn or Mode.EmptyExplicit or Mode.DoubleRegister or Mode.HoldAttempt)
            {
                var attempt = selected.BeginRegistrationAttempt(CommandRegistrationExit.SettledRequiredExplicit, command.Dispatch.Connection);
                settings.Attempt = attempt;
                if (command.Mode == Mode.HoldAttempt) { settings.Ready.TrySetResult(); await settings.Release.Task; }
                try { await outbox.RegisterExplicitAsync(command.Dispatch.Connection.ConnectionId,
                    new(command.Mode == Mode.EmptyExplicit ? [] : [UseStackableResponse.Success(command.Input.Slot, 0, 1000)], []), attempt, ct); }
                catch when (command.Mode is Mode.CatchAndWrite or Mode.CatchAndReturn) { }
                if (command.Mode == Mode.DoubleRegister)
                {
                    try { await outbox.RegisterExplicitAsync(command.Dispatch.Connection.ConnectionId,
                        new([UseStackableResponse.Success(command.Input.Slot, 0, 1000)], []), attempt, ct); }
                    catch (Exception error) { settings.InitialError = error; throw; }
                }
                if (command.Mode == Mode.CatchAndWrite)
                    try { await units.WriteAsync(context, new("after-failure", [recipient]), (_, _) => { settings.LaterWrites++; return ValueTask.FromResult(1); }, ct); } catch { }
            }
            return 1;
        }
    }

}
