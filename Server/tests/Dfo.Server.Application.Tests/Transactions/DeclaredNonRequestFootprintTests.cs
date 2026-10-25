using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.InitialConnection;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Transactions;

public sealed partial class ProtocolNonEffectFootprintTests
{
    private static CommandWorkUnitDeclaration Declared(IReadOnlyList<SelectedSessionIdentity>? owners,
        IReadOnlyList<SelectedSessionRecipient>? participants = null) =>
        new("declared-nonrequest", participants ?? [], Footprint: CommandFootprintMode.DeclaredNonRequest, FootprintOwners: owners);

    [Fact]
    public async Task Declared_owners_are_frozen_without_becoming_selected_safety_participants()
    {
        using var world = World(out var state);
        var peer = state.Actor! with { AccountId = new(70), CharacterId = new(71), WireUserId = 71, ConnectionId = new("not-current") };
        var inserted = peer with { ConnectionGeneration = peer.ConnectionGeneration + 1 };
        var owners = new List<SelectedSessionIdentity> { state.Actor!, peer };
        var reachedFrozenOwner = false;
        state.DeclaredAction = (ctx, units, _) => units.WriteAsync(ctx, Declared(owners), (unit, _) =>
        {
            var cap = unit.Footprint;
            owners.Clear(); owners.Add(inserted);
            cap.DemandEffects(new(peer.AccountId, peer.CharacterId, peer));
            cap.RecordInventorySlots(peer, []); cap.RecordRentalState(peer);
            reachedFrozenOwner = true;
            cap.DemandEffects(new(inserted.AccountId, inserted.CharacterId, inserted));
            return ValueTask.FromResult(1);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Send(world, state, Mode.Declared).AsTask());
        Assert.True(reachedFrozenOwner);
        Assert.Equal(100000, world.Gold);
        Assert.False(world.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(state.Actor!)!.Fenced);
    }

    public enum DeclaredSource { ReadOnly, Main, Internal, Selected, Rejected, MissingOwners, EmptyOwners, DuplicateOwners, OwnersWithoutMode, RecoverCommit, MissingRequest }

    [Theory]
    [InlineData(DeclaredSource.ReadOnly)] [InlineData(DeclaredSource.Main)] [InlineData(DeclaredSource.Internal)]
    [InlineData(DeclaredSource.Selected)] [InlineData(DeclaredSource.Rejected)] [InlineData(DeclaredSource.MissingOwners)]
    [InlineData(DeclaredSource.EmptyOwners)] [InlineData(DeclaredSource.DuplicateOwners)]
    [InlineData(DeclaredSource.OwnersWithoutMode)] [InlineData(DeclaredSource.RecoverCommit)] [InlineData(DeclaredSource.MissingRequest)]
    public async Task Declared_mode_does_not_grant_another_source_or_completion_capability(DeclaredSource source)
    {
        using var world = World(out var state);
        var calls = 0;
        state.DeclaredAction = async (ctx, units, _) =>
        {
            IReadOnlyList<SelectedSessionIdentity>? owners = source == DeclaredSource.MissingOwners ? null
                : source == DeclaredSource.EmptyOwners ? [] : source == DeclaredSource.DuplicateOwners ? [state.Actor!, state.Actor!] : [state.Actor!];
            if (source == DeclaredSource.MissingRequest) ctx = ctx with { Request = null };
            var declaration = Declared(owners) with
            {
                Kind = source == DeclaredSource.Main ? CommandWorkUnitKind.MainSettlement : CommandWorkUnitKind.Ordinary,
                Footprint = source == DeclaredSource.OwnersWithoutMode ? CommandFootprintMode.None : CommandFootprintMode.DeclaredNonRequest,
            };
            if (source == DeclaredSource.ReadOnly)
                return await units.ReadOnlyAsync(ctx, declaration, _ => { calls++; return ValueTask.FromResult(1); });
            if (source == DeclaredSource.RecoverCommit)
            {
                await units.WriteResultAsync(ctx, declaration, (_, _) => { calls++; return ValueTask.FromResult(1); });
                return 1;
            }
            return await units.WriteAsync(ctx, declaration, (_, _) => { calls++; return ValueTask.FromResult(1); });
        };
        if (source == DeclaredSource.Rejected)
            TestGameplay.Run(world.Services, () => world.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(world.Session));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (source == DeclaredSource.Internal)
                await world.Services.GetRequiredService<ICommandBus>().SendInternalAsync(new InternalCap(Mode.Declared),
                    Dfo.Server.Application.Tests.Commands.ProtocolCommandExecutionTests.Origin());
            else
                await Send(world, state, source == DeclaredSource.Selected ? Mode.Selected : source == DeclaredSource.Rejected ? Mode.Rejected : Mode.Declared);
        });
        Assert.Equal(0, calls);
        Assert.Equal(100000, world.Gold);
    }

    [Theory]
    [InlineData("undeclared")] [InlineData("replacement")] [InlineData("account")] [InlineData("character")] [InlineData("no-session")]
    public async Task A_caught_declared_guard_rolls_back_real_SQL_and_unit_messages_and_preserves_the_source_close(string defect)
    {
        using var world = World(out var state);
        var staged = false;
        var outside = new GameInitialConnectionNotice(11, DateTimeOffset.UnixEpoch, "192.0.2.1");
        var inside = outside with { EndpointHost = "192.0.2.2" };
        var outbox = world.Services.GetRequiredService<IGameCommandOutbox>();
        state.DeclaredAction = async (ctx, units, characters) =>
        {
            await outbox.RegisterNotiAsync(world.Connection.ConnectionId, outside, default);
            return await units.WriteAsync(ctx, Declared([state.Actor!]), async (unit, token) =>
            {
                var actor = state.Actor!;
                var session = defect switch
                {
                    "undeclared" => actor with { ConnectionId = new("undeclared") },
                    "replacement" => actor with { ConnectionGeneration = actor.ConnectionGeneration + 1 },
                    _ => actor,
                };
                var owner = new EffectFootprintOwner(defect == "account" ? new(99) : actor.AccountId,
                    defect == "character" ? new(99) : actor.CharacterId, defect == "no-session" ? null : session);
                var guard = Assert.Throws<InvalidOperationException>(() => unit.Footprint.DemandEffects(owner));
                Assert.Same(guard, Assert.Throws<InvalidOperationException>(() => unit.Footprint.DemandActive(actor)));
                // Existing SQL and ordinary registration can still be staged inside the open action;
                // the sticky request forces this whole transaction to roll back before any durability.
                Assert.Equal(99999, await characters.Get(actor.CharacterId).TrySpendGoldAsync(1, token));
                staged = true;
                await outbox.RegisterNotiAsync(world.Connection.ConnectionId, inside, token);
                return 1;
            });
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Send(world, state, Mode.Declared).AsTask());
        Assert.True(staged);
        Assert.Equal(100000, world.Gold);
        Assert.Equal([outside], world.Sender.Messages.OfType<GameInitialConnectionNotice>());
        var entries = world.Sender.Outbound.Entries;
        Assert.Same(world.Connection, Assert.Single(entries, entry => entry.IsClose).Connection);
        Assert.True(entries[^1].IsClose);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Declared_effect_owners_do_not_expand_the_original_failure_protection_set(bool protect)
    {
        using var world = World(out var state);
        var peer = Peer(world);
        var cause = new IOException("after declared commit");
        state.DeclaredAction = async (ctx, units, characters) =>
        {
            await units.WriteAsync(ctx, Declared([state.Actor!, peer.Session], protect ? [new(state.Actor!, world.Connection), peer] : []),
                async (unit, token) =>
                {
                    unit.Footprint.DemandEffects(new(peer.Session.AccountId, peer.Session.CharacterId, peer.Session));
                    Assert.Equal(99999, await characters.Get(state.Actor!.CharacterId).TrySpendGoldAsync(1, token));
                    return 1;
                });
            throw cause;
        };
        var error = await Record.ExceptionAsync(() => Send(world, state, Mode.Declared).AsTask());
        var uncertain = Assert.IsType<Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException>(error);
        Assert.Same(cause, uncertain.InnerException);
        Assert.Equal(99999, world.Gold);
        var sessions = world.Services.GetRequiredService<ISelectedSessionDomainService>();
        Assert.Equal(protect, sessions.StandingOf(state.Actor!)!.Fenced);
        Assert.Equal(protect, sessions.StandingOf(peer.Session)!.Fenced);
        Assert.Contains(world.Connection.ConnectionId, world.Sender.Outbound.Closes);
        Assert.Equal(protect, world.Sender.Outbound.Closes.Contains(peer.Session.ConnectionId));
    }

    [Fact]
    public async Task A_declared_capability_ends_with_its_action_and_cannot_be_caught_and_reused_by_the_next_request()
    {
        using var world = World(out var state);
        IEffectFootprintRecorder? old = null;
        state.DeclaredAction = (ctx, units, _) => units.WriteAsync(ctx, Declared([state.Actor!]), (unit, _) =>
        {
            old = unit.Footprint;
            old.DemandActive(state.Actor!);
            return ValueTask.FromResult(1);
        });
        Assert.Equal(1, await Send(world, state, Mode.Declared));
        state.DeclaredAction = async (ctx, units, characters) => await units.WriteAsync(ctx, Declared([state.Actor!]), async (_, token) =>
        {
            Assert.Throws<InvalidOperationException>(() => old!.DemandActive(state.Actor!));
            Assert.Equal(99999, await characters.Get(state.Actor!.CharacterId).TrySpendGoldAsync(1, token));
            return 1;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Send(world, state, Mode.Declared).AsTask());
        Assert.Equal(100000, world.Gold);
    }

    [Fact]
    public async Task A_declared_guard_on_a_foreign_job_fails_both_the_caller_job_and_the_real_owner_request()
    {
        using var world = World(out var state);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IEffectFootprintRecorder? cap = null;
        state.DeclaredAction = (ctx, units, _) => units.WriteAsync(ctx, Declared([state.Actor!]), async (unit, _) =>
        {
            cap = unit.Footprint; ready.TrySetResult(); await release.Task; return 1;
        });
        var pending = Send(world, state, Mode.Declared).AsTask();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => TestGameplay.RunAsync(() =>
            {
                Assert.Throws<InvalidOperationException>(() => cap!.DemandActive(state.Actor!));
                return Task.CompletedTask;
            }));
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Equal(100000, world.Gold);
    }
    [Theory]
    [InlineData("after-action")] [InlineData("prepare")] [InlineData("next-unit")]
    public async Task Declared_action_closes_before_prepare_or_any_later_unit(string phase)
    {
        using var world = World(out var state);
        state.DeclaredAction = async (ctx, units, characters) =>
        {
            IEffectFootprintRecorder? previous = null;
            await units.WriteAsync(ctx, Declared([state.Actor!]), async (unit, token) =>
            {
                previous = unit.Footprint;
                Assert.Equal(99999, await characters.Get(state.Actor!.CharacterId).TrySpendGoldAsync(1, token));
                if (phase == "prepare")
                    world.Services.GetRequiredService<Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor>().Current!
                        .Enlist("declared-prepare", () => new DeclaredPrepareGuard(() => previous.DemandActive(state.Actor!)));
                return 1;
            });
            if (phase == "next-unit")
                return await units.WriteAsync(ctx, Declared([state.Actor!]), (_, _) =>
                {
                    previous!.DemandActive(state.Actor!);
                    return ValueTask.FromResult(1);
                });
            previous!.DemandActive(state.Actor!);
            return 1;
        };
        var error = await Record.ExceptionAsync(() => Send(world, state, Mode.Declared).AsTask());
        if (phase == "prepare") Assert.IsType<InvalidOperationException>(error);
        else Assert.IsType<Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException>(error);
        Assert.Equal(phase == "prepare" ? 100000 : 99999, world.Gold);
    }

    private sealed class DeclaredPrepareGuard(Action guard) : Dfo.Server.Abstractions.Transactions.ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken token) { guard(); return ValueTask.CompletedTask; }
        public ValueTask CommitAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_replacement_selected_session_does_not_inherit_an_old_declared_participants_fence_or_close()
    {
        using var world = World(out var state);
        var peer = Peer(world);
        SelectedSessionIdentity replacement = null!;
        var cause = new IOException("after declared commit and replacement");
        state.DeclaredAction = async (ctx, units, _) =>
        {
            await units.WriteAsync(ctx, Declared([state.Actor!, peer.Session], [new(state.Actor!, world.Connection), new(peer.Session, null)]),
                (unit, _) => { unit.Footprint.DemandActive(peer.Session); return ValueTask.FromResult(1); });
            var registry = world.Services.GetRequiredService<OnlineStateRegistry>();
            var connection = new NetworkConnectionContext(new("declared-replacement"), NetworkEndpointKind.Game, 10011);
            world.Sender.Outbound.Connections.Add(connection);
            var online = registry.RegisterConnection(connection).OnlineUserId;
            registry.Authenticate(online, peer.Session.AccountId);
            replacement = SelectedSessionIdentity.Create(peer.Session.AccountId, peer.Session.CharacterId, connection.ConnectionId,
                registry.GetRequiredUser(online).Generation, 10011);
            registry.Select(replacement); registry.EnterWorld(replacement);
            throw cause;
        };
        await Assert.ThrowsAsync<Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException>(() => Send(world, state, Mode.Declared).AsTask());
        var current = world.Services.GetRequiredService<ISelectedSessionDomainService>();
        Assert.Equal(replacement, current.FindByCharacter(peer.Session.CharacterId));
        Assert.False(current.StandingOf(replacement)!.Fenced);
        Assert.True(current.StandingOf(state.Actor!)!.Fenced);
        Assert.DoesNotContain(replacement.ConnectionId, world.Sender.Outbound.Closes);
        Assert.Contains(world.Connection.ConnectionId, world.Sender.Outbound.Closes);
    }

}
