using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Channel.Info;
using Dfo.Server.Protocol.Channel.Script;
using Dfo.Server.Protocol.Channel.ScriptVersion;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Channel;

public sealed class ChannelControlMessageHandlerTests
{
    [Fact]
    public async Task ScriptVersion_ForwardsTheOriginalRequestAndExactConnectionContext()
    {
        var context = CreateContext();
        var request = new ChannelScriptVersionRequest(ChannelMessageMetadata.Empty);
        var calls = 0;
        var bus = new StubCommandBus(command =>
        {
            var protocol = Assert.IsType<ChannelScriptVersionCommand>(command);
            Assert.Same(context, protocol.Dispatch);
            Assert.Same(request, protocol.Request);
            calls++;
            return default(ChannelScriptVersionCompleted);
        });
        var handler = new ChannelScriptVersionHandler(bus);
        await handler.HandleAsync(context, request, CancellationToken.None);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Script_MapsTheCommandResultToTheSameConnection()
    {
        var sender = new RecordingSender();
        var bus = new StubCommandBus(command =>
        {
            Assert.IsType<GetChannelScriptCommand>(command);
            return new GetChannelScriptResult([0x10, 0x20]);
        });
        var handler = new ChannelScriptHandler(sender, bus);

        await TestGameplay.RunAsync(async () => await handler.HandleAsync(
            CreateContext(),
            new ChannelScriptRequest(ChannelMessageMetadata.Empty),
            CancellationToken.None));

        var response = Assert.IsType<ChannelScriptResponse>(sender.Message);
        Assert.Equal(new byte[] { 0x10, 0x20 }, response.Content);
    }

    [Fact]
    public async Task ChannelInfo_MapsTheCommandResultToTheSameConnection()
    {
        var channels = new[]
        {
            new ChannelServerInfo(11, "#ch.11", 500, 0, "127.0.0.1", 10011)
        };
        var sender = new RecordingSender();
        var bus = new StubCommandBus(command =>
        {
            Assert.IsType<GetChannelInfoCommand>(command);
            return new GetChannelInfoResult(channels);
        });
        var handler = new ChannelInfoHandler(sender, bus);

        await TestGameplay.RunAsync(async () => await handler.HandleAsync(
            CreateContext(),
            new ChannelInfoRequest(ChannelMessageMetadata.Empty),
            CancellationToken.None));

        var response = Assert.IsType<ChannelInfoResponse>(sender.Message);
        Assert.Equal(channels, response.Channels);
    }

    private static NetworkDispatchContext CreateContext() =>
        new(new NetworkConnectionContext(
            new NetworkConnectionId("channel-42"),
            NetworkEndpointKind.Channel,
            7001));

    private sealed class StubCommandBus(Func<ICommand, object> execute) : ICommandBus
    {
        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull =>
            ValueTask.FromResult((TResult)execute(command));
    }

    /// <summary>
    /// The last message a real registration adapter registered, read from its outbox; every call goes to it (TASK-25).
    /// An explicit delivery fails where it is registered, with the exception the old double threw.
    /// </summary>
    private sealed class RecordingSender : INetworkMessageSender
    {
        private readonly RecordedOutbound outbound = new();

        public RecordingSender() => outbound.RefuseExplicit(
            () => new InvalidOperationException("This handler does not use explicit-recipient delivery."));

        public NetworkConnectionId ConnectionId => outbound.Entries.LastOrDefault(entry => !entry.IsClose)?.Connection.ConnectionId ?? default;

        public IServerNetworkMessage? Message => outbound.Messages.LastOrDefault();

        public ValueTask SendAsync(
            NetworkConnectionId connectionId,
            IServerNetworkMessage message,
            CancellationToken cancellationToken) =>
            outbound.Network.SendAsync(connectionId, message, cancellationToken);

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            ExplicitMessageDelivery delivery,
            CancellationToken cancellationToken) =>
            outbound.Network.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);
    }
}
