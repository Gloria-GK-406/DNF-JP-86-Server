using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Application.Transactions;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Application.Tests.Commands.ProtocolCommandExecutionTests;

namespace Dfo.Server.Application.Tests.Transactions;

[Trait("Category", "Unit")]
public sealed class CommandSelfSettledWorkUnitsTests
{
    // The oracle observes recovery, admission and original causes, rather than a chosen constructor shape.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Abort_pins_admission_before_callbacks_and_keeps_it_after_inner_completion(bool begin)
    {
        await using var world = World();
        var cause = new IOException("action");
        var later = 0;
        Exception? guard = null;
        world.Settings.Action = async context =>
        {
            await world.Services.GetRequiredService<ICommandWorkUnits>().WriteSelfSettledResultAsync<int>(context, new("observed", []),
                (unit, ct) => unit.InvokeSelfSettlingAsync<int>(async _ =>
                {
                    var scope = world.Services.GetRequiredService<DomainTransactionScope>();
                    var tx = (DomainTransaction)scope.Current!;
                    await tx.AbortActionAsync(cause);
                    Assert.Null(scope.Current);
                    Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, tx.Completion);
                    guard = Assert.Throws<InvalidOperationException>(() =>
                    {
                        if (begin) { using var abandoned = new LeaseAdapter(scope.Begin()); later++; }
                        else world.Services.GetRequiredService<GameplayOutboxes>().Current.RequestClose(world.Source);
                    });
                    throw cause;
                }, ct));
            return 1;
        };
        var error = await Assert.ThrowsAnyAsync<Exception>(() => Send(world));
        Assert.True(Has(error, cause)); Assert.True(Has(error, guard!)); Assert.Equal(0, later);
        Assert.True(Assert.Single(world.Outbound.Entries).IsClose);
    }

    [Fact]
    public async Task A_healthy_action_rollback_recovers_only_after_cleanup_and_allows_the_next_unit()
    {
        await using var world = World();
        var cause = new IOException("action");
        world.Settings.Action = async context =>
        {
            var units = world.Services.GetRequiredService<ICommandWorkUnits>();
            var result = await units.WriteSelfSettledResultAsync<int>(context, new("observed", []),
                (unit, ct) => unit.InvokeSelfSettlingAsync<int>(async _ =>
                { await ((DomainTransaction)world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!).AbortActionAsync(cause); throw cause; }, ct));
            Assert.Same(cause, Assert.IsType<CommandSelfSettledWriteResult<int>.ActionRolledBack>(result).Cause);
            Assert.Null(world.Services.GetRequiredService<IDomainTransactionAccessor>().Current);
            return await units.WriteAsync(context, new("next", []), (_, _) => ValueTask.FromResult(9));
        };
        Assert.Equal(9, await Send(world)); Assert.Empty(world.Outbound.Entries);
    }

    [Fact]
    public async Task A_plain_throw_followed_by_runner_abort_has_no_action_recovery_source()
    {
        await using var world = World(); var cause = new IOException("plain");
        world.Settings.Action = async context =>
        {
            await world.Services.GetRequiredService<ICommandWorkUnits>().WriteSelfSettledResultAsync<int>(context, new("plain", []),
                (unit, ct) => unit.InvokeSelfSettlingAsync<int>(_ => throw cause, ct));
            return 1;
        };
        Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => Send(world)));
    }

    [Theory]
    [InlineData("rollback")]
    [InlineData("request")]
    [InlineData("invoke")]
    [InlineData("register")]
    [InlineData("begin")]
    [InlineData("enlist")]
    [InlineData("registration")]
    public async Task Independent_guard_after_real_fact_cannot_be_washed_by_rethrowing_the_original_handoff(string boundary)
    {
        await using var world = World(); var cause = new IOException("original"); Exception? guard = null; DomainTransaction? observed = null;
        world.Settings.Action = async context =>
        {
            await world.Services.GetRequiredService<ICommandWorkUnits>().WriteSelfSettledResultAsync<int>(context, new("guard", []), async (unit, ct) =>
            {
                try
                {
                    return await unit.InvokeSelfSettlingAsync<int>(async _ =>
                    { observed = (DomainTransaction)world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!; await observed.AbortActionAsync(cause); throw cause; }, ct);
                }
                catch (CommandSelfSettlementPropagationException)
                {
                    guard = Assert.Throws<InvalidOperationException>(() =>
                    {
                        switch (boundary)
                        {
                            case "rollback": unit.RequireRollback(); break;
                            case "request": context.Request!.DemandCurrent(); break;
                            case "invoke": _ = unit.InvokeSelfSettlingAsync(_ => ValueTask.FromResult(1)); break;
                            case "register": world.Services.GetRequiredService<GameplayOutboxes>().Current.RequestClose(world.Source); break;
                            case "begin": _ = world.Services.GetRequiredService<DomainTransactionScope>().Begin(); break;
                            case "enlist": observed!.Enlist("late", () => new RollbackFailure(cause)); break;
                            default: context.Request!.BeginRegistrationAttempt(CommandRegistrationExit.CommittedReplayCurrentStateBatch, world.Source); break;
                        }
                    });
                    throw;
                }
            });
            return 1;
        };
        var error = await Assert.ThrowsAnyAsync<Exception>(() => Send(world));
        Assert.True(Has(error, cause)); Assert.True(Has(error, guard!));
    }

    [Fact]
    public async Task A_rollback_second_cause_is_typed_uncertain_and_preserves_both_original_objects()
    {
        await using var world = World(); var cause = new IOException("action"); var second = new IOException("rollback");
        world.Settings.Action = async context =>
        {
            await world.Services.GetRequiredService<ICommandWorkUnits>().WriteSelfSettledResultAsync<int>(context, new("two-causes", []),
                (unit, ct) => unit.InvokeSelfSettlingAsync<int>(async _ =>
                {
                    var tx = (DomainTransaction)world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!;
                    tx.Enlist("rollback", () => new RollbackFailure(second));
                    await tx.AbortActionAsync(cause); throw cause;
                }, ct)); return 1;
        };
        var error = await Assert.ThrowsAnyAsync<Exception>(() => Send(world));
        Assert.True(TransactionOutcomeUncertainException.Contains(error)); Assert.True(Has(error, cause)); Assert.True(Has(error, second));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Missing_capability_internal_and_main_cannot_get_self_recovery(int defect)
    {
        await using var world = new CommandWorld(configure: services =>
        {
            if (defect != 0) Permit(services);
        });
        var invoked = false;
        world.Settings.Action = async context =>
        {
            await world.Services.GetRequiredService<ICommandWorkUnits>().WriteSelfSettledResultAsync(context,
                new("forbidden", [], defect == 2 ? CommandWorkUnitKind.MainSettlement : CommandWorkUnitKind.Ordinary),
                (_, _) => { invoked = true; return ValueTask.FromResult(1); }); return 1;
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => defect == 1 ? world.Internal(new InternalProbe()).AsTask() : Send(world));
        Assert.False(invoked);
    }

    [Fact]
    public async Task Candidate_current_validation_throwing_the_action_cause_again_is_an_independent_occurrence()
    {
        await using var world = World(); var cause = new IOException("shared object, two real phases");
        world.Settings.Action = async context =>
        {
            await world.Services.GetRequiredService<ICommandWorkUnits>().WriteSelfSettledResultAsync<int>(context, new("current", []), async (unit, ct) =>
            {
                try
                {
                    return await unit.InvokeSelfSettlingAsync<int>(async _ =>
                    { await ((DomainTransaction)world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!).AbortActionAsync(cause); throw cause; }, ct);
                }
                catch (CommandSelfSettlementPropagationException)
                {
                    // A narrow authority seam; the real candidate-validation guard performs the throw.
                    typeof(CommandRequestContext.ProtocolNonEffect).GetField("authority", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                        .SetValue(context.Request, (Func<bool>)(() => throw cause));
                    throw;
                }
            }); return 1;
        };
        var error = await Assert.ThrowsAnyAsync<Exception>(() => Send(world));
        Assert.Equal(2, Leaves(error).Count(item => ReferenceEquals(item, cause)));
    }
    private static IEnumerable<Exception> Leaves(Exception error) => error is AggregateException aggregate
        ? aggregate.InnerExceptions.SelectMany(Leaves) : [error];

    [Fact]
    public async Task Known_storage_prepare_failure_cannot_impersonate_native_fallible_commit()
    {
        await using var world = World(); var cause = new IOException("known source, wrong phase");
        world.Settings.Action = async context =>
        {
            await world.Services.GetRequiredService<ICommandWorkUnits>().WriteSelfSettledResultAsync(context, new("prepare", []), (_, _) =>
            {
                world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!.Enlist("prepare", () => new KnownPrepareFailure(cause));
                return ValueTask.FromResult(1);
            }); return 1;
        };
        Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => Send(world)));
    }
    private sealed class KnownPrepareFailure(Exception error) : ITransactionParticipant, ITransactionFailureSource
    {
        public TransactionFailureFact? LastFailure { get; private set; }
        public bool CommitCanFail => true;
        public ValueTask PrepareAsync(CancellationToken ct)
        { LastFailure = new(this, error, TransactionFailurePhase.Prepare, TransactionFailureKind.KnownStorage); return ValueTask.FromException(error); }
        public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
    [Fact]
    public async Task A_previous_completed_fact_does_not_authorize_the_next_calls_plain_throw()
    {
        await using var world = World(); var cause = new IOException("previous actual action");
        world.Settings.Action = async context =>
        {
            var units = world.Services.GetRequiredService<ICommandWorkUnits>();
            var first = await units.WriteSelfSettledResultAsync<int>(context, new("first", []), (unit, ct) => unit.InvokeSelfSettlingAsync<int>(async _ =>
            { await ((DomainTransaction)world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!).AbortActionAsync(cause); throw cause; }, ct));
            Assert.IsType<CommandSelfSettledWriteResult<int>.ActionRolledBack>(first);
            await units.WriteSelfSettledResultAsync<int>(context, new("second", []), (unit, ct) => unit.InvokeSelfSettlingAsync<int>(_ => throw cause, ct));
            return 1;
        };
        Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => Send(world)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_swallowed_handoff_or_a_replaced_throw_retains_the_original_action_occurrence(bool replace)
    {
        await using var world = World(); var cause = new IOException("original action before swallowed handoff"); var replacement = new IOException("independent replacement");
        world.Settings.Action = async context =>
        {
            await world.Services.GetRequiredService<ICommandWorkUnits>().WriteSelfSettledResultAsync<int>(context, new("swallowed", []), async (unit, ct) =>
            {
                try { return await unit.InvokeSelfSettlingAsync<int>(async _ =>
                    { await ((DomainTransaction)world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!).AbortActionAsync(cause); throw cause; }, ct); }
                catch (CommandSelfSettlementPropagationException) { if (replace) throw replacement; return 1; }
            }); return 1;
        };
        var error = await Assert.ThrowsAnyAsync<Exception>(() => Send(world));
        Assert.True(Has(error, cause)); if (replace) Assert.True(Has(error, replacement));
    }

    [Fact]
    public async Task A_closed_request_cannot_be_reused_and_caught_to_make_another_request_healthy()
    {
        await using var world = World(); CommandExecutionContext? previous = null;
        world.Settings.Action = context => { previous = context; return ValueTask.FromResult(1); };
        Assert.Equal(1, await Send(world));
        var invoked = false;
        world.Settings.Action = async context =>
        {
            try { await world.Services.GetRequiredService<ICommandWorkUnits>().WriteSelfSettledResultAsync(previous!, new("closed", []),
                (_, _) => { invoked = true; return ValueTask.FromResult(2); }); } catch (InvalidOperationException) { }
            return 2;
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Send(world)); Assert.False(invoked);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("typed")]
    [InlineData("cancelled-token")]
    public async Task Cancellation_and_typed_uncertainty_never_become_healthy_action_results(string mode)
    {
        await using var world = World(); using var cancellation = new CancellationTokenSource();
        Exception cause = mode == "cancel" ? new OperationCanceledException() : mode == "typed"
            ? new TransactionOutcomeUncertainException(TransactionUncertaintyReason.RollbackFailure, "controlled typed source", new IOException("inner")) : new IOException("cancelled token source");
        world.Settings.Action = async context =>
        {
            if (mode == "cancelled-token") cancellation.Cancel();
            await world.Services.GetRequiredService<ICommandWorkUnits>().WriteSelfSettledResultAsync<int>(context, new("cancel", []),
                (unit, ct) => unit.InvokeSelfSettlingAsync<int>(async _ =>
                { await ((DomainTransaction)world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!).AbortActionAsync(cause); throw cause; }, ct), cancellation.Token);
            return 1;
        };
        var error = await Assert.ThrowsAnyAsync<Exception>(() => Send(world)); Assert.True(Has(error, cause));
        Assert.True(Assert.Single(world.Outbound.Entries).IsClose);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_self_declaration_cannot_add_participants_or_a_nonrequest_footprint(bool footprint)
    {
        await using var world = World(); var invoked = false;
        world.Settings.Action = async context =>
        {
            var participant = new SelectedSessionRecipient(Dfo.Server.Data.World.SelectedSessionIdentity.Create(new(1), new(1), world.Source.ConnectionId, 1, 7001), world.Source);
            await world.Services.GetRequiredService<ICommandWorkUnits>().WriteSelfSettledResultAsync(context,
                new("forbidden-shape", footprint ? [] : [participant], Footprint: footprint ? CommandFootprintMode.NonRequest : CommandFootprintMode.None),
                (_, _) => { invoked = true; return ValueTask.FromResult(1); }); return 1;
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Send(world)); Assert.False(invoked);
    }

    [Fact]
    public async Task A_second_observation_cannot_be_caught_and_then_committed()
    {
        await using var world = World();
        world.Settings.Action = async context =>
        {
            await world.Services.GetRequiredService<ICommandWorkUnits>().WriteSelfSettledResultAsync(context, new("one-call", []), async (unit, ct) =>
            {
                await unit.InvokeSelfSettlingAsync(_ => ValueTask.FromResult(1), ct);
                try { await unit.InvokeSelfSettlingAsync(_ => ValueTask.FromResult(2), ct); } catch (InvalidOperationException) { }
                return 3;
            }); return 1;
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Send(world));
    }

    [Fact]
    public async Task A_different_outer_cause_cannot_hide_the_real_owned_action_abort()
    {
        await using var world = World(); var original = new IOException("actual action abort"); var replacement = new IOException("outer replacement cause");
        world.Settings.Action = async context =>
        {
            await world.Services.GetRequiredService<ICommandWorkUnits>().WriteSelfSettledResultAsync<int>(context, new("wrong-output", []),
                (unit, ct) => unit.InvokeSelfSettlingAsync<int>(async _ =>
                { await ((DomainTransaction)world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!).AbortActionAsync(original); throw replacement; }, ct));
            return 1;
        };
        var error = await Assert.ThrowsAnyAsync<Exception>(() => Send(world));
        Assert.True(Has(error, original)); Assert.True(Has(error, replacement));
        Assert.True(Assert.Single(world.Outbound.Entries).IsClose);
    }

    [Fact]
    public async Task A_lower_action_return_after_its_real_abort_is_not_a_healthy_result_and_keeps_the_action_cause()
    {
        await using var world = World(); var original = new IOException("aborted but lower caller returns");
        world.Settings.Action = async context =>
        {
            await world.Services.GetRequiredService<ICommandWorkUnits>().WriteSelfSettledResultAsync<int>(context, new("lower-return", []),
                (unit, ct) => unit.InvokeSelfSettlingAsync(async _ =>
                { await ((DomainTransaction)world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!).AbortActionAsync(original); return 42; }, ct));
            return 1;
        };
        var error = await Assert.ThrowsAnyAsync<Exception>(() => Send(world)); Assert.True(Has(error, original));
        Assert.True(Assert.Single(world.Outbound.Entries).IsClose);
    }

    private static CommandWorld World() => new(configure: Permit);
    private static void Permit(IServiceCollection services)
    {
        var descriptor = services.Single(d => d.ServiceType == typeof(CommandHandlerRegistration)
            && d.ImplementationInstance is CommandHandlerRegistration r && r.CommandType == typeof(Probe));
        services.Remove(descriptor);
        services.AddSingleton(((CommandHandlerRegistration)descriptor.ImplementationInstance!) with { SelfSettlement = CommandSelfSettlementCapability.SelfSettledAction });
    }
    private static Task<int> Send(CommandWorld world) => world.Bus.SendAsync(new Probe(new(world.Source), new ProbeMessage())).AsTask();
    internal static bool Has(Exception outer, Exception inner) => ReferenceEquals(outer, inner)
        || outer is AggregateException aggregate && aggregate.InnerExceptions.Any(child => Has(child, inner))
        || outer.InnerException is { } cause && Has(cause, inner);
    private sealed class RollbackFailure(Exception error) : ITransactionParticipant
    {
        public bool CommitCanFail => true;
        public bool SupportsReadOnly => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.FromException(error);
    }
    private sealed class LeaseAdapter(DomainTransactionLease lease) : IDisposable
    { public void Dispose() => lease.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
}
