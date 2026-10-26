using System.Collections.Immutable;
using System.Reflection;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.Commands;

public sealed class CommandSelectedSessionCleanupTests
{
    private static readonly SelectedSessionIdentity Actor = Session(1);
    private static readonly SelectedSessionIdentity Peer = Session(2);
    private static SelectedSessionIdentity Session(int id) => SelectedSessionIdentity.Create(new(id), new(id), new($"cleanup:{id}"), 1, 10011);
    private static CommandExecutionContext Context(Func<bool>? authority = null)
    {
        var dispatch = new NetworkDispatchContext(new(Actor.ConnectionId, NetworkEndpointKind.Game, 10011));
        var request = new CommandRequestContext.ProtocolNonEffect(dispatch,
            new Dfo.Server.Protocol.Game.SelectCharacter.SelectCharacterRequest(GameMessageMetadata.Empty, 0),
            new ProtocolFrameMetadata.Game(GameMessageMetadata.Empty), authority ?? (() => true))
            { SelectedSessionCleanup = CommandSelectedSessionCleanupCapability.SelectedSessionLifecycle };
        return new(Guid.NewGuid(), typeof(CommandSelectedSessionCleanupTests), typeof(CommandSelectedSessionCleanupTests), CommandExecutionKind.Direct) { Request = request };
    }
    private static ICommandWorkUnits Units() => new CommandWorkUnits(new DomainTransactionScope(),
        new SelectedSessionSafety(new ServiceCollection().BuildServiceProvider(), NullLogger<SelectedSessionSafety>.Instance));

    [Fact]
    public async Task Sticky_failure_still_allows_only_fixed_cleanup_and_its_independent_commit()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var ctx = Context(); var units = Units(); var cause = new IOException("ordinary action");
            await Assert.ThrowsAsync<IOException>(() => units.WriteAsync<int>(ctx, new("failed", []), (_, _) => throw cause).AsTask());
            var durable = new Marker();
            var cap = new CommandSelectedSessionCleanup(new Cleanup(async (_, footprint) =>
            {
                footprint.DemandActive(Actor);
                var tx = new DomainTransaction(); tx.Enlist("position", () => durable);
                await tx.CommitAsync(default);
                return SelectedSessionCleanupNotificationPlan.Empty(Actor);
            }));
            await cap.CleanupAsync(ctx, Actor, true);
            Assert.True(durable.Committed);
            Assert.Same(cause, ctx.Request!.Failure);
            var called = false;
            await Assert.ThrowsAsync<IOException>(() => units.WriteAsync(ctx,new("forbidden",[]),(_,_) => { called=true; return ValueTask.FromResult(1); }).AsTask());
            Assert.False(called);
            Assert.Throws<IOException>(() => ctx.Request.DemandFinished());
        });
    }

    [Fact]
    public async Task Exact_owners_are_frozen_from_both_tower_runs_before_cleanup_awaits()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestGameplay.RunAsync(async () =>
        {
            var third = Session(3); var foreign = Session(4);
            var boss = DispatchProxy.Create<IBossTowerDomainService, TowerProxy>();
            ((TowerProxy)(object)boss).Snapshot = new BossTowerSnapshot(Guid.NewGuid(), 11012,
                [new(Actor,0,true,false),new(Peer,1,false,false)], null!,[],null,DateTimeOffset.UtcNow);
            var illusion = DispatchProxy.Create<IIllusionTowerDomainService, TowerProxy>();
            ((TowerProxy)(object)illusion).Snapshot = new IllusionTowerSnapshot(Guid.NewGuid(),11001,
                [new(Actor,0,true,false),new(third,1,false,false)],null!,[],null,DateTimeOffset.UtcNow);
            var ctx = Context();
            var cap = new CommandSelectedSessionCleanup(new Cleanup(async (_, footprint) =>
            {
                await Task.Yield();
                ((TowerProxy)(object)boss).Snapshot = ((BossTowerSnapshot)((TowerProxy)(object)boss).Snapshot!) with { Members = [new(foreign,0,true,false)] };
                ((TowerProxy)(object)illusion).Snapshot = ((IllusionTowerSnapshot)((TowerProxy)(object)illusion).Snapshot!) with { Members = [new(foreign,0,true,false)] };
                footprint.DemandEffects(new(Peer.AccountId,Peer.CharacterId,Peer));
                footprint.DemandEffects(new(third.AccountId,third.CharacterId,third));
                Assert.Throws<InvalidOperationException>(() => footprint.DemandActive(foreign));
                return SelectedSessionCleanupNotificationPlan.Empty(Actor);
            }),illusion,boss);
            await cap.CleanupAsync(ctx,Actor,true);
            Assert.NotNull(ctx.Request!.Failure);
        }));
    }

    [Fact]
    public async Task A_healthy_action_can_await_cleanup_but_commit_callback_cannot_reuse_its_unit()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestGameplay.RunAsync(async () =>
        {
            var ctx=Context(); var units=Units(); CommandWorkUnit? held=null; var calls=0;
            var cap=new CommandSelectedSessionCleanup(new Cleanup((_,f) => { calls++; f.DemandActive(Actor); return ValueTask.FromResult(SelectedSessionCleanupNotificationPlan.Empty(Actor)); }));
            await units.WriteAsync(ctx,new("prepare",[]),async (unit,_) => { held=unit; await cap.CleanupAsync(ctx,Actor,true,unit); return 1; });
            Assert.Equal(1,calls); Assert.Null(ctx.Request!.Failure);
            await Assert.ThrowsAsync<InvalidOperationException>(() => cap.CleanupAsync(ctx,Actor,true,held).AsTask());
            Assert.Equal(1,calls);
        }));
    }

    [Fact]
    public async Task Commit_phase_has_no_action_authority_even_while_the_unit_is_open()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestGameplay.RunAsync(async () =>
        {
            var ctx=Context(); var scope=new DomainTransactionScope(); var safety=new SelectedSessionSafety(new ServiceCollection().BuildServiceProvider(),NullLogger<SelectedSessionSafety>.Instance);
            var units=new CommandWorkUnits(scope,safety); var calls=0;
            var cap=new CommandSelectedSessionCleanup(new Cleanup((_,_) => { calls++; return ValueTask.FromResult(SelectedSessionCleanupNotificationPlan.Empty(Actor)); }));
            await Assert.ThrowsAnyAsync<Exception>(() => units.WriteAsync(ctx,new("prepare",[]),(unit,_) =>
            {
                scope.Current!.Enlist("commit-hook",() => new Marker { Commit = async () => { await cap.CleanupAsync(ctx,Actor,true,unit); } });
                return ValueTask.FromResult(1);
            }).AsTask());
            Assert.Equal(0,calls); Assert.NotNull(ctx.Request!.Failure);
        }));
    }

    [Fact]
    public async Task Unawaited_cleanup_is_drained_after_action_exit_and_cannot_continue_effects_or_commit()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestGameplay.RunAsync(async () =>
        {
            var ctx=Context(); var scope=new DomainTransactionScope();
            var units=new CommandWorkUnits(scope,new SelectedSessionSafety(new ServiceCollection().BuildServiceProvider(),NullLogger<SelectedSessionSafety>.Instance));
            var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var began=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ended=false; var marker=new Marker();
            var cap=new CommandSelectedSessionCleanup(new Cleanup(async (_,footprint) =>
            {
                began.SetResult();
                await release.Task;
                try { footprint.DemandActive(Actor); }
                finally { ended=true; }
                return SelectedSessionCleanupNotificationPlan.Empty(Actor);
            }));
            var operation=units.WriteAsync(ctx,new("prepare",[]),(unit,_) =>
            {
                scope.Current!.Enlist("marker",()=>marker);
                var pendingCleanup = cap.CleanupAsync(ctx,Actor,true,unit);
                return ValueTask.FromResult(1);
            }).AsTask();
            await began.Task; Assert.False(operation.IsCompleted); Assert.False(marker.Committed);
            release.SetResult(); await Assert.ThrowsAnyAsync<Exception>(()=>operation);
            Assert.True(ended); Assert.True(marker.RolledBack); Assert.False(marker.Committed); Assert.NotNull(ctx.Request!.Failure);
        }));
    }

    [Fact]
    public async Task Authority_loss_is_recorded_before_cleanup_and_cannot_return_success()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var ctx=Context(()=>false); var called=false;
            var cap=new CommandSelectedSessionCleanup(new Cleanup((_,f) => { called=true; f.DemandActive(Actor); return ValueTask.FromResult(SelectedSessionCleanupNotificationPlan.Empty(Actor)); }));
            await cap.CleanupAsync(ctx,Actor,true);
            Assert.True(called); Assert.NotNull(ctx.Request!.Failure); Assert.Throws<InvalidOperationException>(()=>ctx.Request.DemandFinished());
        });
    }

    [Fact]
    public async Task Closed_wrong_job_and_unpermitted_requests_cannot_invoke_cleanup()
    {
        CommandExecutionContext? held=null;
        await TestGameplay.RunAsync(()=> { held=Context(); return Task.CompletedTask; });
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestGameplay.RunAsync(async () =>
        {
            var calls=0; var cap=new CommandSelectedSessionCleanup(new Cleanup((_,_)=> { calls++; return ValueTask.FromResult(SelectedSessionCleanupNotificationPlan.Empty(Actor)); }));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>cap.CleanupAsync(held!,Actor,true).AsTask());
            var closed=Context(); closed.Request!.Close();
            await Assert.ThrowsAsync<InvalidOperationException>(()=>cap.CleanupAsync(closed,Actor,true).AsTask());
            var denied=Context(); denied.Request!.SelectedSessionCleanup=CommandSelectedSessionCleanupCapability.None;
            await Assert.ThrowsAsync<InvalidOperationException>(()=>cap.CleanupAsync(denied,Actor,true).AsTask());
            Assert.Equal(0,calls);
        }));
    }

    [Fact]
    public async Task Cleanup_recorder_is_not_reusable_after_invocation_and_overlapping_cleanup_is_rejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestGameplay.RunAsync(async () =>
        {
            var ctx=Context(); IEffectFootprintRecorder? held=null;
            var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cap=new CommandSelectedSessionCleanup(new Cleanup(async (_,f)=> { held=f; await release.Task; return SelectedSessionCleanupNotificationPlan.Empty(Actor); }));
            var first=cap.CleanupAsync(ctx,Actor,true).AsTask();
            await Assert.ThrowsAsync<InvalidOperationException>(()=>cap.CleanupAsync(ctx,Actor,true).AsTask());
            release.SetResult(); await first;
            Assert.Throws<InvalidOperationException>(()=>held!.DemandActive(Actor));
        }));
    }

    private sealed class Cleanup(Func<SelectedSessionIdentity,IEffectFootprintRecorder,ValueTask<SelectedSessionCleanupNotificationPlan>> action) : ISelectedSessionCleanupDomainService
    {
        public ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(SelectedSessionIdentity session,bool includeOwner,IEffectFootprintRecorder footprint,CancellationToken ct) => action(session,footprint);
    }
    public class TowerProxy : DispatchProxy
    {
        public object? Snapshot { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == "Find" ? Snapshot : throw new NotSupportedException();
    }
    private sealed class Marker : ITransactionParticipant
    {
        public bool CommitCanFail=>false; public bool Committed; public bool RolledBack;
        public Func<Task>? Commit { get; init; }
        public ValueTask PrepareAsync(CancellationToken ct)=>ValueTask.CompletedTask;
        public async ValueTask CommitAsync(CancellationToken ct) { if(Commit is not null) await Commit(); Committed=true; }
        public ValueTask RollbackAsync(CancellationToken ct) { RolledBack=true; return ValueTask.CompletedTask; }
    }
}
