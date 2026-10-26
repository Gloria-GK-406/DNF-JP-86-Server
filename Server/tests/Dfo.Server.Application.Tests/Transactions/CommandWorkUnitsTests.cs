using System.Diagnostics.CodeAnalysis;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Channel.ScriptVersion;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Application.Tests.Commands.ProtocolCommandExecutionTests;

namespace Dfo.Server.Application.Tests.Transactions;

public sealed class CommandWorkUnitsTests
{
    [Fact]
    public async Task A_nonreporting_participant_that_is_no_longer_current_stops_the_unit_before_its_action()
    {
        var players = new Players();
        await using var world = new CommandWorld(configure: players.Register);
        var handled = new List<int>();
        players.Current.Remove(players.All[1]);
        world.Settings.Action = ctx => world.Services.GetRequiredService<ICommandWorkUnits>().WriteAsync(
            ctx, new("both-current-required", players.Recipients), (_, _) =>
            { handled.Add(1); return ValueTask.FromResult(42); });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Internal(new InternalProbe()).AsTask());
        Assert.Contains("stale", error.Message, StringComparison.Ordinal);
        Assert.Empty(handled);
        Assert.Empty(players.Fenced);
        players.Current.Add(players.All[1]);
        Assert.Equal(42, await world.Internal(new InternalProbe()));
        Assert.Single(handled);
    }

    [Fact]
    public async Task A_normal_write_prepares_then_commits_and_leaves_no_ambient_transaction()
    {
        await using var world = new CommandWorld();
        var log = new List<string>();
        var durable = new RecordingParticipant("durable", log, true);
        var memory = new RecordingParticipant("memory", log, false);
        world.Settings.Action = ctx => world.Services.GetRequiredService<ICommandWorkUnits>().WriteAsync(ctx, new("ordinary", []),
            (_, _) =>
            {
                var transaction = world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!;
                transaction.Enlist("durable", () => durable); transaction.Enlist("memory", () => memory);
                return ValueTask.FromResult(42);
            });
        Assert.Equal(42, await world.Internal(new InternalProbe()));
        Assert.Equal(new[] { "prepare:durable", "prepare:memory", "commit:durable", "commit:memory" }, log);
        Assert.True(durable.Applied); Assert.True(memory.Applied);
        Assert.Null(world.Services.GetRequiredService<IDomainTransactionAccessor>().Current);
    }

    [Fact]
    public async Task A_normal_business_refusal_really_rolls_back_and_can_be_followed_by_another_unit()
    {
        await using var world = new CommandWorld();
        var participant = new RecordingParticipant("refused", [], true);
        var later = new RecordingParticipant("later", [], true);
        world.Settings.Action = async ctx =>
        {
            var units = world.Services.GetRequiredService<ICommandWorkUnits>();
            var refused = await units.WriteAsync(ctx, new("refusal", []), (unit, _) =>
            {
                world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!.Enlist("refusal", () => participant);
                unit.RequireRollback(); return ValueTask.FromResult(7);
            });
            Assert.Equal(7, refused);
            return await units.WriteAsync(ctx, new("later", []), (_, _) =>
            {
                world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!.Enlist("later", () => later);
                return ValueTask.FromResult(9);
            });
        };
        Assert.Equal(9, await world.Internal(new InternalProbe()));
        Assert.True(participant.RolledBack); Assert.False(participant.Applied); Assert.True(later.Applied);
    }

    [Fact]
    public async Task A_read_only_unit_refuses_a_writable_participant()
    {
        await using var world = new CommandWorld();
        var participant = new RecordingParticipant("write", [], true);
        world.Settings.Action = ctx => world.Services.GetRequiredService<ICommandWorkUnits>().ReadOnlyAsync<int>(ctx, new("read", []), _ =>
        {
            var current = world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!;
            Assert.True(current.IsReadOnly);
            current.Enlist("write", () => participant);
            return ValueTask.FromResult(1);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => world.Internal(new InternalProbe()).AsTask());
        Assert.False(participant.Applied);
        Assert.Null(world.Services.GetRequiredService<IDomainTransactionAccessor>().Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prepare_or_fallible_commit_failure_confirms_rollback_and_preserves_the_original_error(bool failCommit)
    {
        await using var world = new CommandWorld();
        var failure = new IOException("before durable completion");
        var participant = new RecordingParticipant("failing", [], true)
        { PrepareFailure = failCommit ? null : failure, CommitFailure = failCommit ? failure : null };
        world.Settings.Action = ctx => world.Services.GetRequiredService<ICommandWorkUnits>().WriteAsync(ctx, new("fail", []), (_, _) =>
        {
            world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!.Enlist("failing", () => participant);
            return ValueTask.FromResult(1);
        });
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => world.Internal(new InternalProbe()).AsTask()));
        Assert.True(participant.RolledBack); Assert.False(participant.Applied);
    }

    [Fact]
    public async Task A_swallowed_failed_unit_cannot_authorize_another_write()
    {
        await using var world = new CommandWorld();
        var failure = new IOException("body failed");
        var later = 0;
        world.Settings.Action = async ctx =>
        {
            var units = world.Services.GetRequiredService<ICommandWorkUnits>();
            try { await units.WriteAsync<int>(ctx, new("failed", []), (_, _) => throw failure); } catch (IOException) { }
            try { await units.WriteAsync(ctx, new("must-not-run", []), (_, _) => { later++; return ValueTask.FromResult(1); }); } catch (IOException) { }
            return 0;
        };
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => world.Internal(new InternalProbe()).AsTask()));
        Assert.Equal(0, later);
    }

    [Fact]
    public async Task A_second_unit_refusal_discards_only_its_messages_and_preserves_the_first_commit_and_exact_close()
    {
        await using var world = new CommandWorld();
        world.Settings.Action = async ctx =>
        {
            var units = world.Services.GetRequiredService<ICommandWorkUnits>();
            var messages = world.Services.GetRequiredService<ICommandMessageOutbox>();
            await units.WriteAsync(ctx, new("first", []), async (_, ct) =>
            { await messages.RegisterAsync(world.Source.ConnectionId, new ChannelScriptVersionResponse("first"), ct); return 1; });
            await units.WriteAsync(ctx, new("second", []), async (unit, ct) =>
            {
                await messages.RegisterAsync(world.Source.ConnectionId, new ChannelScriptVersionResponse("second"), ct);
                await world.Services.GetRequiredService<ICommandConnectionClosures>().RequestCloseAsync(world.Source, ct);
                unit.RequireRollback(); return 2;
            });
            return 3;
        };
        Assert.Equal(3, await world.Internal(new InternalProbe()));
        Assert.Collection(world.Outbound.Entries,
            entry => Assert.Equal(new ChannelScriptVersionResponse("first"), Assert.Single(entry.Messages)),
            entry => { Assert.True(entry.IsClose); Assert.Same(world.Source, entry.Connection); });
    }

    [Fact]
    public async Task An_unfinished_failed_unit_flushes_no_messages_and_does_not_close_a_rootless_target()
    {
        await using var world = new CommandWorld();
        var failure = new IOException("abort");
        world.Settings.Action = ctx => world.Services.GetRequiredService<ICommandWorkUnits>().WriteAsync<int>(ctx, new("unfinished", []), async (_, ct) =>
        {
            await world.Services.GetRequiredService<ICommandMessageOutbox>().RegisterAsync(world.Source.ConnectionId, new ChannelScriptVersionResponse("not committed"), ct);
            throw failure;
        });
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => world.Internal(new InternalProbe()).AsTask()));
        Assert.Empty(world.Outbound.Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Returning_before_a_unit_finishes_waits_for_its_action_and_preserves_rollback_uncertainty(bool cleanupFails)
    {
        var players = new Players();
        await using var world = new CommandWorld(configure: players.Register);
        var cleanup = new IOException("unfinished unit rollback failed");
        var participant = new RecordingParticipant("unfinished", [], true) { RollbackFailure = cleanupFails ? cleanup : null };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int>? pending = null;
        var resumedOnExecutor = false;
        world.Settings.Action = ctx =>
        {
            pending = world.Services.GetRequiredService<ICommandWorkUnits>().WriteAsync(ctx, new("unfinished", players.Recipients), async (unit, ct) =>
            {
                world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!.Enlist("unfinished", () => participant);
                await world.Services.GetRequiredService<ICommandMessageOutbox>().RegisterAsync(players.Recipients[0].Session.ConnectionId,
                    new ChannelScriptVersionResponse("pending"), ct);
                entered.TrySetResult();
                await release.Task;
                resumedOnExecutor = GameplayJob.IsOnExecutor;
                Assert.False(Assert.Single(world.Settings.Resources).Disposed);
                unit.RequireRollback();
                return 1;
            }).AsTask();
            return ValueTask.FromResult(1);
        };
        var command = world.Internal(new InternalProbe()).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Task.Delay(30);
            Assert.False(command.IsCompleted);
            Assert.False(Assert.Single(world.Settings.Resources).Disposed);
        }
        finally { release.TrySetResult(); }
        if (cleanupFails)
        {
            var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => command);
            Assert.Equal(TransactionUncertaintyReason.RollbackFailure, failure.Reason);
            var causes = Assert.IsType<AggregateException>(failure.InnerException).InnerExceptions;
            Assert.Contains(cleanup, causes);
            Assert.Contains(causes, error => error is InvalidOperationException && error.Message.Contains("unfinished", StringComparison.Ordinal));
            Assert.Equal(players.All, players.Fenced.OrderBy(x => x.AccountId.Value));
            Assert.All(world.Outbound.Entries, entry => Assert.True(entry.IsClose));
        }
        else
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => command);
            Assert.Contains("unfinished", failure.Message);
            Assert.True(participant.RolledBack);
            Assert.Empty(players.Fenced);
            Assert.Empty(world.Outbound.Entries);
        }
        Assert.True(resumedOnExecutor);
        Assert.True(Assert.Single(world.Settings.Resources).Disposed);
        Assert.False(participant.Applied);
        await Assert.ThrowsAnyAsync<Exception>(() => pending!);
    }

    [Fact]
    public async Task An_unawaited_commit_is_drained_before_job_end_and_is_never_reported_as_rollback()
    {
        var players = new Players();
        await using var world = new CommandWorld(configure: players.Register);
        var participant = new HeldCommit();
        Task<int>? unit = null;
        world.Settings.Action = ctx =>
        {
            unit = world.Services.GetRequiredService<ICommandWorkUnits>().WriteAsync(ctx, new("held-commit", players.Recipients), (_, _) =>
            {
                world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!.Enlist("held-commit", () => participant);
                return ValueTask.FromResult(1);
            }).AsTask();
            return ValueTask.FromResult(1);
        };
        var command = world.Internal(new InternalProbe()).AsTask();
        await participant.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Task.Delay(30);
            Assert.False(command.IsCompleted);
            Assert.False(participant.Applied);
        }
        finally { participant.Release.TrySetResult(); }
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => command);
        Assert.Equal(TransactionUncertaintyReason.PostCommitPublicationFailure, failure.Reason);
        try { await unit!; } catch (InvalidOperationException) { }
        Assert.True(participant.Applied);
        Assert.False(participant.RolledBack);
        Assert.Equal(players.All, players.Fenced.OrderBy(x => x.AccountId.Value));
    }

    private sealed class HeldCommit : ITransactionParticipant
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CommitCanFail => true;
        public bool Applied, RolledBack;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public async ValueTask CommitAsync(CancellationToken ct) { Entered.TrySetResult(); await Release.Task; Applied = true; }
        public ValueTask RollbackAsync(CancellationToken ct) { RolledBack = true; return ValueTask.CompletedTask; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uncertain_or_post_commit_failure_fences_every_exact_participant_and_never_allows_a_second_write(bool ordinaryAfterCommit)
    {
        var players = new Players();
        await using var world = new CommandWorld(configure: players.Register);
        var durable = new RecordingParticipant("durable", [], true);
        var memory = new RecordingParticipant("memory", [], false)
        { CommitFailure = ordinaryAfterCommit ? null : new IOException("contract broken") };
        var ordinary = new IOException("after commit");
        var later = 0;
        world.Settings.Action = async ctx =>
        {
            var units = world.Services.GetRequiredService<ICommandWorkUnits>();
            try
            {
                await units.WriteAsync(ctx, new("participants", players.Recipients), (_, _) =>
                {
                    var transaction = world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!;
                    transaction.Enlist("durable", () => durable); transaction.Enlist("memory", () => memory);
                    return ValueTask.FromResult(1);
                });
                if (ordinaryAfterCommit) throw ordinary;
            }
            catch (Exception error) when (!ordinaryAfterCommit)
            {
                Assert.True(TransactionOutcomeUncertainException.Contains(error));
                try { await units.WriteAsync(ctx, new("must-not-repeat", players.Recipients), (_, _) => { later++; return ValueTask.FromResult(0); }); }
                catch (Exception) { }
                return 0;
            }
            return 1;
        };
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => world.Internal(new InternalProbe()).AsTask());
        Assert.Equal(ordinaryAfterCommit ? TransactionUncertaintyReason.PostCommitPublicationFailure : TransactionUncertaintyReason.NonFallibleCommitContractViolation, failure.Reason);
        if (ordinaryAfterCommit) Assert.Same(ordinary, failure.InnerException);
        Assert.True(durable.Applied);
        Assert.Equal(0, later);
        Assert.Equal(players.All, players.Fenced.OrderBy(x => x.AccountId.Value));
        Assert.Equal(players.Recipients.Select(x => x.Connection), world.Outbound.Entries.Where(x => x.IsClose).Select(x => x.Connection));
    }

    [Fact]
    public async Task Rollback_cleanup_failure_retains_both_causes_and_fences_every_participant()
    {
        var players = new Players();
        await using var world = new CommandWorld(configure: players.Register);
        var cause = new IOException("business failed"); var cleanup = new IOException("rollback failed");
        var participant = new RecordingParticipant("failing", [], true) { RollbackFailure = cleanup };
        world.Settings.Action = ctx => world.Services.GetRequiredService<ICommandWorkUnits>().WriteAsync<int>(ctx, new("rollback", players.Recipients), (_, _) =>
        { world.Services.GetRequiredService<IDomainTransactionAccessor>().Current!.Enlist("failing", () => participant); throw cause; });
        var error = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => world.Internal(new InternalProbe()).AsTask());
        var aggregate = Assert.IsType<AggregateException>(error.InnerException);
        Assert.Contains(cause, aggregate.InnerExceptions); Assert.Contains(cleanup, aggregate.InnerExceptions);
        Assert.Equal(players.All, players.Fenced.OrderBy(x => x.AccountId.Value));
    }

    private sealed class Players : ISelectedSessionDomainService, IOnlineUserQueryDomainService, INetworkConnectionContexts
    {
        public SelectedSessionIdentity[] All { get; } = [
            SelectedSessionIdentity.Create(new(1), new(11), new("unit-player-1"), 1, 10011),
            SelectedSessionIdentity.Create(new(2), new(12), new("unit-player-2"), 1, 10011)];
        public HashSet<SelectedSessionIdentity> Fenced { get; } = [];
        public HashSet<SelectedSessionIdentity> Current { get; }
        public IReadOnlyList<SelectedSessionRecipient> Recipients { get; }
        public Players()
        {
            Current = [.. All];
            Recipients = [.. All.Select(s => new SelectedSessionRecipient(s, new(s.ConnectionId, NetworkEndpointKind.Game, 10011)))];
        }
        public void Register(IServiceCollection services)
        {
            services.AddSingleton<ISelectedSessionDomainService>(this);
            services.AddSingleton<IOnlineUserQueryDomainService>(this);
            services.AddSingleton<INetworkConnectionContexts>(this);
        }
        public SelectedSessionStanding? StandingOf(SelectedSessionIdentity session) => Current.Contains(session) ? new(SelectedSessionPhase.InWorld, Fenced.Contains(session), false) : null;
        public bool IsSelected(SelectedSessionIdentity session) => All.Contains(session);
        public bool IsInWorld(SelectedSessionIdentity session) => IsSelected(session);
        public bool Fence(SelectedSessionIdentity session) => All.Contains(session) && Fenced.Add(session);
        public SelectedSessionIdentity? Find(AccountId id) => All.FirstOrDefault(x => x.AccountId == id);
        public SelectedSessionIdentity? FindByCharacter(CharacterId id) => All.FirstOrDefault(x => x.CharacterId == id);
        public SelectedSessionIdentity? FindByWireUserId(ushort id) => All.FirstOrDefault(x => x.WireUserId == id);
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId id) => All.FirstOrDefault(x => x.ConnectionId == id);
        public bool TryGetInWorld(CharacterId id, [NotNullWhen(true)] out SelectedSessionIdentity? s) => (s = FindByCharacter(id)) is not null;
        public bool TryGetInWorld(ushort id, [NotNullWhen(true)] out SelectedSessionIdentity? s) => (s = FindByWireUserId(id)) is not null;
        public bool TryGetInWorld(NetworkConnectionId id, [NotNullWhen(true)] out SelectedSessionIdentity? s) => (s = FindByConnection(id)) is not null;
        public SelectedSessionIdentity? Select(SelectedSessionIdentity s) => throw new NotSupportedException();
        public void EnterWorld(SelectedSessionIdentity s) => throw new NotSupportedException();
        public bool ReleaseExact(SelectedSessionIdentity s) => throw new NotSupportedException();
        public OnlineUserSnapshot GetRequired(OnlineUserId id) => throw new NotSupportedException();
        public bool TryGetByConnection(NetworkConnectionId id, out OnlineUserSnapshot? user)
        {
            var recipient = Recipients.FirstOrDefault(x => x.Session.ConnectionId == id);
            user = recipient is null ? null : new(new(Guid.Empty), recipient.Connection!, recipient.Session.AccountId,
                recipient.Session.CharacterId, OnlineUserState.CharacterActive, recipient.Session.ConnectionGeneration);
            return user is not null;
        }
        public bool TryGet(NetworkConnectionId id, out NetworkConnectionContext? connection)
        { connection = Recipients.FirstOrDefault(x => x.Session.ConnectionId == id)?.Connection; return connection is not null; }
    }
}
