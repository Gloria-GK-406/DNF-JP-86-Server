using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Tests.Dispatching;

public sealed class ClientMessageHandlerTests
{
    [Fact]
    public void CanHandle_AcceptsOnlyTheDeclaredMessageClrType()
    {
        var handler = new ConnectHandler();

        Assert.True(handler.CanHandle(new ChannelConnectRequest(ChannelMessageMetadata.Empty)));
        Assert.False(handler.CanHandle(new AlternativeConnectMessage()));
    }

    [Fact]
    public async Task UntypedHandle_DelegatesToTypedImplementation()
    {
        var handler = new ConnectHandler();
        var message = new ChannelConnectRequest(ChannelMessageMetadata.Empty);

        await handler.HandleAsync(NetworkDispatchContext.Unknown, message, CancellationToken.None);

        Assert.Same(message, handler.HandledMessage);
    }

    private sealed class ConnectHandler : ClientMessageHandler<ChannelConnectRequest>
    {
        public ChannelConnectRequest? HandledMessage { get; private set; }

        protected override ValueTask HandleMessageAsync(
            NetworkDispatchContext context,
            ChannelConnectRequest message,
            CancellationToken cancellationToken)
        {
            HandledMessage = message;
            return ValueTask.CompletedTask;
        }
    }

    private sealed record AlternativeConnectMessage : IClientNetworkMessage
    {
        public ClientMessageType MessageType => ClientMessageType.ChannelConnect;
    }
}
