using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public void BuildRegistry_RejectsMissingMessageTypeRegistrations()
    {
        var services = new ServiceCollection();
        services.AddNetworkMessageDispatching();
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<MessageHandlerRegistry>());

        Assert.Contains(nameof(ClientMessageType.ChannelConnect), exception.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddNetworkMessageDispatching();
        configure(services);
        return services.BuildServiceProvider();
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
