using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Channel.Connect;

namespace Dfo.Server.Application.Tests.Channel;

public sealed class ChannelConnectHandlerTests
{
    [Fact]
    public async Task HandleAsync_ForwardsTheOriginalRequestAndExactConnectionContext()
    {
        var bus = new RecordingBus();
        var handler = new ChannelConnectHandler(bus);
        var context = new NetworkDispatchContext(
            new NetworkConnectionContext(new("channel-42"), NetworkEndpointKind.Channel, 7001));
        var request = new ChannelConnectRequest(ChannelMessageMetadata.Empty);

        await handler.HandleAsync(context, request, CancellationToken.None);

        var command = Assert.IsType<ChannelConnectCommand>(Assert.Single(bus.Commands));
        Assert.Same(context, command.Dispatch);
        Assert.Same(request, command.Request);
    }

    private sealed class RecordingBus : ICommandBus
    {
        public List<ICommand> Commands { get; } = [];
        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            Commands.Add(command);
            return ValueTask.FromResult((TResult)(object)default(ChannelConnectCompleted));
        }
    }
}
