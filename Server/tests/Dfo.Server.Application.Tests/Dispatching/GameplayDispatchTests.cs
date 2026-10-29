using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Channel.Connect;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Dispatching;

/// <summary>
/// The dispatcher runs each inbound message as one job on the container's executor, with the
/// request-level frames built inside the job; pure telemetry stays off the executor (RQ-005, RQ-020,
/// RQ-025 transitional shape).
/// </summary>
public sealed class GameplayDispatchTests
{
    [Fact]
    public async Task An_inbound_message_runs_as_a_connection_rooted_job_on_the_container_executor()
    {
        var recorder = new Recorder();
        await using var provider = Build(services =>
        {
            services.AddSingleton(recorder);
            RegisterRecording(services);
        });
        var connection = Connection("d1",NetworkEndpointKind.Channel);

        await provider.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(
            new(connection), new ChannelConnectRequest(ChannelMessageMetadata.Empty), default);

        var seen = Assert.Single(recorder.Seen);
        Assert.True(seen.OnExecutor);
        var chain = Assert.NotNull(seen.Chain);
        Assert.Equal(GameplayCallSource.Connection, chain.Source);
        Assert.Equal(connection.ConnectionId, chain.SourceConnection);
        Assert.Equal(nameof(RecordingCommand), chain.CommandType);
        Assert.True(provider.GetRequiredService<GameplayExecutor>().IsStarted);
    }

    [Fact]
    public async Task Explicit_request_is_built_inside_the_job_and_blocks_a_unit_after_its_source_is_fenced()
    {
        var recorder = new Recorder();
        await using var provider = Build(services =>
        {
            services.AddSingleton(recorder);
            services.RegisterProtocolMessageHandler<FencingHandler,ProbeGameMessage,ProbeCommand>(ClientMessageType.GameExit);
            services.AddProtocolCommandHandler<ProbeCommand,string,ProbeCommandHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameExit,typeof(ProbeGameMessage)));
        });
        var connection = Connection("d2");
        var metadata = new GameMessageMetadata(1, 0x0003, 0, 7);

        await Assert.ThrowsAsync<InvalidOperationException>(()=>provider.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(
            new(connection), new ProbeGameMessage(metadata), default).AsTask());

        var seen = Assert.Single(recorder.Seen);
        Assert.True(seen.OnExecutor);
        Assert.Same(metadata, seen.Metadata);
        Assert.IsType<InvalidOperationException>(seen.CommandError);
        Assert.Equal(0, recorder.CommandRuns);
    }

    [Fact]
    public async Task The_callers_ambient_transaction_does_not_follow_the_message_into_the_job()
    {
        var recorder = new Recorder();
        await using var provider = Build(services =>
        {
            services.AddSingleton(recorder);
            RegisterRecording(services);
        });
        var scope = provider.GetRequiredService<DomainTransactionScope>();
        var dispatcher = provider.GetRequiredService<INetworkMessageDispatcher>();

        var callerSaw = await TestGameplay.RunAsync(async () =>
        {
            await using var lease = scope.Begin(readOnly: true);
            var open = scope.Current;
            await GameplayJob.RunDetachedAsync(()=>dispatcher.DispatchAsync(new(Connection("d3",NetworkEndpointKind.Channel)), new ChannelConnectRequest(ChannelMessageMetadata.Empty), default));
            return open;
        });

        Assert.NotNull(callerSaw);
        var seen = Assert.Single(recorder.Seen);
        Assert.True(seen.OnExecutor);
        Assert.Null(seen.Transaction);
    }

    [Fact]
    public async Task Dispatching_from_a_job_of_the_same_executor_is_a_self_wait()
    {
        await using var provider = Build(services =>
        {
            services.AddSingleton(new Recorder());
            RegisterRecording(services);
        });
        var dispatcher = provider.GetRequiredService<INetworkMessageDispatcher>();

        await Assert.ThrowsAsync<GameplaySelfWaitException>(() => TestGameplay.RunAsync(provider, async () =>
            await dispatcher.DispatchAsync(new(Connection("d4",NetworkEndpointKind.Channel)), new ChannelConnectRequest(ChannelMessageMetadata.Empty), default)));
    }

    [Fact]
    public async Task A_telemetry_message_is_handled_off_the_executor()
    {
        await using var provider = Build(services =>
            services.RegisterTelemetryMessageHandler<TelemetryHandler>(ClientMessageType.GamePi));
        var connection = Connection("d5");

        await provider.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(
            new(connection), new ProbeTelemetryMessage(), default);

        Assert.True(TelemetryHandler.Seen.TryGetValue(connection.ConnectionId, out var onExecutor));
        Assert.False(onExecutor);
        Assert.False(provider.GetRequiredService<GameplayExecutor>().IsStarted);
    }

    [Fact]
    public async Task A_telemetry_message_from_a_blocked_connection_is_not_handled()
    {
        await using var provider = Build(services =>
            services.RegisterTelemetryMessageHandler<TelemetryHandler>(ClientMessageType.GamePi));
        var connection = Connection("d8");
        var safety = provider.GetRequiredService<SelectedSessionSafety>();
        await TestGameplay.RunAsync(provider, async () => await safety.BlockAsync(
            new(SelectedSessionIdentity.Create(new(1), new(1), connection.ConnectionId, 1, 10011), connection),
            new IOException("unknown"), "test"));

        await provider.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(
            new(connection), new ProbeTelemetryMessage(), default);

        Assert.False(TelemetryHandler.Seen.ContainsKey(connection.ConnectionId));
    }

    [Fact]
    public void A_telemetry_handler_that_injects_anything_but_logging_is_refused_at_startup()
    {
        var services = new ServiceCollection();
        services.AddNetworkMessageDispatching();
        services.RegisterTelemetryMessageHandler<TelemetryHandler>(ClientMessageType.GamePi);
        services.RegisterTelemetryMessageHandler<StatefulTelemetryHandler>(ClientMessageType.GameLagStatistics);
        services.RegisterTelemetryMessageHandler<StatefulTelemetryHandler>(ClientMessageType.GameUseSkill);
        using var provider = services.AddSessionSafetyStandIns().BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<MessageHandlerRegistry>());

        Assert.Contains(nameof(StatefulTelemetryHandler), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ClientMessageType.GameLagStatistics), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ClientMessageType.GameUseSkill), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ICommandBus), exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ClientMessageType.GamePi), exception.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddNetworkMessageDispatching();
        configure(services);
        return services.AddSessionSafetyStandIns().BuildServiceProvider();
    }

    private static NetworkConnectionContext Connection(string id,NetworkEndpointKind kind=NetworkEndpointKind.Game) =>
        new(new("dispatch-" + id + "-" + Guid.NewGuid().ToString("N")), kind, 10011);

    private sealed record Seen(
        bool OnExecutor,
        GameplayCallChain? Chain,
        GameMessageMetadata? Metadata = null,
        IDomainTransaction? Transaction = null,
        Exception? CommandError = null);

    private sealed class Recorder
    {
        public ConcurrentQueue<Seen> Seen { get; } = new();
        public int CommandRuns;
    }

    private static void RegisterRecording(IServiceCollection services)
    {
        services.RegisterProtocolMessageHandler<RecordingHandler,ChannelConnectRequest,RecordingCommand>(ClientMessageType.ChannelConnect);
        services.AddProtocolCommandHandler<RecordingCommand,bool,RecordingBody>(new ProtocolCommandMessageBinding(ClientMessageType.ChannelConnect,typeof(ChannelConnectRequest)));
    }
    private sealed record RecordingCommand(NetworkDispatchContext Dispatch,ChannelConnectRequest Input):IProtocolCommand<bool>
    {public IClientNetworkMessage Message=>Input;public ProtocolFrameMetadata Frame=>new ProtocolFrameMetadata.Channel(Input.Metadata);}
    private sealed class RecordingHandler(ICommandBus bus):ClientMessageHandler<ChannelConnectRequest>,IProtocolCommandMessageHandler<RecordingCommand>
    {protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,ChannelConnectRequest message,CancellationToken ct)=>await bus.SendAsync(new RecordingCommand(context,message),ct);}
    private sealed class RecordingBody(Recorder recorder,IDomainTransactionAccessor transactions):ICommandHandler<RecordingCommand,bool>
    {public ValueTask<bool> HandleAsync(CommandExecutionContext context,RecordingCommand command,CancellationToken ct)
        {recorder.Seen.Enqueue(new(GameplayJob.IsOnExecutor,GameplayJob.Current,Transaction:transactions.Current));return ValueTask.FromResult(true);}}
    private sealed record ProbeGameMessage(GameMessageMetadata Metadata):IClientNetworkMessage
    {public ClientMessageType MessageType=>ClientMessageType.GameExit;}
    private sealed record ProbeCommand(NetworkDispatchContext Dispatch,ProbeGameMessage Input):IProtocolCommand<string>
    {public IClientNetworkMessage Message=>Input;public ProtocolFrameMetadata Frame=>new ProtocolFrameMetadata.Game(Input.Metadata);}
    private sealed class FencingHandler(ICommandBus bus):ClientMessageHandler<ProbeGameMessage>,IProtocolCommandMessageHandler<ProbeCommand>
    {protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,ProbeGameMessage message,CancellationToken ct)=>await bus.SendAsync(new ProbeCommand(context,message),ct);}
    private sealed class ProbeCommandHandler(Recorder recorder,SelectedSessionSafety safety,ICommandWorkUnits units):ICommandHandler<ProbeCommand,string>
    {
        public async ValueTask<string> HandleAsync(CommandExecutionContext context,ProbeCommand command,CancellationToken ct)
        {
            await safety.BlockAsync(new(SelectedSessionIdentity.Create(new(1),new(1),command.Dispatch.Connection.ConnectionId,1,10011),command.Dispatch.Connection),new IOException("unknown"),"test");
            Exception? error=null;
            try{await units.WriteAsync(context,new("after-fence",[]),(_,_)=>{recorder.CommandRuns++;return ValueTask.FromResult("ran");},ct);}
            catch(Exception caught){error=caught;}
            recorder.Seen.Enqueue(new(GameplayJob.IsOnExecutor,GameplayJob.Current,command.Input.Metadata,CommandError:error));
            return "refused";
        }
    }

    private sealed record ProbeTelemetryMessage : IClientNetworkMessage
    {
        public ClientMessageType MessageType => ClientMessageType.GamePi;
    }

    /// <summary>Telemetry handlers may only inject logging, so what they saw goes to a static table keyed by connection.</summary>
    private sealed class TelemetryHandler(ILogger<TelemetryHandler> logger) : ClientMessageHandler<ProbeTelemetryMessage>
    {
        public static ConcurrentDictionary<NetworkConnectionId, bool> Seen { get; } = new();

        protected override ValueTask HandleMessageAsync(
            NetworkDispatchContext context, ProbeTelemetryMessage message, CancellationToken cancellationToken)
        {
            logger.LogDebug("telemetry probe {ConnectionId}", context.Connection.ConnectionId);
            Seen[context.Connection.ConnectionId] = GameplayJob.IsOnExecutor;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StatefulTelemetryHandler(ILogger<StatefulTelemetryHandler> logger, ICommandBus bus)
        : ClientMessageHandler<ProbeTelemetryMessage>
    {
        protected override ValueTask HandleMessageAsync(
            NetworkDispatchContext context, ProbeTelemetryMessage message, CancellationToken cancellationToken)
        {
            _ = logger;
            _ = bus;
            return ValueTask.CompletedTask;
        }
    }
}
