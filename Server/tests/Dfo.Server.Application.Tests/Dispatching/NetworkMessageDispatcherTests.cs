using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Channel.Connect;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Dispatching;

public sealed class NetworkMessageDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_ExecutesTheRegisteredHandlerExactlyOnce()
    {
        CountingHandler.ExecutionCount = 0;
        await using var provider = BuildProvider(
            services => services.RegisterMessageHandler<CountingHandler>(ClientMessageType.ChannelConnect));
        var dispatcher = provider.GetRequiredService<INetworkMessageDispatcher>();

        await dispatcher.DispatchAsync(
            NetworkDispatchContext.Unknown,
            new ChannelConnectRequest(ChannelMessageMetadata.Empty),
            CancellationToken.None);

        Assert.Equal(1, CountingHandler.ExecutionCount);
    }

    [Fact]
    public async Task DispatchAsync_RejectsAMessageWhoseClrTypeDoesNotMatchTheHandler()
    {
        await using var provider = BuildProvider(
            services => services.RegisterMessageHandler<CountingHandler>(ClientMessageType.ChannelConnect));
        var dispatcher = provider.GetRequiredService<INetworkMessageDispatcher>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await dispatcher.DispatchAsync(
                NetworkDispatchContext.Unknown,
                new AlternativeConnectMessage(),
                CancellationToken.None));

        Assert.Contains(nameof(CountingHandler), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRegistry_RejectsDuplicateMessageTypeRegistrations()
    {
        var services = new ServiceCollection();
        services.AddNetworkMessageDispatching();
        services.RegisterMessageHandler<CountingHandler>(ClientMessageType.ChannelConnect);
        services.RegisterMessageHandler<OtherHandler>(ClientMessageType.ChannelConnect);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<MessageHandlerRegistry>());

        Assert.Contains("more than once", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The catalogue registers protocols; it does not claim they are all wired. Every member other
    /// than ChannelConnect here has neither decoder nor handler, and that is a normal state.
    /// </summary>
    [Fact]
    public void BuildRegistry_AcceptsCataloguedMessageTypesThatAreNotWired()
    {
        var services = new ServiceCollection();
        services.AddNetworkMessageDispatching();
        services.RegisterMessageHandler<CountingHandler>(ClientMessageType.ChannelConnect);
        services.AddSingleton(new ClientMessageProductionRegistration(
            ClientMessageType.ChannelConnect, typeof(StubDecoder)));
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<MessageHandlerRegistry>();

        Assert.Equal(typeof(CountingHandler), registry.GetHandlerType(ClientMessageType.ChannelConnect));
    }

    [Fact]
    public void BuildRegistry_RejectsADecoderProductionThatNoHandlerConsumes()
    {
        var services = new ServiceCollection();
        services.AddNetworkMessageDispatching();
        services.RegisterMessageHandler<CountingHandler>(ClientMessageType.ChannelConnect);
        services.AddSingleton(new ClientMessageProductionRegistration(
            ClientMessageType.GameLogin, typeof(StubDecoder)));
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<MessageHandlerRegistry>());

        Assert.Contains(nameof(ClientMessageType.GameLogin), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(StubDecoder), exception.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddNetworkMessageDispatching();
        configure(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Blocked_physical_connection_stops_before_handler_lookup_without_login_authority()
    {
        await using var provider = BuildProvider(_ => { });
        var connection = new NetworkConnectionContext(new("blocked"), NetworkEndpointKind.Game, 10011);
        var session = Dfo.Server.Data.World.SelectedSessionIdentity.Create(new(1),new(1),connection.ConnectionId,1,10011);
        var safety = provider.GetRequiredService<Dfo.Server.Application.Commands.SelectedSessionSafety>();
        await TestGameplay.RunAsync(provider, async () =>
        {
            await safety.BlockAsync(new(session, connection), new IOException("unknown"), "test");
        });
        await provider.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(connection),new AlternativeConnectMessage(),default);
        Assert.False(safety.IsBlocked(connection with { }));
        await Assert.ThrowsAsync<KeyNotFoundException>(async () => await provider.GetRequiredService<INetworkMessageDispatcher>()
            .DispatchAsync(new(connection with { }),new AlternativeConnectMessage(),default));
    }

    [Fact]
    public async Task Already_queued_request_without_login_authority_is_rejected_before_uow_after_fence()
    {
        var probe = new QueuedProbe();
        await using var provider = BuildProvider(services =>
        {
            services.RegisterMessageHandler<QueuedHandler>(ClientMessageType.ChannelConnect);
            services.AddSingleton(probe);
            services.AddSingleton<Dfo.Server.Application.Commands.ICommandPipelineBehavior<string>>(probe);
            services.AddCommandHandler<QueuedCommand,string,QueuedCommandHandler>(Dfo.Server.Application.Commands.CommandExecutionKind.Transactional);
        });
        var connection = new NetworkConnectionContext(new("queued"),NetworkEndpointKind.Game,10011);
        var serial = provider.GetRequiredService<Dfo.Server.Application.Commands.Pipeline.TransactionalExecutionGate>();
        var held = await serial.AcquireAsync(default);
        var request = provider.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(connection),new ChannelConnectRequest(ChannelMessageMetadata.Empty),default).AsTask();
        try
        {
            await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var safety = provider.GetRequiredService<Dfo.Server.Application.Commands.SelectedSessionSafety>();
            await safety.BlockAsync(new(Dfo.Server.Data.World.SelectedSessionIdentity.Create(new(1),new(1),connection.ConnectionId,1,10011),connection),new IOException("unknown"),"test");
        }
        finally { await held.DisposeAsync(); }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => request);
        Assert.Equal(0,probe.HandlerCalls);
    }
    private sealed record QueuedCommand : Dfo.Server.Application.Commands.ICommand<string>;
    private sealed class QueuedHandler(Dfo.Server.Application.Commands.ICommandBus bus) : ClientMessageHandler<ChannelConnectRequest>
    {
        protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ChannelConnectRequest message, CancellationToken ct) =>
            _ = await bus.SendAsync(new QueuedCommand(),ct);
    }
    private sealed class QueuedCommandHandler(QueuedProbe probe) : Dfo.Server.Application.Commands.ICommandHandler<QueuedCommand,string>
    {
        public ValueTask<string> HandleAsync(Dfo.Server.Application.Commands.CommandExecutionContext context,QueuedCommand command,CancellationToken ct)
        { probe.HandlerCalls++; return ValueTask.FromResult("paid"); }
    }
    private sealed class QueuedProbe : Dfo.Server.Application.Commands.ICommandPipelineBehavior<string>
    {
        public int Order => 56;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int HandlerCalls;
        public ValueTask<string> ExecuteAsync(Dfo.Server.Application.Commands.CommandExecutionContext context,Dfo.Server.Application.Commands.ICommand command,
            Dfo.Server.Application.Commands.CommandExecutionDelegate<string> next,CancellationToken ct)
        { Entered.TrySetResult(); return next(); }
    }

    private sealed class StubDecoder : IGameMessageDecoder
    {
        public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => null;
    }

    private sealed class CountingHandler : ClientMessageHandler<ChannelConnectRequest>
    {
        public static int ExecutionCount { get; set; }

        protected override ValueTask HandleMessageAsync(
            NetworkDispatchContext context,
            ChannelConnectRequest message,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OtherHandler : ClientMessageHandler<ChannelConnectRequest>
    {
        protected override ValueTask HandleMessageAsync(
            NetworkDispatchContext context,
            ChannelConnectRequest message,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed record AlternativeConnectMessage : IClientNetworkMessage
    {
        public ClientMessageType MessageType => ClientMessageType.ChannelConnect;
    }
}
