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
    public async Task GetScriptVersion_ReturnsTheConfiguredVersion()
    {
        var handler = new GetChannelScriptVersionCommandHandler(
            new ChannelDirectoryDomainService(new ChannelContentRuleService(
                new NoChannelCatalog(), new StubScriptSource("66", [0x01]))));
        var command = new GetChannelScriptVersionCommand();

        var result = await handler.HandleAsync(CreateContext(command, handler), command, CancellationToken.None);

        var typed = Assert.IsType<GetChannelScriptVersionResult>(result);
        Assert.Equal("66", typed.Version);
    }

    [Fact]
    public async Task GetScript_ReturnsTheBytesFromThePeripheralSource()
    {
        var handler = new GetChannelScriptCommandHandler(
            new ChannelDirectoryDomainService(new ChannelContentRuleService(
                new NoChannelCatalog(), new StubScriptSource("66", [0x10, 0x20, 0x30]))));
        var command = new GetChannelScriptCommand();

        var result = await handler.HandleAsync(CreateContext(command, handler), command, CancellationToken.None);

        var typed = Assert.IsType<GetChannelScriptResult>(result);
        Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, typed.Content);
    }

    [Fact]
    public async Task GetChannelInfo_ReturnsTheCatalogSnapshot()
    {
        var channels = new[]
        {
            new ChannelServerInfo(11, "#ch.11", 500, 12, "127.0.0.1", 10011)
        };
        var handler = new GetChannelInfoCommandHandler(
            new ChannelDirectoryDomainService(
                new ChannelContentRuleService(new StubChannelCatalog(channels), new NoChannelScript())));
        var command = new GetChannelInfoCommand();

        var result = await handler.HandleAsync(CreateContext(command, handler), command, CancellationToken.None);

        var typed = Assert.IsType<GetChannelInfoResult>(result);
        Assert.Equal(channels, typed.Channels);
    }

    private static CommandExecutionContext CreateContext(ICommand command, object handler) =>
        new(Guid.NewGuid(), command.GetType(), handler.GetType(), CommandExecutionKind.Direct);

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
