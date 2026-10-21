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
            services.RegisterMessageHandler<RecordingHandler>(ClientMessageType.ChannelConnect);
        });
        var connection = Connection("d1");

        await provider.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(
            new(connection), new ChannelConnectRequest(ChannelMessageMetadata.Empty), default);

        var seen = Assert.Single(recorder.Seen);
        Assert.True(seen.OnExecutor);
        var chain = Assert.NotNull(seen.Chain);
        Assert.Equal(GameplayCallSource.Connection, chain.Source);
        Assert.Equal(connection.ConnectionId, chain.SourceConnection);
        Assert.Equal("inbound:ChannelConnect", chain.CommandType);
        Assert.True(provider.GetRequiredService<GameplayExecutor>().IsStarted);
    }

    [Fact]
    public async Task Request_frames_are_built_inside_the_job_and_reach_the_commands_it_runs()
    {
        var recorder = new Recorder();
        await using var provider = Build(services =>
        {
            services.AddSingleton(recorder);
            services.RegisterMessageHandler<FencingHandler>(ClientMessageType.GameExit);
            services.AddCommandHandler<ProbeCommand, string, ProbeCommandHandler>(CommandExecutionKind.Direct);
        });
        var connection = Connection("d2");
        var metadata = new GameMessageMetadata(1, 0x0003, 0, 7);

        await provider.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(
            new(connection), new ProbeGameMessage(metadata), default);

        var seen = Assert.Single(recorder.Seen);
        Assert.True(seen.OnExecutor);
        Assert.Same(metadata, seen.Metadata);
        Assert.IsType<UnauthorizedAccessException>(seen.CommandError);
        Assert.Equal(0, recorder.CommandRuns);
    }

    [Fact]
    public async Task The_callers_ambient_transaction_does_not_follow_the_message_into_the_job()
    {
        var recorder = new Recorder();
        await using var provider = Build(services =>
        {
            services.AddSingleton(recorder);
            services.RegisterMessageHandler<RecordingHandler>(ClientMessageType.ChannelConnect);
        });
        var scope = provider.GetRequiredService<DomainTransactionScope>();
        var dispatcher = provider.GetRequiredService<INetworkMessageDispatcher>();

        var callerSaw = await TestGameplay.RunAsync(async () =>
        {
            await using var lease = scope.Begin(readOnly: true);
            var open = scope.Current;
            await dispatcher.DispatchAsync(new(Connection("d3")), new ChannelConnectRequest(ChannelMessageMetadata.Empty), default);
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
            services.RegisterMessageHandler<RecordingHandler>(ClientMessageType.ChannelConnect);
        });
        var dispatcher = provider.GetRequiredService<INetworkMessageDispatcher>();

        await Assert.ThrowsAsync<GameplaySelfWaitException>(() => TestGameplay.RunAsync(provider, async () =>
            await dispatcher.DispatchAsync(new(Connection("d4")), new ChannelConnectRequest(ChannelMessageMetadata.Empty), default)));
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
        using var provider = services.BuildServiceProvider();

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
        return services.BuildServiceProvider();
    }

    private static NetworkConnectionContext Connection(string id) =>
        new(new("dispatch-" + id + "-" + Guid.NewGuid().ToString("N")), NetworkEndpointKind.Game, 10011);

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

    private sealed class RecordingHandler(Recorder recorder, IDomainTransactionAccessor transactions)
        : ClientMessageHandler<ChannelConnectRequest>
    {
        protected override ValueTask HandleMessageAsync(
            NetworkDispatchContext context, ChannelConnectRequest message, CancellationToken cancellationToken)
        {
            recorder.Seen.Enqueue(new(GameplayJob.IsOnExecutor, GameplayJob.Current, Transaction: transactions.Current));
            return ValueTask.CompletedTask;
        }
    }

    private sealed record ProbeGameMessage(GameMessageMetadata Metadata) : IClientNetworkMessage
    {
        public ClientMessageType MessageType => ClientMessageType.GameExit;
    }

    /// <summary>Fences its own connection, then sends a command: the command must see the fence.</summary>
    private sealed class FencingHandler(
        Recorder recorder,
        NetworkCommandAuthorization authorization,
        SelectedSessionSafety safety,
        ICommandBus bus) : ClientMessageHandler<ProbeGameMessage>
    {
        protected override async ValueTask HandleMessageAsync(
            NetworkDispatchContext context, ProbeGameMessage message, CancellationToken cancellationToken)
        {
            var metadata = authorization.Metadata;
            await safety.BlockAsync(new(SelectedSessionIdentity.Create(
                new(1), new(1), context.Connection.ConnectionId, 1, 10011), context.Connection), new IOException("unknown"), "test");
            Exception? error = null;
            try { await bus.SendAsync(new ProbeCommand(), cancellationToken); }
            catch (Exception caught) { error = caught; }
            recorder.Seen.Enqueue(new(GameplayJob.IsOnExecutor, GameplayJob.Current, metadata, CommandError: error));
        }
    }

    private sealed record ProbeCommand : ICommand<string>;

    private sealed class ProbeCommandHandler(Recorder recorder) : ICommandHandler<ProbeCommand, string>
    {
        public ValueTask<string> HandleAsync(CommandExecutionContext context, ProbeCommand command, CancellationToken ct)
        {
            Interlocked.Increment(ref recorder.CommandRuns);
            return ValueTask.FromResult("ran");
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
