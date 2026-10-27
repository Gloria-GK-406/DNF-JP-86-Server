using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.Connect;

public sealed class ChannelConnectHandler(ICommandBus commandBus)
    : ClientMessageHandler<ChannelConnectRequest>, IProtocolCommandMessageHandler<ChannelConnectCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        ChannelConnectRequest message, CancellationToken cancellationToken) =>
        _ = await commandBus.SendAsync(new ChannelConnectCommand(context, message), cancellationToken).ConfigureAwait(false);
}
