using System.Reflection;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Marriage;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CharacterName;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class ExplicitFootprintLifecycleTests
{
    [Fact]
    public async Task Real_dispatcher_main_work_unit_freezes_slots_and_rental_per_request_on_the_same_singleton()
    {
        var sink = new Consumer(); var replay = new Replay();
        using var f = Fixture(sink, replay);
        var dispatcher = f.Services.GetRequiredService<INetworkMessageDispatcher>();
        await dispatcher.DispatchAsync(new(f.Connection), Request(1, 1, true, 1), default);
        await dispatcher.DispatchAsync(new(f.Connection), Request(2, 1, true, 1, fresh: false), default);
        await dispatcher.DispatchAsync(new(f.Connection), Request(3, 2, false, 2), default);
        await dispatcher.DispatchAsync(new(f.Connection), Request(4, 2, false, 2, fresh: false), default);
        Assert.Equal(2, sink.Seen.Count);
        Assert.NotSame(sink.Seen[0].Footprint, sink.Seen[1].Footprint);
        Assert.All(sink.Seen, seen => { Assert.True(seen.InTransaction); Assert.True(seen.OnJob); });
        Assert.Equal(2, sink.Commits); Assert.Equal(0, sink.Rollbacks);
        Assert.Equal(new BusinessSlot(BusinessInventorySpace.Consumable, 1), Assert.Single(replay.Plans[0].TouchedSlots));
        Assert.True(replay.Plans[0].Needs.HasFlag(ReplayProjectionNeeds.Rental));
        Assert.Equal(new BusinessSlot(BusinessInventorySpace.Consumable, 2), Assert.Single(replay.Plans[1].TouchedSlots));
        Assert.False(replay.Plans[1].Needs.HasFlag(ReplayProjectionNeeds.Rental));
        Assert.Equal(2, sink.Seen.Count); // Replays did not execute the consumer again.
        Assert.All(sink.Seen, seen => Assert.Throws<InvalidOperationException>(() => seen.Footprint.RecordRentalState(f.Session)));
    }

    [Fact]
    public async Task Confirmed_rollback_then_next_request_does_not_reuse_the_failed_requests_footprint()
    {
        var sink = new Consumer { FailNext = true }; var replay = new Replay();
        using var f = Fixture(sink, replay);
        var dispatcher = f.Services.GetRequiredService<INetworkMessageDispatcher>();
        await Assert.ThrowsAsync<IOException>(() => dispatcher.DispatchAsync(new(f.Connection), Request(1, 1, true, 1), default).AsTask());
        Assert.Equal(1, sink.Rollbacks); Assert.Equal(0, sink.Commits);
        await dispatcher.DispatchAsync(new(f.Connection), Request(2, 1, true, 1, fresh: false), default); // Confirmed rollback permits this same request again.
        await dispatcher.DispatchAsync(new(f.Connection), Request(3, 2, false, 2), default);
        await dispatcher.DispatchAsync(new(f.Connection), Request(4, 2, false, 2, fresh: false), default);
        var plan = Assert.Single(replay.Plans);
        Assert.Equal(new BusinessSlot(BusinessInventorySpace.Consumable, 2), Assert.Single(plan.TouchedSlots));
        Assert.False(plan.Needs.HasFlag(ReplayProjectionNeeds.Rental));
        Assert.NotSame(sink.Seen[0].Footprint, sink.Seen[1].Footprint);
        Assert.Equal(1, sink.Rollbacks); Assert.Equal(2, sink.Commits);
        Assert.Equal(3, sink.Seen.Select(seen => seen.Footprint).Distinct().Count());
    }

    [Fact]
    public async Task Known_non_effect_network_and_the_actual_trusted_internal_entry_are_legal_without_a_ledger()
    {
        var sink = new Consumer(); using var f = Fixture(sink, new Replay());
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),
            new PlainRequest(GameMessageMetadata.Empty, 1), default);
        await f.Services.GetRequiredService<ICommandBus>().SendInternalAsync(new InternalProbe(2,true),new(GameplayCallSource.Timer,GameplayCallChain.NewRoot(GameplayCallSource.Timer),"test:explicit-footprint"));
        Assert.Equal(2, sink.Seen.Count); Assert.Equal(2, sink.Commits);
        Assert.All(sink.Seen, seen => Assert.True(seen.InTransaction));
    }

    [Fact]
    public void Required_cap_parameters_and_no_ambient_constructor_dependencies_are_enforced_with_bad_shapes()
    {
        var methods = new[]
        {
            typeof(INpcShopDomainService).GetMethod("PurchaseAsync")!, typeof(IItemSocketDomainService).GetMethod("ApplyAsync")!,
            typeof(IAvatarCompoundDomainService).GetMethod("CompoundAsync")!, typeof(IItemUseEffectDomainService).GetMethod("UseAsync")!,
            typeof(ISharedEffectDomainService).GetMethod("UseAsync")!,
            typeof(Dfo.Server.Abstractions.Marriage.IMarriageHousingDomainService).GetMethod("ExecuteAsync")!,
            typeof(Dfo.Server.Abstractions.Marriage.IMarriageCapsuleDomainService).GetMethod("CapsuleAsync")!,
        };
        Assert.All(methods, method => Assert.True(RequiredCap(method)));
        Assert.False(RequiredCap(typeof(BadShapes).GetMethod(nameof(BadShapes.Missing))!));
        Assert.False(RequiredCap(typeof(BadShapes).GetMethod(nameof(BadShapes.Optional))!));
        var domains = new[] { typeof(NpcShopDomainService), typeof(ItemSocketDomainService), typeof(AvatarCompoundDomainService),
            typeof(ItemUseEffectDomainService), typeof(HousingOperations), typeof(GrowthCapsules) };
        Assert.All(domains.SelectMany(type => type.GetConstructors().SelectMany(ctor => ctor.GetParameters())),
            parameter => Assert.False(Forbidden(parameter.ParameterType)));
        Assert.All(new[] { typeof(IEffectFootprintRecorder), typeof(IServiceProvider), typeof(Func<IEffectFootprintRecorder>) }, type => Assert.True(Forbidden(type)));
    }

    private static bool RequiredCap(MethodInfo method) => method.GetParameters().Count(p => p.ParameterType == typeof(IEffectFootprintRecorder) && !p.IsOptional) == 1;
    private static bool Forbidden(Type type) => type == typeof(IEffectFootprintRecorder) || type == typeof(IServiceProvider) || typeof(Delegate).IsAssignableFrom(type);
    private sealed class BadShapes { public void Missing() { } public void Optional(IEffectFootprintRecorder? value = null) { } }
    private static EffectRequest Request(long ordinal, ushort sequence, bool rental, int slot, bool fresh = true) =>
        new((new GameMessageMetadata(1, 3, 0, sequence) { InboundOrdinal = ordinal, SequenceIsFresh = fresh }).WithRawBody([checked((byte)slot), rental ? (byte)1 : (byte)0]), slot, rental);
    private static ShopFixture Fixture(Consumer sink, Replay replay)
    {
        var f = new ShopFixture(services =>
        {
            foreach (var descriptor in services.Where(d => d.ImplementationInstance is MessageHandlerRegistration { MessageType: ClientMessageType.GameExit }).ToArray()) services.Remove(descriptor);
            foreach (var descriptor in services.Where(d => d.ImplementationInstance is CommandHandlerRegistration registration && registration.CommandType == typeof(Dfo.Server.Protocol.Game.Exit.GameExitCommand)).ToArray()) services.Remove(descriptor);
            services.RegisterProtocolMessageHandler<ProbeNetworkHandler, IProbeInput, Probe>(ClientMessageType.GameExit, typeof(EffectRequest),typeof(PlainRequest));
            services.AddProtocolCommandHandler<Probe, IEffectFootprintRecorder, ProbeHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameExit, typeof(EffectRequest)),new ProtocolCommandMessageBinding(ClientMessageType.GameExit, typeof(PlainRequest)));
            services.AddInternalCommandHandler<InternalProbe, IEffectFootprintRecorder, ProbeHandler>();
            services.AddSingleton(sink); services.AddSingleton<IReplayStateDomainService>(replay);services.AddSingleton<IReplayStateProjectionSink>(replay);
        });
        sink.Owner = f.Session;
        return f;
    }
    private interface IProbeInput : IClientNetworkMessage { int Slot { get; } bool Rental { get; } GameMessageMetadata Metadata { get; } }
    private sealed record EffectRequest(GameMessageMetadata Metadata, int Slot, bool Rental) : IProbeInput, IEffectSettlementRequest
    { public ClientMessageType MessageType => ClientMessageType.GameExit; public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.None; }
    private sealed record PlainRequest(GameMessageMetadata Metadata, int Slot) : IProbeInput
    { public ClientMessageType MessageType => ClientMessageType.GameExit; public bool Rental => false; }
    private sealed class ProbeNetworkHandler(ICommandBus bus) : ClientMessageHandler<IProbeInput>, IProtocolCommandMessageHandler<Probe>
    {
        protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, IProbeInput message, CancellationToken ct) => await bus.SendAsync(new Probe(context,message), ct);
    }
    private sealed record Probe(NetworkDispatchContext Dispatch, IProbeInput Input) : IProtocolCommand<IEffectFootprintRecorder>
    { public IClientNetworkMessage Message=>Input; public ProtocolFrameMetadata Frame=>new ProtocolFrameMetadata.Game(Input.Metadata); }
    private sealed record InternalProbe(int Slot,bool Rental) : ICommand<IEffectFootprintRecorder>;
    private sealed class ProbeHandler(Consumer consumer, IDomainTransactionAccessor transactions,ICommandWorkUnits units,SelectedSessionSafety safety)
        : ICommandHandler<Probe, IEffectFootprintRecorder>,ICommandHandler<InternalProbe, IEffectFootprintRecorder>
    {
        public ValueTask<IEffectFootprintRecorder> HandleAsync(CommandExecutionContext c,Probe q,CancellationToken ct)=>Run(c,q.Input.Slot,q.Input.Rental,ct);
        public ValueTask<IEffectFootprintRecorder> HandleAsync(CommandExecutionContext c,InternalProbe q,CancellationToken ct)=>Run(c,q.Slot,q.Rental,ct);
        private ValueTask<IEffectFootprintRecorder> Run(CommandExecutionContext context,int slot,bool rental,CancellationToken ct)
        {
            var selected=context.Request as CommandRequestContext.ProtocolEffectSelected;
            var recipient=selected?.State.Recipient ?? safety.Capture(consumer.Owner,null);
            var declaration=selected is not null ? new CommandWorkUnitDeclaration("effect",[recipient],CommandWorkUnitKind.MainSettlement)
                : new CommandWorkUnitDeclaration("non-request",context.Request is CommandRequestContext.TrustedInternal?[]:[recipient],Footprint:CommandFootprintMode.DeclaredNonRequest,FootprintOwners:[consumer.Owner]);
            return units.WriteAsync(context,declaration,(unit,_)=>{
                consumer.Accept(consumer.Owner,unit.Footprint,slot,rental,transactions.Current is not null);
                transactions.Current!.Enlist(consumer,()=>new Witness(consumer));
                if(consumer.FailNext){consumer.FailNext=false;throw new IOException("injected before-commit failure");}
                return ValueTask.FromResult(unit.Footprint);
            },ct);
        }
    }
    private sealed class Consumer
    {
        public SelectedSessionIdentity Owner { get; set; } = null!;
        public bool FailNext; public int Commits; public int Rollbacks;
        public List<(IEffectFootprintRecorder Footprint, bool InTransaction, bool OnJob)> Seen { get; } = [];
        public void Accept(SelectedSessionIdentity owner, IEffectFootprintRecorder footprint, int slot, bool rental, bool transaction)
        {
            footprint.RecordInventorySlots(owner, [new(BusinessInventorySpace.Consumable, slot)]);
            if (rental) footprint.RecordRentalState(owner);
            Seen.Add((footprint, transaction, GameplayJob.IsOnExecutor));
        }
    }
    private sealed class Witness(Consumer consumer) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct) { consumer.Commits++; return ValueTask.CompletedTask; }
        public ValueTask RollbackAsync(CancellationToken ct) { consumer.Rollbacks++; return ValueTask.CompletedTask; }
    }
    private sealed class Replay : IReplayStateDomainService,IReplayStateProjectionSink
    {
        public List<ReplayProjectionRequest> Plans { get; } = [];
        public ValueTask<ReplayStateSnapshot> ReadAsync(SelectedSessionIdentity session,ReplayProjectionRequest request,GameMessageMetadata? metadata,CancellationToken ct) => ValueTask.FromResult(new ReplayStateSnapshot(session,request,DateTimeOffset.UnixEpoch,null,[],null,null,null,null,null,[],[],[],null));
        public ValueTask ProjectAsync(NetworkConnectionContext connection,SelectedSessionIdentity session,GameMessageMetadata? metadata,ReplayStateSnapshot snapshot,ICommandRegistrationAttempt? attempt,CancellationToken ct)
        { Plans.Add(snapshot.Request);return ValueTask.CompletedTask; }
    }
}
