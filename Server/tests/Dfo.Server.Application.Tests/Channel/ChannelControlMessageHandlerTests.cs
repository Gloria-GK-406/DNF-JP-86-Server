using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Channel.Info;
using Dfo.Server.Protocol.Channel.Script;
using Dfo.Server.Protocol.Channel.ScriptVersion;

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
    public async Task Script_ForwardsTheOriginalRequestAndExactConnectionContext()
    {
        var context = CreateContext();
        var request = new ChannelScriptRequest(ChannelMessageMetadata.Empty);
        var calls = 0;
        var bus = new StubCommandBus(command =>
        {
            var protocol = Assert.IsType<ChannelScriptCommand>(command);
            Assert.Same(context, protocol.Dispatch);
            Assert.Same(request, protocol.Request);
            calls++;
            return default(ChannelScriptCompleted);
        });
        await new ChannelScriptHandler(bus).HandleAsync(context, request, CancellationToken.None);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ChannelInfo_ForwardsTheOriginalRequestAndExactConnectionContext()
    {
        var context = CreateContext();
        var request = new ChannelInfoRequest(ChannelMessageMetadata.Empty);
        var calls = 0;
        var bus = new StubCommandBus(command =>
        {
            var protocol = Assert.IsType<ChannelInfoCommand>(command);
            Assert.Same(context, protocol.Dispatch);
            Assert.Same(request, protocol.Request);
            calls++;
            return default(ChannelInfoCompleted);
        });
        await new ChannelInfoHandler(bus).HandleAsync(context, request, CancellationToken.None);
        Assert.Equal(1, calls);
    }

    private static NetworkDispatchContext CreateContext() =>
        new(new NetworkConnectionContext(new("channel-42"), NetworkEndpointKind.Channel, 7001));

    private sealed class StubCommandBus(Func<ICommand, object> execute) : ICommandBus
    {
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull => ValueTask.FromResult((TResult)execute(command));
    }
}
