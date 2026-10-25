using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Transactions;

public sealed partial class ProtocolNonEffectFootprintTests
{
    private const ClientMessageType MessageId = (ClientMessageType)0x1FFFF;
    [Fact]
    public async Task A_declared_protocol_write_uses_its_actor_without_an_effect_entry_and_each_request_is_independent()
    {
        using var world = World(out var state);
        Assert.Equal(1, await Send(world, state, Mode.Allowed));
        var first = state.Cap;
        Assert.IsType<CommandRequestContext.ProtocolNonEffect>(state.Context!.Request);
        Assert.Equal(99999, world.Gold);
        Assert.Equal(1, await Send(world, state, Mode.Allowed));
        Assert.NotSame(first, state.Cap);
        Assert.Equal(99998, world.Gold);
        Assert.Equal(2, state.Writes);
    }
    [Theory]
    [InlineData(Mode.Undeclared)]
    [InlineData(Mode.ReadOnly)]
    [InlineData(Mode.Main)]
    [InlineData(Mode.NoActor)]
    [InlineData(Mode.TwoActors)]
    [InlineData(Mode.WrongPhysical)]
    [InlineData(Mode.WrongOwner)]
    [InlineData(Mode.WrongEffectsOwner)]
    [InlineData(Mode.WrongRental)]
    [InlineData(Mode.Selected)]
    [InlineData(Mode.Rejected)]
    [InlineData(Mode.Internal)]
    [InlineData(Mode.ReplayPermission)]
    public async Task Wrong_source_or_owner_cannot_reach_a_real_gold_write(Mode mode)
    {
        using var world = World(out var state);
        if (mode == Mode.TwoActors) state.Peer = Peer(world);
        if (mode == Mode.Rejected)
            TestGameplay.Run(world.Services, () => world.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(world.Session));
        var error = await Record.ExceptionAsync(() => mode == Mode.Internal
            ? world.Services.GetRequiredService<ICommandBus>().SendInternalAsync(new InternalCap(Mode.Allowed),
                Dfo.Server.Application.Tests.Commands.ProtocolCommandExecutionTests.Origin()).AsTask()
            : Send(world, state, mode).AsTask());
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(0, state.Writes);
        Assert.Equal(100000, world.Gold);
    }
    [Fact]
    public async Task A_caught_capability_error_is_sticky_and_the_next_real_request_has_a_fresh_capability()
    {
        using var world = World(out var state);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Send(world, state, Mode.CatchOwner).AsTask());
        Assert.Equal(0, state.Writes);
        Assert.Equal(100000, world.Gold);
        Assert.Equal(1, await Send(world, state, Mode.Allowed));
        Assert.Equal(99999, world.Gold);
    }
    [Theory]
    [InlineData(Mode.AfterUnit)]
    [InlineData(Mode.NextUnit)]
    [InlineData(Mode.Prepare)]
    [InlineData(Mode.ScopeClosed)]
    public async Task The_capability_ends_with_the_action_before_prepare_and_cannot_revive_after_close(Mode mode)
    {
        using var world = World(out var state);
        var error = await Record.ExceptionAsync(() => Send(world, state, mode).AsTask());
        if (mode == Mode.Prepare) Assert.IsType<InvalidOperationException>(error);
        else Assert.IsType<TransactionOutcomeUncertainException>(error);
        Assert.Equal(mode == Mode.Prepare ? 100000 : 99999, world.Gold);
        Assert.Equal(1, state.Writes);
        Assert.NotNull(state.Cap);
        if (mode == Mode.ScopeClosed) Assert.True(state.ClosedAtDispose);
    }
    [Fact]
    public async Task A_live_capability_on_a_foreign_job_cannot_write_and_the_owner_is_drained()
    {
        using var world = World(out var state);
        var pending = Send(world, state, Mode.Hold).AsTask();
        Exception? result = null;
        try
        {
            await state.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(state.Context!.Request!.IsClosed);
            await Assert.ThrowsAsync<InvalidOperationException>(() => TestGameplay.RunAsync(() =>
            {
                Assert.NotEqual(state.Job, GameplayJob.Current!.Value.Id);
                state.Cap!.RecordInventorySlots(state.Actor!, []);
                state.ForeignWrites++;
                return Task.CompletedTask;
            }));
            Assert.Equal(0, state.ForeignWrites);
        }
        finally
        {
            state.Release.TrySetResult();
            result = await Record.ExceptionAsync(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        Assert.IsType<InvalidOperationException>(result);
        Assert.Equal(100000, world.Gold);
        Assert.Equal(0, state.Writes);
        Assert.True(pending.IsCompleted);
    }
    [Fact]
    public async Task A_missing_request_cannot_open_a_nonrequest_unit()
    {
        using var world = World(out var state);
        await TestGameplay.RunAsync(world.Services, async () =>
        {
            var context = new CommandExecutionContext(Guid.NewGuid(), typeof(CapCommand), typeof(Handler), CommandExecutionKind.Direct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => world.Services.GetRequiredService<ICommandWorkUnits>().WriteAsync(context,
                new("missing", [], Footprint: CommandFootprintMode.NonRequest), (_, _) => { state.Writes++; return ValueTask.FromResult(1); }).AsTask());
        });
        Assert.Equal(0, state.Writes);
        Assert.Equal(100000, world.Gold);
    }
    private static ShopFixture World(out Settings state)
    {
        var settings = new Settings(); state = settings;
        var world = new ShopFixture(services =>
        {
            services.AddSingleton(settings);
            services.AddScoped<DisposeProbe>();
            services.AddProtocolCommandHandler<CapCommand, int, Handler>(
                new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
                new ProtocolCommandMessageBinding(MessageId, typeof(CapMessage)),
                new ProtocolCommandMessageBinding(MessageId, typeof(EffectCapMessage)));
            services.RegisterProtocolMessageHandler<Thin, CapMessage, CapCommand>(MessageId, typeof(CapMessage), typeof(EffectCapMessage));
            services.AddInternalCommandHandler<InternalCap, int, Handler>();
        });
        settings.Actor = world.Session; settings.Connection = world.Connection; settings.Online = world.OnlineUser;
        return world;
    }
    private static SelectedSessionRecipient Peer(ShopFixture world)
    {
        world.Sql("INSERT INTO accounts(account_id,m_id) VALUES(2,'cap-peer'); INSERT INTO characters(character_id,account_id,name,job,level,slot_index) VALUES(3,2,'peer',0,50,0)");
        var connection = new NetworkConnectionContext(new("cap-peer"), NetworkEndpointKind.Game, 10011);
        world.Sender.Outbound.Connections.Add(connection);
        return TestGameplay.Run(world.Services, () =>
        {
            var registry = world.Services.GetRequiredService<OnlineStateRegistry>();
            var user = registry.RegisterConnection(connection); registry.Authenticate(user.OnlineUserId, new(2));
            var peer = SelectedSessionIdentity.Create(new(2), new(3), connection.ConnectionId, registry.GetRequiredUser(user.OnlineUserId).Generation, 10012);
            registry.Select(peer); registry.EnterWorld(peer);
            return new SelectedSessionRecipient(peer, connection);
        });
    }
    private static ValueTask<int> Send(ShopFixture world, Settings state, Mode mode)
    {
        var metadata = (new GameMessageMetadata(1, 0xFFFF, 0, ++state.Sequence) { InboundOrdinal = state.Sequence, SequenceIsFresh = true }).WithRawBody([1]);
        CapMessage input = mode is Mode.Selected or Mode.Rejected ? new EffectCapMessage(metadata) : new CapMessage(metadata);
        return world.Services.GetRequiredService<ICommandBus>().SendAsync(new CapCommand(new(world.Connection), input, mode));
    }
    public enum Mode { Allowed, Undeclared, ReadOnly, Main, NoActor, TwoActors, WrongPhysical, WrongOwner, WrongEffectsOwner, WrongRental, Selected, Rejected, Internal, CatchOwner, AfterUnit, Prepare, ScopeClosed, Hold, ReplayPermission, NextUnit, Declared }
    private record CapMessage(GameMessageMetadata Metadata) : IClientNetworkMessage { public ClientMessageType MessageType => MessageId; }
    private sealed record EffectCapMessage(GameMessageMetadata Metadata) : CapMessage(Metadata), IEffectSettlementRequest
    { public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory; }
    private sealed record CapCommand(NetworkDispatchContext Dispatch, CapMessage Input, Mode Mode) : IProtocolCommand<int>
    { public IClientNetworkMessage Message => Input; public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata); }
    private sealed record InternalCap(Mode Mode) : ICommand<int>;
    private sealed class Thin(ICommandBus bus) : ClientMessageHandler<CapMessage>, IProtocolCommandMessageHandler<CapCommand>
    { protected override async ValueTask HandleMessageAsync(NetworkDispatchContext ctx, CapMessage message, CancellationToken ct) => _ = await bus.SendAsync(new CapCommand(ctx, message, Mode.Allowed), ct); }
    private sealed class Settings
    {
        internal SelectedSessionIdentity? Actor; internal NetworkConnectionContext? Connection;
        internal Dfo.Server.Data.OnlineUsers.OnlineUserId Online;
        internal SelectedSessionRecipient? Peer; internal CommandExecutionContext? Context; internal IEffectFootprintRecorder? Cap;
        internal Func<CommandExecutionContext, ICommandWorkUnits, ICharacterDomainService, ValueTask<int>>? DeclaredAction;
        internal ushort Sequence; internal int Writes, ForeignWrites; internal Guid Job; internal Mode Mode; internal bool ClosedAtDispose;
        internal readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class Handler(Settings state, ICommandWorkUnits units, ICharacterDomainService characters,
        OnlineStateRegistry registry, IDomainTransactionAccessor transactions, DisposeProbe dispose) : ICommandHandler<CapCommand, int>, ICommandHandler<InternalCap, int>
    {
        public ValueTask<int> HandleAsync(CommandExecutionContext ctx, CapCommand command, CancellationToken ct) => Run(ctx, command.Mode, ct);
        public ValueTask<int> HandleAsync(CommandExecutionContext ctx, InternalCap command, CancellationToken ct) => Run(ctx, command.Mode, ct);
        private async ValueTask<int> Run(CommandExecutionContext context, Mode mode, CancellationToken ct)
        {
            _ = dispose;
            state.Context = context; state.Mode = mode; state.Job = GameplayJob.Current!.Value.Id;
            if (mode == Mode.Rejected)
            {
                state.Actor = SelectedSessionIdentity.Create(state.Actor!.AccountId, state.Actor.CharacterId, state.Connection!.ConnectionId,
                    registry.GetRequiredUser(state.Online).Generation, 10011);
                registry.Select(state.Actor); registry.EnterWorld(state.Actor);
            }
            if (state.DeclaredAction is { } declared) return await declared(context, units, characters);
            if (mode == Mode.ReplayPermission)
                context.Request!.BeginRegistrationAttempt(CommandRegistrationExit.CommittedReplayCurrentStateBatch, state.Connection!);
            var actor = new SelectedSessionRecipient(state.Actor!, mode == Mode.WrongPhysical ? state.Connection! with { } : state.Connection);
            SelectedSessionRecipient[] participants = mode == Mode.NoActor ? [] : mode == Mode.TwoActors ? [actor, state.Peer!] : [actor];
            var declaration = new CommandWorkUnitDeclaration("nonrequest", participants,
                mode == Mode.Main ? CommandWorkUnitKind.MainSettlement : CommandWorkUnitKind.Ordinary,
                mode == Mode.Undeclared ? CommandFootprintMode.None : CommandFootprintMode.NonRequest);
            if (mode == Mode.ReadOnly) return await units.ReadOnlyAsync(context, declaration, _ => ValueTask.FromResult(1), ct);
            var result = await units.WriteAsync(context, declaration, async (unit, token) =>
            {
                state.Cap = unit.Footprint;
                if (mode is Mode.WrongOwner or Mode.CatchOwner)
                {
                    try { state.Cap.RecordInventorySlots(state.Actor! with { CharacterId = new(2) }, []); }
                    catch when (mode == Mode.CatchOwner) { }
                }
                if (mode == Mode.WrongEffectsOwner) state.Cap.RecordEffects(new(new(2), state.Actor!.CharacterId, state.Actor), []);
                if (mode == Mode.WrongRental) state.Cap.RecordRentalState(state.Actor! with { CharacterId = new(2) });
                state.Cap.RecordEffects(new(state.Actor!.AccountId, state.Actor.CharacterId, state.Actor), []);
                state.Cap.RecordInventorySlots(state.Actor, []); state.Cap.RecordRentalState(state.Actor);
                if (mode == Mode.Hold) { state.Ready.TrySetResult(); await state.Release.Task; }
                state.Cap.DemandActive(state.Actor);
                await characters.Get(state.Actor.CharacterId).TrySpendGoldAsync(1, token);
                state.Writes++;
                if (mode == Mode.Prepare)
                    transactions.Current!.Enlist("cap-prepare", () => new PrepareProbe(state));
                return 1;
            }, ct);
            if (mode == Mode.NextUnit)
            {
                var previous = state.Cap!;
                await units.WriteAsync(context, declaration, (unit, token) =>
                {
                    _ = unit.Footprint;
                    previous.RecordInventorySlots(state.Actor!, []);
                    state.Writes++;
                    return ValueTask.FromResult(1);
                }, ct);
            }
            if (mode == Mode.AfterUnit) state.Cap!.RecordInventorySlots(state.Actor!, []);
            return result;
        }
    }
    private sealed class PrepareProbe(Settings state) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) { state.Cap!.DemandActive(state.Actor!); return ValueTask.CompletedTask; }
        public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
    private sealed class DisposeProbe(Settings state) : IDisposable
    {
        public void Dispose()
        {
            if (state.Mode != Mode.ScopeClosed) return;
            state.ClosedAtDispose = state.Context!.Request!.IsClosed;
            state.Cap!.RecordRentalState(state.Actor!);
        }
    }
}
