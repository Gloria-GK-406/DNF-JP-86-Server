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
            services => services.RegisterFixtureMessageHandler<CountingHandler>(ClientMessageType.ChannelConnect));
        var dispatcher = provider.GetRequiredService<INetworkMessageDispatcher>();

        await dispatcher.DispatchAsync(
            new NetworkDispatchContext(new NetworkConnectionContext(new("channel-fixture"),NetworkEndpointKind.Channel,7001)),
            new ChannelConnectRequest(ChannelMessageMetadata.Empty),
            CancellationToken.None);

        Assert.Equal(1, CountingHandler.ExecutionCount);
    }

    [Fact]
    public async Task DispatchAsync_RejectsAMessageWhoseClrTypeDoesNotMatchTheHandler()
    {
        await using var provider = BuildProvider(
            services => services.RegisterFixtureMessageHandler<CountingHandler>(ClientMessageType.ChannelConnect));
        var dispatcher = provider.GetRequiredService<INetworkMessageDispatcher>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await dispatcher.DispatchAsync(
                new NetworkDispatchContext(new NetworkConnectionContext(new("channel-fixture"),NetworkEndpointKind.Channel,7001)),
                new AlternativeConnectMessage(),
                CancellationToken.None));

        Assert.Contains("unbound", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRegistry_RejectsDuplicateMessageTypeRegistrations()
    {
        var services = new ServiceCollection();
        services.AddNetworkMessageDispatching();
        services.RegisterFixtureMessageHandler<CountingHandler>(ClientMessageType.ChannelConnect);
        services.RegisterFixtureMessageHandler<OtherHandler>(ClientMessageType.ChannelConnect);
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
        services.RegisterFixtureMessageHandler<CountingHandler>(ClientMessageType.ChannelConnect);
        services.AddSingleton(new ClientMessageProductionRegistration(
            ClientMessageType.ChannelConnect, typeof(StubDecoder)));
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<MessageHandlerRegistry>();

        Assert.True(registry.TryGet(ClientMessageType.ChannelConnect,out var route));
        Assert.Equal(MessageHandlerRouteKind.ProtocolCommand,route.Kind);
    }

    [Fact]
    public void BuildRegistry_RejectsADecoderProductionThatNoHandlerConsumes()
    {
        var services = new ServiceCollection();
        services.AddNetworkMessageDispatching();
        services.RegisterFixtureMessageHandler<CountingHandler>(ClientMessageType.ChannelConnect);
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
        await new GameplayDispatchTests().Explicit_request_is_built_inside_the_job_and_blocks_a_unit_after_its_source_is_fenced();
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
