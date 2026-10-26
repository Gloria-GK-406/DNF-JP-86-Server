using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Application.Channel;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Channel;
using Dfo.Server.Protocol.Channel.Info;
using Dfo.Server.Protocol.Channel.Script;
using Dfo.Server.Protocol.Channel.ScriptVersion;

namespace Dfo.Server.Application.Tests.Commands.Channel;

public sealed class ChannelControlCommandHandlerTests
{
    [Fact]
    public async Task ScriptVersion_RegistersTheConfiguredVersionToTheOriginalConnection()
    {
        var outbound = new VersionOutbox();
        var handler = new ChannelScriptVersionCommandHandler(
            new ChannelDirectoryDomainService(new ChannelContentRuleService(
                new NoChannelCatalog(), new StubScriptSource("66", [0x01]))), outbound);
        var connection = new NetworkConnectionContext(new("channel-42"), NetworkEndpointKind.Channel, 7001);
        var command = new ChannelScriptVersionCommand(new(connection), new(ChannelMessageMetadata.Empty));
        await TestGameplay.RunAsync(async () =>
            await handler.HandleAsync(CreateContext(command, handler), command, CancellationToken.None));
        Assert.Equal(connection.ConnectionId, Assert.Single(outbound.Recorded.Entries).Connection.ConnectionId);
        Assert.Equal(new ChannelScriptVersionResponse("66"), Assert.Single(outbound.Recorded.Messages));
    }

    [Fact]
    public async Task Script_RegistersThePeripheralBytesToTheOriginalConnection()
    {
        var outbound = new VersionOutbox();
        var handler = new ChannelScriptCommandHandler(
            new ChannelDirectoryDomainService(new ChannelContentRuleService(
                new NoChannelCatalog(), new StubScriptSource("66", [0x10, 0x20, 0x30]))), outbound);
        var connection = new NetworkConnectionContext(new("channel-42"), NetworkEndpointKind.Channel, 7001);
        var command = new ChannelScriptCommand(new(connection), new(ChannelMessageMetadata.Empty));

        await TestGameplay.RunAsync(async () =>
            await handler.HandleAsync(CreateContext(command, handler), command, CancellationToken.None));

        Assert.Equal(connection.ConnectionId, Assert.Single(outbound.Recorded.Entries).Connection.ConnectionId);
        var response = Assert.IsType<ChannelScriptResponse>(Assert.Single(outbound.Recorded.Messages));
        Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, response.Content);
    }

    [Fact]
    public async Task Info_RegistersTheCatalogSnapshotToTheOriginalConnection()
    {
        var channels = new[]
        {
            new ChannelServerInfo(11, "#ch.11", 500, 12, "127.0.0.1", 10011)
        };
        var outbound = new VersionOutbox();
        var handler = new ChannelInfoCommandHandler(
            new ChannelDirectoryDomainService(
                new ChannelContentRuleService(new StubChannelCatalog(channels), new NoChannelScript())), outbound);
        var connection = new NetworkConnectionContext(new("channel-42"), NetworkEndpointKind.Channel, 7001);
        var command = new ChannelInfoCommand(new(connection), new(ChannelMessageMetadata.Empty));

        await TestGameplay.RunAsync(async () =>
            await handler.HandleAsync(CreateContext(command, handler), command, CancellationToken.None));

        Assert.Equal(connection.ConnectionId, Assert.Single(outbound.Recorded.Entries).Connection.ConnectionId);
        var response = Assert.IsType<ChannelInfoResponse>(Assert.Single(outbound.Recorded.Messages));
        Assert.Equal(channels, response.Channels);
    }

    private sealed class VersionOutbox : ICommandMessageOutbox
    {
        public RecordedOutbound Recorded { get; } = new();
        public ValueTask RegisterAsync(NetworkConnectionId connection, IServerNetworkMessage message, CancellationToken ct) =>
            Recorded.Network.SendAsync(connection, message, ct);
        public ValueTask RegisterBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages, CancellationToken ct) =>
            Recorded.Network.SendBatchAsync(connection, messages, ct);
        public ValueTask RegisterExplicitAsync(NetworkConnectionId connection, ExplicitMessageDelivery delivery, CancellationToken ct) =>
            Recorded.Network.SendExplicitAsync(connection, delivery, ct);
    }

    private static CommandExecutionContext CreateContext(ICommand command, object handler) =>
        new(Guid.NewGuid(), command.GetType(), handler.GetType());

    /// <summary>Each of these tests reads one of the two declarations, never both.</summary>
    private sealed class NoChannelCatalog : IChannelCatalog
    {
        public ValueTask<IReadOnlyList<ChannelServerInfo>> GetChannelsAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoChannelScript : IChannelScriptSource
    {
        public string Version => throw new NotSupportedException();

        public ValueTask<byte[]> ReadAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubScriptSource(string version, byte[] content) : IChannelScriptSource
    {
        public string Version => version;

        public ValueTask<byte[]> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(content);
    }

    private sealed class StubChannelCatalog(IReadOnlyList<ChannelServerInfo> channels) : IChannelCatalog
    {
        public ValueTask<IReadOnlyList<ChannelServerInfo>> GetChannelsAsync(
            CancellationToken cancellationToken) => ValueTask.FromResult(channels);
    }
}
