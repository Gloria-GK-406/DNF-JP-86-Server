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

public sealed class LegacyEffectFootprintTests
{
    [Fact]
    public async Task Real_dispatcher_primary_pipeline_freezes_slots_and_rental_per_request_on_the_same_singleton()
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
    public async Task Known_non_effect_network_and_the_actual_standalone_bus_entry_are_legal_without_a_ledger()
    {
        var sink = new Consumer(); using var f = Fixture(sink, new Replay());
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),
            new PlainRequest(GameMessageMetadata.Empty, 1), default);
        await f.Services.GetRequiredService<ICommandBus>().SendAsync(new Probe(f.Session, 2, true));
        Assert.Equal(2, sink.Seen.Count); Assert.Equal(2, sink.Commits);
        Assert.All(sink.Seen, seen => Assert.True(seen.InTransaction));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unknown_executor_or_network_effect_without_operation_cannot_obtain_non_request_authority(bool effect)
    {
        var sink = new Consumer(); using var f = Fixture(sink, new Replay());
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            using var auth = effect ? f.Services.GetRequiredService<NetworkCommandAuthorization>().Enter(
                () => true, GameMessageMetadata.Empty, NetworkCommandAuthorization.MessageCategory.Effect) : null;
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Services.GetRequiredService<ICommandBus>()
                .SendAsync(new Probe(f.Session, 1, false)).AsTask());
        });
        Assert.Empty(sink.Seen); Assert.Equal(0, sink.Commits);
        Assert.Throws<InvalidOperationException>(() => Context().LegacyFootprint);
    }

    [Fact]
    public async Task Rejected_capture_stays_rejected_after_a_later_selected_scope_even_for_empty_slots_and_rental()
    {
        using var f = Fixture(new Consumer(), new Replay());
        TestGameplay.Run(f.Services, () =>
        {
            var operations = f.Services.GetRequiredService<EffectOperationContext>();
            var auth = f.Services.GetRequiredService<NetworkCommandAuthorization>();
            using var network = auth.Enter(() => true, GameMessageMetadata.Empty, NetworkCommandAuthorization.MessageCategory.Effect);
            using var rejection = operations.EnterWithoutSelectedSession();
            var context = Context();
            using var admission = context.EnterLegacyInvocation(f.Services.GetRequiredService<CommandHandlerRegistry>().GetRegistration(typeof(Probe)));
            using var cap = operations.CaptureLegacy(context, auth.Capture(), false, () => { });
            var entry = Entry(f.Session);
            using var laterSelection = operations.Enter(entry, new(f.Session, f.Connection), ReplayProjectionNeeds.None);
            Assert.Throws<InvalidOperationException>(() => cap.RecordInventorySlots(f.Session, []));
            Assert.Throws<InvalidOperationException>(() => cap.RecordRentalState(f.Session));
            Assert.Null(entry.Projection);
        });
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("main")]
    [InlineData("transaction")]
    [InlineData("frozen")]
    [InlineData("operation")]
    [InlineData("execution")]
    [InlineData("lease")]
    [InlineData("authorization")]
    public async Task Captured_state_is_checked_at_each_record_instead_of_a_boolean_snapshot(string invalid)
    {
        using var f = Fixture(new Consumer(), new Replay());
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var operations = f.Services.GetRequiredService<EffectOperationContext>();
            var authorization = f.Services.GetRequiredService<NetworkCommandAuthorization>();
            using var network = authorization.Enter(() => true, GameMessageMetadata.Empty, NetworkCommandAuthorization.MessageCategory.Effect);
            var context = Context(); var command = new Probe(f.Session, 1, false); var entry = Entry(f.Session);
            using var admission = context.EnterLegacyInvocation(f.Services.GetRequiredService<CommandHandlerRegistry>().GetRegistration(typeof(Probe)));
            using var operation = operations.Enter(entry, new(f.Session, f.Connection), ReplayProjectionNeeds.None);
            using var main = operations.BeginMain(context, invalid == "main" ? new PlainCommand() : command);
            await using var transaction = f.Services.GetRequiredService<DomainTransactionScope>().Begin();
            if (invalid != "transaction") operations.TransactionStarted(context, command);
            using var cap = operations.CaptureLegacy(context, authorization.Capture(), false, () => { });
            if (invalid == "frozen") operations.Freeze(context, command);
            if (invalid == "operation") operation.Dispose();
            if (invalid == "execution") main.Dispose();
            if (invalid == "lease") cap.Dispose();
            if (invalid == "authorization") network.Dispose();
            var owner = invalid == "owner" ? SelectedSessionIdentity.Create(new(1), new(2), new("other"), 1, 10011) : f.Session;
            Assert.Throws<InvalidOperationException>(() => cap.RecordInventorySlots(owner, []));
            Assert.Throws<InvalidOperationException>(() => cap.RecordRentalState(owner));
        });
    }

    [Fact]
    public void A_cap_cannot_be_used_after_its_job_ends_even_if_its_local_lease_was_not_disposed()
    {
        using var f = Fixture(new Consumer(), new Replay());
        var captured = TestGameplay.Run(f.Services, () =>
        {
            var context = Context();
            var admission = context.EnterLegacyInvocation(f.Services.GetRequiredService<CommandHandlerRegistry>().GetRegistration(typeof(Probe)));
            var cap = f.Services.GetRequiredService<EffectOperationContext>().CaptureLegacy(context,
                f.Services.GetRequiredService<NetworkCommandAuthorization>().Capture(), true, () => { });
            return (Cap: cap, Admission: admission);
        });
        try { Assert.Throws<InvalidOperationException>(() => captured.Cap.RecordInventorySlots(f.Session, [])); }
        finally { captured.Cap.Dispose(); captured.Admission.Dispose(); }
    }

    [Fact]
    public void Default_context_and_record_copies_cannot_forge_live_legacy_admission()
    {
        using var f = Fixture(new Consumer(), new Replay());
        TestGameplay.Run(f.Services, () =>
        {
            var operations = f.Services.GetRequiredService<EffectOperationContext>();
            var auth = f.Services.GetRequiredService<NetworkCommandAuthorization>();
            using var network = auth.Enter(() => true, GameMessageMetadata.Empty, NetworkCommandAuthorization.MessageCategory.NonEffect);
            Assert.Throws<InvalidOperationException>(() => operations.CaptureLegacy(Context(), auth.Capture(), true, () => { }));
            var context = Context();
            using var admission = context.EnterLegacyInvocation(f.Services.GetRequiredService<CommandHandlerRegistry>().GetRegistration(typeof(Probe)));
            using var cap = operations.CaptureLegacy(context, auth.Capture(), false, () => { });
            context.SupplyLegacyFootprint(cap);
            Assert.Throws<InvalidOperationException>(() => (context with { }).LegacyFootprint);
            Assert.Throws<InvalidOperationException>(() => operations.CaptureLegacy(context with { ExecutionId = Guid.NewGuid() }, auth.Capture(), true, () => { }));
            cap.RecordInventorySlots(f.Session, []);
        });
    }

    [Fact]
    public async Task A_live_cap_does_not_belong_to_another_live_job()
    {
        using var f = Fixture(new Consumer(), new Replay());
        using var other = Fixture(new Consumer(), new Replay());
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var auth = f.Services.GetRequiredService<NetworkCommandAuthorization>();
            using var network = auth.Enter(() => true, GameMessageMetadata.Empty, NetworkCommandAuthorization.MessageCategory.NonEffect);
            var context = Context();
            using var admission = context.EnterLegacyInvocation(f.Services.GetRequiredService<CommandHandlerRegistry>().GetRegistration(typeof(Probe)));
            using var cap = f.Services.GetRequiredService<EffectOperationContext>().CaptureLegacy(context, auth.Capture(), false, () => { });
            cap.RecordInventorySlots(f.Session, []);
            await GameplayJob.RunDetachedAsync(async () =>
            {
                await TestGameplay.RunAsync(other.Services, () =>
                {
                    Assert.Throws<InvalidOperationException>(() => cap.RecordRentalState(f.Session));
                    return Task.CompletedTask;
                });
            });
            cap.RecordInventorySlots(f.Session, []);
        });
    }

    [Fact]
    public async Task An_already_obtained_cap_refuses_the_real_domain_entry_before_its_first_business_delegation()
    {
        var sink = new Consumer(); using var f = Fixture(sink, new Replay());
        var delegated = new ReviveWrite();
        var domain = new ItemUseEffectDomainService(null!, null!, null!, null!, TimeProvider.System, reviveCoins: delegated);
        var item = new ItemReference(new object(), ContainerId.ForCharacter(f.Session.CharacterId, InventoryContainerType.Main), 0, 1,
            ItemSnapshot.FromCore(StackableItemCore.Create(2, 42, 1), true));
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            using var network = f.Services.GetRequiredService<NetworkCommandAuthorization>().Enter(
                () => true, GameMessageMetadata.Empty, NetworkCommandAuthorization.MessageCategory.NonEffect);
            var cap = await f.Services.GetRequiredService<ICommandBus>().SendAsync(new Probe(f.Session, 1, false));
            var error = await Record.ExceptionAsync(async () => await domain.UseAsync(f.Session,
                new(BusinessInventorySpace.Consumable, 0), item, cap, default));
            Assert.Equal(0, delegated.Writes);
            Assert.IsType<InvalidOperationException>(error);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closed_authorization_or_operation_cannot_be_laundered_by_detaching_the_job(bool effect)
    {
        var sink = new Consumer(); using var f = Fixture(sink, new Replay());
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var authorization = f.Services.GetRequiredService<NetworkCommandAuthorization>();
            var operations = f.Services.GetRequiredService<EffectOperationContext>();
            IDisposable scope = effect ? operations.EnterWithoutSelectedSession() : authorization.Enter(
                () => true, GameMessageMetadata.Empty, NetworkCommandAuthorization.MessageCategory.NonEffect);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var inherited = Task.Run(async () =>
            {
                await release.Task;
                await GameplayJob.RunDetachedAsync(async () =>
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(() => f.Services.GetRequiredService<ICommandBus>()
                        .SendAsync(new Probe(f.Session, 1, false)).AsTask());
                });
            });
            scope.Dispose(); release.SetResult(); await inherited;
        });
        Assert.Empty(sink.Seen);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public async Task New_typed_commands_cannot_use_any_legacy_entry_and_catching_it_cannot_enable_a_write(bool protocol, int entry)
    {
        var sink = new Consumer(); using var f = Fixture(sink, new Replay(), explicitProbe: true);
        var bus = f.Services.GetRequiredService<ICommandBus>();
        if (protocol)
            await Assert.ThrowsAsync<InvalidOperationException>(() => bus.SendAsync(new ExplicitProtocol(new(f.Connection),
                new CheckCharacterNameRequest(GameMessageMetadata.Empty, [1]), entry)).AsTask());
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => bus.SendInternalAsync(new ExplicitInternal(entry),
                new(GameplayCallSource.Timer, GameplayCallChain.NewRoot(GameplayCallSource.Timer), "explicit-footprint-probe")).AsTask());
        Assert.Equal(0, sink.Writes); Assert.True(sink.WriteDenied);
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
        Assert.All(new[] { typeof(EffectOperationContext), typeof(NetworkCommandAuthorization), typeof(IEffectFootprintRecorder),
            typeof(IServiceProvider), typeof(Func<IEffectFootprintRecorder>) }, type => Assert.True(Forbidden(type)));
    }

    private static bool RequiredCap(MethodInfo method) => method.GetParameters().Count(p => p.ParameterType == typeof(IEffectFootprintRecorder) && !p.IsOptional) == 1;
    private static bool Forbidden(Type type) => type == typeof(EffectOperationContext) || type == typeof(NetworkCommandAuthorization)
        || type == typeof(IEffectFootprintRecorder) || type == typeof(IServiceProvider) || typeof(Delegate).IsAssignableFrom(type);
    private sealed class BadShapes { public void Missing() { } public void Optional(IEffectFootprintRecorder? value = null) { } }
    private static CommandExecutionContext Context() => new(Guid.NewGuid(), typeof(Probe), typeof(ProbeHandler), CommandExecutionKind.Transactional);
    private static EffectOperationEntry Entry(SelectedSessionIdentity session) => new(session, 1, 1, 3, 1, new string('A', 64), 1);
    private static EffectRequest Request(long ordinal, ushort sequence, bool rental, int slot, bool fresh = true) =>
        new((new GameMessageMetadata(1, 3, 0, sequence) { InboundOrdinal = ordinal, SequenceIsFresh = fresh }).WithRawBody([checked((byte)slot), rental ? (byte)1 : (byte)0]), slot, rental);

    private static ShopFixture Fixture(Consumer sink, Replay replay, bool explicitProbe = false)
    {
        var f = new ShopFixture(services =>
        {
            foreach (var descriptor in services.Where(d => d.ImplementationInstance is MessageHandlerRegistration { MessageType: ClientMessageType.GameExit }).ToArray()) services.Remove(descriptor);
            foreach (var descriptor in services.Where(d => d.ImplementationInstance is CommandHandlerRegistration registration
            && registration.CommandType == typeof(Dfo.Server.Protocol.Game.Exit.GameExitCommand)).ToArray()) services.Remove(descriptor);
        services.RegisterMessageHandler<ProbeNetworkHandler>(ClientMessageType.GameExit);
            services.AddSingleton(sink); services.AddSingleton<IReplayStateProjectionDelivery>(replay);
            services.AddCommandHandler<Probe, IEffectFootprintRecorder, ProbeHandler>(CommandExecutionKind.Transactional);
            if (explicitProbe)
            {
                foreach (var descriptor in services.Where(d => d.ImplementationInstance is CommandHandlerRegistration registration
                    && registration.CommandType == typeof(GameCheckCharacterNameCommand)).ToArray()) services.Remove(descriptor);
                foreach (var descriptor in services.Where(d => d.ImplementationInstance is MessageHandlerRegistration { MessageType: ClientMessageType.GameCheckCharacterName }).ToArray()) services.Remove(descriptor);
                services.AddProtocolCommandHandler<ExplicitProtocol, bool, ExplicitHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameCheckCharacterName, typeof(CheckCharacterNameRequest)));
                services.RegisterProtocolMessageHandler<ExplicitNetworkHandler, CheckCharacterNameRequest, ExplicitProtocol>(ClientMessageType.GameCheckCharacterName);
                services.AddInternalCommandHandler<ExplicitInternal, bool, ExplicitHandler>();
            }
        });
        sink.Owner = f.Session;
        sink.Registration = f.Services.GetRequiredService<CommandHandlerRegistry>().GetRegistration(typeof(Probe));
        return f;
    }

    private interface IProbeInput : IClientNetworkMessage { int Slot { get; } bool Rental { get; } }
    private sealed record EffectRequest(GameMessageMetadata Metadata, int Slot, bool Rental) : IProbeInput, IEffectSettlementRequest
    { public ClientMessageType MessageType => ClientMessageType.GameExit; public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.None; }
    private sealed record PlainRequest(GameMessageMetadata Metadata, int Slot) : IProbeInput
    { public ClientMessageType MessageType => ClientMessageType.GameExit; public bool Rental => false; }
    private sealed class ProbeNetworkHandler(ICommandBus bus, ISelectedSessionDomainService sessions) : IClientMessageHandler
    {
        public bool CanHandle(IClientNetworkMessage message) => message is IProbeInput;
        public async ValueTask HandleAsync(NetworkDispatchContext context, IClientNetworkMessage message, CancellationToken ct)
        {
            var input = (IProbeInput)message;
            if (sessions.FindByConnection(context.Connection.ConnectionId) is { } owner)
                await bus.SendAsync(new Probe(owner, input.Slot, input.Rental), ct);
        }
    }
    private sealed record Probe(SelectedSessionIdentity Session, int Slot, bool Rental) : ICommand<IEffectFootprintRecorder>, IRequestSettlementCommand;
    private sealed record PlainCommand : ICommand<bool>;
    private sealed class ProbeHandler(Consumer consumer, IDomainTransactionAccessor transactions) : ICommandHandler<Probe, IEffectFootprintRecorder>
    {
        public ValueTask<IEffectFootprintRecorder> HandleAsync(CommandExecutionContext context, Probe command, CancellationToken ct)
        {
            var footprint = context.LegacyFootprint;
            consumer.Accept(command.Session, footprint, command.Slot, command.Rental, transactions.Current is not null);
            transactions.Current!.Enlist(consumer, () => new Witness(consumer));
            if (consumer.FailNext) { consumer.FailNext = false; throw new IOException("injected before-commit failure"); }
            return ValueTask.FromResult(footprint);
        }
    }
    private sealed class Consumer
    {
        public SelectedSessionIdentity Owner { get; set; } = null!;
        public CommandHandlerRegistration Registration { get; set; } = null!;
        public bool FailNext; public int Commits; public int Rollbacks; public int Writes; public bool WriteDenied;
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
    private sealed class Replay : IReplayStateProjectionDelivery
    {
        public List<ReplayProjectionRequest> Plans { get; } = [];
        public ValueTask RefreshAsync(NetworkConnectionContext connection, SelectedSessionIdentity session, ReplayProjectionRequest request, GameMessageMetadata? metadata, CancellationToken ct)
        { Plans.Add(request); return ValueTask.CompletedTask; }
    }
    private sealed class ReviveWrite : IReviveCoinDomainService
    {
        public int Writes;
        public ValueTask<ItemUseApplication?> TryUseAsync(SelectedSessionIdentity owner, BusinessSlot source, ItemReference item, IEffectFootprintRecorder footprint, CancellationToken ct)
        { Writes++; return ValueTask.FromResult<ItemUseApplication?>(new(ItemUsability.Allowed, 42, 0)); }
        public ValueTask<DailyReviveCoinResult> ClaimAsync(SelectedSessionIdentity session, IEffectFootprintRecorder footprint, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<bool> ClaimedAsync(CharacterId character, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed record ExplicitInternal(int Entry) : ICommand<bool>;
    private sealed record ExplicitProtocol(NetworkDispatchContext Dispatch, CheckCharacterNameRequest Request, int Entry) : IProtocolCommand<bool>
    { public IClientNetworkMessage Message => Request; public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata); }
    private sealed class ExplicitNetworkHandler(ICommandBus bus) : ClientMessageHandler<CheckCharacterNameRequest>, IProtocolCommandMessageHandler<ExplicitProtocol>
    { protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, CheckCharacterNameRequest message, CancellationToken ct) => await bus.SendAsync(new ExplicitProtocol(context, message, 0), ct); }
    private sealed class ExplicitHandler(Consumer consumer, ICommandWorkUnits units, EffectOperationContext operations, NetworkCommandAuthorization authorization)
        : ICommandHandler<ExplicitInternal, bool>, ICommandHandler<ExplicitProtocol, bool>
    {
        public ValueTask<bool> HandleAsync(CommandExecutionContext context, ExplicitInternal command, CancellationToken ct) => Run(context, command.Entry, ct);
        public ValueTask<bool> HandleAsync(CommandExecutionContext context, ExplicitProtocol command, CancellationToken ct) => Run(context, command.Entry, ct);
        private async ValueTask<bool> Run(CommandExecutionContext context, int entry, CancellationToken ct)
        {
            using var testInput = new TestEffectFootprints.Lease(consumer.Owner);
            try
            {
                if (entry == 0) _ = context.LegacyFootprint;
                else if (entry == 1) using (operations.CaptureLegacy(context, authorization.Capture(), true, () => { })) { }
                else if (entry == 2) context.SupplyLegacyFootprint(testInput);
                else using (context.EnterLegacyInvocation(consumer.Registration)) { }
            }
            catch (InvalidOperationException) { }
            try
            {
                await units.WriteAsync(context, new("after-forbidden-bridge", []), (_, _) =>
                { consumer.Writes++; return ValueTask.FromResult(true); }, ct);
            }
            catch (InvalidOperationException) { consumer.WriteDenied = true; }
            return true;
        }
    }
}
