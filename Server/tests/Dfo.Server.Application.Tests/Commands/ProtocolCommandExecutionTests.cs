using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Channel.ScriptVersion;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Tests.Commands;

public sealed class ProtocolCommandExecutionTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Generic_jobs_keep_first_program_error_unless_the_final_body_tree_contains_that_same_reference(bool nested)
    {
        await using var world = new CommandWorld();
        var p = new InvalidOperationException("first program guard"); var q = new InvalidOperationException("later program guard");
        var e = new IOException("independent body");
        Exception body = nested ? new AggregateException("outer", e, new Exception("wrapper", new AggregateException("inner", p, e))) : e;
        var final = await Record.ExceptionAsync(() => world.Services.GetRequiredService<GameplayExecutor>().RunAsync(
            new GameplayWork<int>(default, GameplayCallChain.NewRoot(GameplayCallSource.Connection), "generic-program-control", _ => {
                GameplayJob.RecordProgramError(p); GameplayJob.RecordProgramError(q);
                Assert.Same(p, Record.Exception(GameplayJob.DemandHealthy));
                return ValueTask.FromException<int>(body);
            }), default).AsTask());
        Assert.Same(nested ? body : p, final);
    }

    [Fact]
    public void Program_error_recording_off_executor_has_no_side_effect_and_null_is_rejected()
    {
        Assert.False(GameplayJob.IsOnExecutor); var cause = new IOException("off executor");
        GameplayJob.RecordProgramError(cause); Assert.False(GameplayJob.IsOnExecutor); Assert.Null(GameplayJob.Current);
        Assert.Throws<ArgumentNullException>(() => GameplayJob.RecordProgramError(null!));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void Initial_batch_registration_requires_only_effect_bindings_and_unique_known_protocol_permission(int defect)
    {
        var exits = new[] { CommandRegistrationExit.SettledInitialBatch };
        var row = Registration([new(ClientMessageType.GameUseStackable, typeof(EffectMessage))]) with { RegistrationExits = exits };
        row = defect switch {
            1 => row with { ProtocolMessages = [new(ClientMessageType.GameUseStackable, typeof(EffectMessage)), new(ClientMessageType.ChannelConnect, typeof(FamilyMessage))] },
            2 => row with { RegistrationExits = [CommandRegistrationExit.SettledInitialBatch,CommandRegistrationExit.SettledInitialBatch] },
            3 => row with { RegistrationExits = [(CommandRegistrationExit)99] },
            4 => row with { Route = CommandRouteKind.Internal },
            5 => row with { ProtocolMessages = [] }, _ => row };
        if (defect != 0) { Assert.Throws<InvalidOperationException>(() => new CommandHandlerRegistry([row])); return; }
        var registry = new CommandHandlerRegistry([row]); exits[0] = CommandRegistrationExit.SettledRequiredExplicit;
        Assert.Equal(CommandRegistrationExit.SettledInitialBatch,Assert.Single(registry.GetRegistration(typeof(Probe)).RegistrationExits!));
    }

    [Fact]
    public async Task The_real_script_version_dispatcher_runs_one_job_and_registers_the_original_response()
    {
        await using var world = new CommandWorld(realVersion: true);
        var input = new ChannelScriptVersionRequest(new(0x7C, 5, 0, 1));
        await world.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(world.Source), input, default);
        Assert.Equal(1, world.Queue.Calls);
        var entry = Assert.Single(world.Outbound.Entries);
        Assert.Same(world.Source, entry.Connection);
        Assert.Equal(new ChannelScriptVersionResponse("66"), Assert.Single(entry.Messages));
        Assert.Equal(new byte[] { 6, 6 }, entry.Frames.ToArray());
        Assert.False(entry.IsClose);
    }

    [Fact]
    public async Task The_same_singleton_sees_distinct_explicit_requests_and_the_next_request_survives_a_failure()
    {
        await using var world = new CommandWorld();
        Assert.Equal(2, await world.Bus.SendAsync(new Probe(new(world.Source), new ProbeMessage(), 1)));
        await Assert.ThrowsAsync<IOException>(() => world.Bus.SendAsync(new Probe(new(world.Source), new ProbeMessage(), Mode: ProbeMode.Throw)).AsTask());
        Assert.Equal(4, await world.Bus.SendAsync(new Probe(new(world.Source), new ProbeMessage(), 3)));
        Assert.Equal(3, world.Settings.Seen.Count);
        var requests = world.Settings.Seen.Select(x => x.Request).ToArray();
        Assert.Equal(3, requests.Distinct().Count());
        Assert.All(requests, request => Assert.True(Assert.IsType<CommandRequestContext.ProtocolNonEffect>(request).IsClosed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_empty_failed_job_closes_its_real_source_but_a_rootless_internal_failure_closes_no_target(bool rootless)
    {
        await using var world = new CommandWorld();
        var error = rootless
            ? await Assert.ThrowsAsync<IOException>(() => world.Internal(new InternalProbe(Mode: ProbeMode.Throw)).AsTask())
            : await Assert.ThrowsAsync<IOException>(() => world.Bus.SendAsync(new Probe(new(world.Source), new ProbeMessage(), Mode: ProbeMode.Throw)).AsTask());
        Assert.Same(world.Settings.Failure, error);
        if (rootless) Assert.Empty(world.Outbound.Entries);
        else
        {
            var close = Assert.Single(world.Outbound.Entries);
            Assert.True(close.IsClose);
            Assert.Same(world.Source, close.Connection);
        }
    }

    [Fact]
    public async Task Internal_and_protocol_entries_cannot_be_substituted_for_each_other()
    {
        await using var world = new CommandWorld();
        await Assert.ThrowsAsync<InvalidOperationException>(() => world.Bus.SendAsync(new InternalProbe()).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => world.Bus.SendInternalAsync(
            new Probe(new(world.Source), new ProbeMessage()), Origin()).AsTask());
        Assert.Empty(world.Settings.Seen);
        Assert.Equal(0, world.Queue.Calls);
        Assert.Equal(2, await world.Internal(new InternalProbe(1)));
        Assert.IsType<CommandRequestContext.TrustedInternal>(Assert.Single(world.Settings.Seen).Request);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_caught_nested_dispatch_cannot_execute_a_legacy_or_internal_handler_or_continue_writing(bool internalEntry)
    {
        await using var world = new CommandWorld();
        await Assert.ThrowsAsync<GameplaySelfWaitException>(() => world.Internal(new InternalProbe(
            Mode: internalEntry ? ProbeMode.NestInternalAndCatch : ProbeMode.NestLegacyAndCatch)).AsTask());
        Assert.Equal(0, world.Settings.LegacyCalls);
        Assert.Equal(0, world.Settings.WriteCalls);
        Assert.Equal(1, world.Queue.Calls);
    }

    [Fact]
    public async Task Detaching_the_flow_inside_an_explicit_command_still_does_not_allow_nested_dispatch()
    {
        await using var world = new CommandWorld();
        Assert.Equal(1, await world.Internal(new InternalProbe(Mode: ProbeMode.Detached)));
        Assert.Equal(0, world.Settings.LegacyCalls);
        Assert.Equal(1, world.Queue.Calls);
    }

    [Fact]
    public async Task An_ended_explicit_command_cannot_become_a_new_internal_root()
    {
        await using var world = new CommandWorld();
        await world.Internal(new InternalProbe(Mode: ProbeMode.Leak));
        world.Settings.Release.TrySetResult();
        var error = await Assert.ThrowsAsync<GameplayLeakedFlowException>(() => world.Settings.Leaked!.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains("bus:", error.Message);
        Assert.Single(world.Settings.Seen);
        Assert.Equal(1, world.Queue.Calls);
        Assert.True(Assert.Single(world.Settings.Seen).Request!.IsClosed);
    }

    [Fact]
    public async Task A_wrong_message_CLR_type_is_rejected_before_business_but_after_source_failure_handling_exists()
    {
        await using var world = new CommandWorld();
        await Assert.ThrowsAsync<InvalidOperationException>(() => world.Bus.SendAsync(new Probe(new(world.Source), new OtherMessage())).AsTask());
        Assert.Empty(world.Settings.Seen);
        Assert.True(Assert.Single(world.Outbound.Entries).IsClose);
    }

    [Fact]
    public async Task An_exact_family_accepts_both_shapes_but_an_unknown_derived_message_cannot_reach_the_bus()
    {
        await using var world = FamilyWorld();
        var dispatcher = world.Services.GetRequiredService<INetworkMessageDispatcher>();
        await dispatcher.DispatchAsync(new(world.Source), new FamilyMessage(), default);
        await dispatcher.DispatchAsync(new(world.Source), new FamilyChild(), default);
        Assert.Equal(2, world.Settings.Seen.Count);
        Assert.Equal(2, world.Queue.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(new(world.Source), new FamilyUnknown(), default).AsTask());
        Assert.Equal(2, world.Queue.Calls); // A second guard in the bus cannot hide an incorrect dispatcher admission.
        Assert.Equal(2, world.Settings.Seen.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => world.Bus.SendAsync(new Probe(new(world.Source), new FamilyUnknown())).AsTask());
        Assert.Equal(3, world.Queue.Calls);
        Assert.Equal(2, world.Settings.Seen.Count);
    }

    [Fact]
    public void Finite_route_and_command_families_are_copied_and_must_close_in_both_directions()
    {
        var family = new[] { typeof(FamilyMessage), typeof(FamilyChild) };
        var bindings = family.Select(type => new ProtocolCommandMessageBinding(ClientMessageType.ChannelConnect, type)).ToArray();
        var row = new MessageHandlerRegistration(ClientMessageType.ChannelConnect, typeof(FamilyThin),
            Route: MessageHandlerRouteKind.ProtocolCommand, MessageClrType: typeof(FamilyMessage), ProtocolCommandType: typeof(Probe), AllowedMessageClrTypes: family);
        var registry = new MessageHandlerRegistry([row], [], [Registration(bindings)]);
        family[1] = typeof(FamilyUnknown); bindings[1] = new(ClientMessageType.ChannelConnect, typeof(FamilyUnknown));
        Assert.True(registry.TryGet(ClientMessageType.ChannelConnect, out var route));
        Assert.Equal(new[] { typeof(FamilyMessage), typeof(FamilyChild) }, route!.AllowedMessageClrTypes);
        Assert.Throws<InvalidOperationException>(() => new MessageHandlerRegistry([row with { AllowedMessageClrTypes = [typeof(FamilyMessage)] }], [], [Registration([new(ClientMessageType.ChannelConnect, typeof(FamilyMessage)), new(ClientMessageType.ChannelConnect, typeof(FamilyChild))]) ]));
        Assert.Throws<InvalidOperationException>(() => new MessageHandlerRegistry([row with { AllowedMessageClrTypes = [typeof(FamilyMessage), typeof(FamilyChild)] }], [], [Registration([new(ClientMessageType.ChannelConnect, typeof(FamilyMessage))]) ]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Invalid_family_registrations_cannot_create_a_route(int defect)
    {
        var family = new[] { typeof(FamilyMessage), typeof(FamilyChild) };
        var bindings = family.Select(type => new ProtocolCommandMessageBinding(ClientMessageType.ChannelConnect, type)).ToArray();
        if (defect == 0) bindings[1] = bindings[0];
        if (defect == 1) bindings = [];
        if (defect == 2) bindings[1] = new(ClientMessageType.ChannelConnect, typeof(IClientNetworkMessage));
        if (defect == 3) bindings[1] = new(ClientMessageType.ChannelConnect, typeof(AbstractFamily));
        if (defect == 4) bindings[1] = new(ClientMessageType.ChannelConnect, typeof(GenericFamily<>));
        if (defect == 5) bindings[1] = new(ClientMessageType.ChannelConnect, typeof(string));
        var command = Registration(bindings);
        if (defect is 6 or 7)
        {
            Assert.Throws<InvalidOperationException>(() => new CommandHandlerRegistry([command,
                Registration([new(ClientMessageType.ChannelConnect, defect == 6 ? typeof(FamilyChild) : typeof(FamilyUnknown))]) with { CommandType = typeof(ChannelScriptVersionCommand) }]));
        }
        else Assert.Throws<InvalidOperationException>(() => new CommandHandlerRegistry([command]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Invalid_route_families_cannot_be_normalized_or_widened(int defect)
    {
        Type[] types = defect switch { 0 => [], 1 => [typeof(FamilyMessage), typeof(FamilyMessage)], _ => [typeof(FamilyMessage), typeof(OtherMessage)] };
        Assert.Throws<InvalidOperationException>(() => new MessageHandlerRegistry([
            new(ClientMessageType.ChannelConnect, typeof(FamilyThin), Route: MessageHandlerRouteKind.ProtocolCommand,
                MessageClrType: typeof(FamilyMessage), ProtocolCommandType: typeof(Probe), AllowedMessageClrTypes: types)], [],
            [Registration(types.Select(type => new ProtocolCommandMessageBinding(ClientMessageType.ChannelConnect, type)).ToArray())]));
    }

    [Fact]
    public void A_mixed_family_allows_only_the_existing_committed_replay_exit()
    {
        var mixed = Registration([new(ClientMessageType.ChannelConnect, typeof(FamilyMessage)), new(ClientMessageType.ChannelConnect, typeof(EffectMessage))]);
        var accepted = new CommandHandlerRegistry([mixed with { RegistrationExits = [CommandRegistrationExit.CommittedReplayCurrentStateBatch] }]);
        Assert.Equal(CommandRegistrationExit.CommittedReplayCurrentStateBatch, Assert.Single(accepted.GetRegistration(typeof(Probe)).RegistrationExits!));
        Assert.Throws<InvalidOperationException>(() => new CommandHandlerRegistry([mixed with { RegistrationExits = [CommandRegistrationExit.SettledRequiredExplicit] }]));
        Assert.Throws<InvalidOperationException>(() => new CommandHandlerRegistry([mixed with { RegistrationExits = [CommandRegistrationExit.SettledInitialBatch] }]));
        Assert.Throws<InvalidOperationException>(() => new CommandHandlerRegistry([Registration([new(ClientMessageType.ChannelConnect, typeof(FamilyMessage))]) with { RegistrationExits = [CommandRegistrationExit.CommittedReplayCurrentStateBatch] }]));
    }

    private static CommandWorld FamilyWorld() => new(configure: services =>
    {
        services.RemoveAll<CommandHandlerRegistration>(); services.RemoveAll<MessageHandlerRegistration>();
        services.AddProtocolCommandHandler<Probe, int, ProbeHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.ChannelConnect, typeof(FamilyMessage)), new ProtocolCommandMessageBinding(ClientMessageType.ChannelConnect, typeof(FamilyChild)));
        services.RegisterProtocolMessageHandler<FamilyThin, FamilyMessage, Probe>(ClientMessageType.ChannelConnect,
            typeof(FamilyMessage), typeof(FamilyChild));
    });

    [Fact]
    public void One_logical_protocol_can_bind_a_finite_family_to_one_command()
    {
        var registry = new CommandHandlerRegistry([Registration([
            new(ClientMessageType.ChannelConnect, typeof(FamilyMessage)),
            new(ClientMessageType.ChannelConnect, typeof(FamilyChild))])]);
        Assert.Equal(2, registry.GetRegistration(typeof(Probe)).ProtocolMessages!.Count);
    }

    [Fact]
    public void Registration_bindings_are_frozen_against_mutation_of_the_callers_array()
    {
        var bindings = new[] { new ProtocolCommandMessageBinding(ClientMessageType.ChannelConnect, typeof(ProbeMessage)) };
        var registry = new CommandHandlerRegistry([Registration(bindings)]);
        bindings[0] = new(ClientMessageType.ChannelScript, typeof(OtherMessage));
        Assert.Equal(typeof(ProbeMessage), Assert.Single(registry.GetRegistration(typeof(Probe)).ProtocolMessages!).MessageClrType);
    }

    [Fact]
    public void Registration_exit_permissions_are_frozen_against_mutation_of_the_callers_array()
    {
        var exits = new[] { CommandRegistrationExit.SettledRequiredExplicit, CommandRegistrationExit.CommittedReplayCurrentStateBatch };
        var registry = new CommandHandlerRegistry([Registration([new(ClientMessageType.GameUseStackable, typeof(EffectMessage))]) with { RegistrationExits = exits }]);
        exits[0] = CommandRegistrationExit.CommittedReplayCurrentStateBatch;
        Assert.Equal(new[] { CommandRegistrationExit.SettledRequiredExplicit, CommandRegistrationExit.CommittedReplayCurrentStateBatch }, registry.GetRegistration(typeof(Probe)).RegistrationExits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Startup_rejects_missing_or_incompatible_command_message_and_thin_handler_bindings(int defect)
    {
        var command = Registration([new(ClientMessageType.ChannelConnect, typeof(ProbeMessage))]);
        var message = new MessageHandlerRegistration(ClientMessageType.ChannelConnect,
            defect == 3 ? typeof(OverpoweredThin) : typeof(Thin), Route: MessageHandlerRouteKind.ProtocolCommand,
            MessageClrType: defect == 1 ? typeof(OtherMessage) : typeof(ProbeMessage),
            ProtocolCommandType: typeof(Probe));
        Assert.Throws<InvalidOperationException>(() => new MessageHandlerRegistry(defect == 2 ? [] : [message], [], defect == 0 ? [] : [command]));
    }

    [Fact]
    public void A_non_effect_binding_cannot_claim_the_effect_registration_exits()
    {
        Assert.Throws<InvalidOperationException>(() => new CommandHandlerRegistry([
            Registration([new(ClientMessageType.ChannelConnect, typeof(ProbeMessage))]) with { RegistrationExits = new[] { CommandRegistrationExit.SettledRequiredExplicit } } ]));
    }

    [Fact]
    public void An_explicit_lifecycle_origin_can_retain_the_original_connection_root()
    {
        var connection = new NetworkConnectionContext(new("lifecycle-source"), NetworkEndpointKind.Game, 10011);
        var chain = GameplayCallChain.NewRoot(GameplayCallSource.Connection, connection.ConnectionId);
        new InternalCommandOrigin(GameplayCallSource.Lifecycle, chain, "lifecycle:connected", SourceConnection: connection).Validate();
        Assert.Throws<InvalidOperationException>(() => new InternalCommandOrigin(GameplayCallSource.Connection, chain, "not-internal").Validate());
    }

    internal static InternalCommandOrigin Origin() => new(GameplayCallSource.StartupRecovery,
        GameplayCallChain.NewRoot(GameplayCallSource.StartupRecovery), "test:internal");
    private static CommandHandlerRegistration Registration(IReadOnlyList<ProtocolCommandMessageBinding> messages) =>
        new(typeof(Probe), typeof(ProbeHandler), typeof(CommandInvoker<Probe, int>), CommandExecutionKind.Direct, CommandRouteKind.Protocol, messages);

    internal enum ProbeMode { Plain, Throw, NestLegacyAndCatch, NestInternalAndCatch, Detached, Leak }
    private record FamilyMessage : IClientNetworkMessage { public ClientMessageType MessageType => ClientMessageType.ChannelConnect; }
    private sealed record FamilyChild : FamilyMessage;
    private sealed record FamilyUnknown : FamilyMessage;
    private abstract record AbstractFamily : FamilyMessage;
    private sealed record GenericFamily<T> : FamilyMessage;
    private sealed class FamilyThin(ICommandBus commands) : ClientMessageHandler<FamilyMessage>, IProtocolCommandMessageHandler<Probe>
    {
        protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, FamilyMessage message, CancellationToken ct) =>
            _ = await commands.SendAsync(new Probe(context, message), ct);
    }
    internal sealed record ProbeMessage : IClientNetworkMessage { public ClientMessageType MessageType => ClientMessageType.ChannelConnect; }
    private sealed record EffectMessage : IEffectSettlementRequest
    {
        public ClientMessageType MessageType => ClientMessageType.GameUseStackable;
        public GameMessageMetadata Metadata => GameMessageMetadata.Empty;
        public Dfo.Server.Abstractions.Effects.ReplayProjectionNeeds MinimumReplayNeeds => Dfo.Server.Abstractions.Effects.ReplayProjectionNeeds.None;
    }
    private sealed record OtherMessage : IClientNetworkMessage { public ClientMessageType MessageType => ClientMessageType.ChannelConnect; }
    internal sealed record Probe(NetworkDispatchContext Dispatch, IClientNetworkMessage Message, int Value = 0, ProbeMode Mode = ProbeMode.Plain) : IProtocolCommand<int>
    { public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Channel(ChannelMessageMetadata.Empty); }
    internal sealed record InternalProbe(int Value = 0, ProbeMode Mode = ProbeMode.Plain) : ICommand<int>;
    private sealed record LegacyProbe : ICommand<int>;

    internal sealed class Settings
    {
        public List<CommandExecutionContext> Seen { get; } = [];
        public List<ScopedResource> Resources { get; } = [];
        public IOException Failure { get; } = new("explicit command probe failure");
        public Func<CommandExecutionContext, ValueTask<int>>? Action { get; set; }
        public int LegacyCalls, WriteCalls;
        public Task<int>? Leaked;
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class Thin(ICommandBus commands) : ClientMessageHandler<ProbeMessage>, IProtocolCommandMessageHandler<Probe>
    {
        protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ProbeMessage message, CancellationToken ct) =>
            _ = await commands.SendAsync(new Probe(context, message), ct);
    }
    private sealed class OverpoweredThin(ICommandMessageOutbox outbox)
        : ClientMessageHandler<ProbeMessage>, IProtocolCommandMessageHandler<Probe>
    {
        protected override ValueTask HandleMessageAsync(NetworkDispatchContext context, ProbeMessage message, CancellationToken ct) =>
            outbox.RegisterAsync(context.Connection.ConnectionId, new ChannelScriptVersionResponse("bad"), ct);
    }
    private sealed class LegacyHandler(Settings settings) : ICommandHandler<LegacyProbe, int>
    {
        public ValueTask<int> HandleAsync(CommandExecutionContext context, LegacyProbe command, CancellationToken ct)
        { settings.LegacyCalls++; return ValueTask.FromResult(1); }
    }
    private sealed class ProbeHandler(Settings settings, ICommandBus commands, ICommandWorkUnits units, ScopedResource resource)
        : ICommandHandler<Probe, int>, ICommandHandler<InternalProbe, int>
    {
        public ValueTask<int> HandleAsync(CommandExecutionContext context, Probe command, CancellationToken ct) => Run(context, command.Value, command.Mode, ct);
        public ValueTask<int> HandleAsync(CommandExecutionContext context, InternalProbe command, CancellationToken ct) => Run(context, command.Value, command.Mode, ct);
        private async ValueTask<int> Run(CommandExecutionContext context, int value, ProbeMode mode, CancellationToken ct)
        {
            settings.Seen.Add(context);
            settings.Resources.Add(resource);
            context.DemandRequest();
            if (settings.Action is { } action) return await action(context);
            if (mode == ProbeMode.Throw) throw settings.Failure;
            if (mode is ProbeMode.NestLegacyAndCatch or ProbeMode.NestInternalAndCatch)
            {
                try
                {
                    if (mode == ProbeMode.NestInternalAndCatch) await commands.SendInternalAsync(new InternalProbe(), Origin(), ct);
                    else await commands.SendAsync(new LegacyProbe(), ct);
                }
                catch (GameplaySelfWaitException) { }
                try { await units.WriteAsync(context, new("after-nested", []), (_, _) => { settings.WriteCalls++; return ValueTask.FromResult(1); }, ct); }
                catch (GameplaySelfWaitException) { }
            }
            if (mode == ProbeMode.Detached)
                await Assert.ThrowsAsync<InvalidOperationException>(() => GameplayJob.RunDetachedAsync(() => commands.SendAsync(new LegacyProbe(), ct)).AsTask());
            if (mode == ProbeMode.Leak)
                settings.Leaked = Task.Run(async () => { await settings.Release.Task; return await commands.SendInternalAsync(new InternalProbe(), Origin(), default); });
            return value + 1;
        }
    }

    internal sealed class CommandWorld : IAsyncDisposable
    {
        public ServiceProvider Services { get; }
        public Settings Settings { get; } = new();
        public Output Outbound { get; } = new();
        public NetworkConnectionContext Source { get; } = new(new("explicit-source"), NetworkEndpointKind.Channel, 7001);
        public CountingQueue Queue => (CountingQueue)Services.GetRequiredService<IGameplayQueue>();
        public ICommandBus Bus => Services.GetRequiredService<ICommandBus>();
        public CommandWorld(bool realVersion = false, Action<IServiceCollection>? configure = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton(Settings);
            services.AddScoped<ScopedResource>();
            services.AddSingleton<IOutboundSender>(Outbound);
            services.AddSingleton<IGameplayQueue>(p => new CountingQueue(new GameplayQueue(p.GetRequiredService<GameplayShardSelector>())));
            services.AddNetworkMessageDispatching();
            services.AddSingleton<INetworkConnectionContexts>(new Connections(Source));
            services.AddSingleton<IOutboundMessageEncoder, Encoder>();
            services.AddInternalCommandHandler<InternalProbe, int, ProbeHandler>();
            services.AddCommandHandler<LegacyProbe, int, LegacyHandler>(CommandExecutionKind.Direct);
            services.AddProtocolCommandHandler<Probe, int, ProbeHandler>(new ProtocolCommandMessageBinding(ClientMessageType.ChannelConnect, typeof(ProbeMessage)));
            services.RegisterProtocolMessageHandler<Thin, ProbeMessage, Probe>(ClientMessageType.ChannelConnect);
            if (realVersion)
            {
                services.AddSingleton<IChannelDirectoryDomainService, Channels>();
                services.AddProtocolCommandHandler<ChannelScriptVersionCommand, ChannelScriptVersionCompleted, ChannelScriptVersionCommandHandler>(new ProtocolCommandMessageBinding(ClientMessageType.ChannelScriptVersion, typeof(ChannelScriptVersionRequest)));
                services.RegisterProtocolMessageHandler<ChannelScriptVersionHandler, ChannelScriptVersionRequest, ChannelScriptVersionCommand>(ClientMessageType.ChannelScriptVersion);
            }
            configure?.Invoke(services);
            Services = services.BuildServiceProvider();
        }
        public ValueTask<int> Internal(InternalProbe command) => Bus.SendInternalAsync(command, Origin());
        public ValueTask DisposeAsync() => Services.DisposeAsync();
    }
    internal sealed class ScopedResource : IDisposable
    {
        private int disposed;
        public bool Disposed => Volatile.Read(ref disposed) != 0;
        public void Dispose() => Volatile.Write(ref disposed, 1);
    }
    internal sealed class CountingQueue(IGameplayQueue inner) : IGameplayQueue
    {
        public int Calls { get; private set; }
        public ValueTask<T> RunAsync<T>(GameplayWork<T> work, CancellationToken ct) { Calls++; return inner.RunAsync(work, ct); }
        public ValueTask RunAsync(GameplayWork work, CancellationToken ct) { Calls++; return inner.RunAsync(work, ct); }
    }
    internal sealed class Output : IOutboundSender
    {
        public List<OutboundEntry> Entries { get; } = [];
        public void Submit(IReadOnlyList<OutboundEntry> entries) => Entries.AddRange(entries);
    }
    private sealed class Connections(NetworkConnectionContext source) : INetworkConnectionContexts
    {
        public bool TryGet(NetworkConnectionId id, [NotNullWhen(true)] out NetworkConnectionContext? connection)
        { connection = id == source.ConnectionId ? source : null; return connection is not null; }
    }
    private sealed class Encoder : IOutboundMessageEncoder
    {
        public byte[] EncodeMessage(NetworkConnectionContext connection, IServerNetworkMessage message) => [6, 6];
        public byte[] EncodeBatch(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages) => [6, 6];
    }
    private sealed class Channels : IChannelDirectoryDomainService
    {
        public string ScriptVersion => "66";
        public ValueTask<IReadOnlyList<ChannelServerInfo>> ChannelsAsync(CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<byte[]> ReadScriptAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
