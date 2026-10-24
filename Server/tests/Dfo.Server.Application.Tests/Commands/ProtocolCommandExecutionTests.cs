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

namespace Dfo.Server.Application.Tests.Commands;

public sealed class ProtocolCommandExecutionTests
{
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
    public void Registration_bindings_are_frozen_against_mutation_of_the_callers_array()
    {
        var bindings = new[] { new ProtocolCommandMessageBinding(ClientMessageType.ChannelConnect, typeof(ProbeMessage)) };
        var registry = new CommandHandlerRegistry([Registration(bindings)]);
        bindings[0] = new(ClientMessageType.ChannelScript, typeof(OtherMessage));
        Assert.Equal(typeof(ProbeMessage), Assert.Single(registry.GetRegistration(typeof(Probe)).ProtocolMessages!).MessageClrType);
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
    public void Explicit_effect_registration_remains_disabled_before_business_or_a_main_unit_can_run()
    {
        Assert.Throws<NotSupportedException>(() => new CommandHandlerRegistry([
            Registration([new(ClientMessageType.GameUseStackable, typeof(EffectMessage))]) ]));
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
