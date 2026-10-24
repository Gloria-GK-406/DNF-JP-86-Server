using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.Info;

public sealed class ChannelInfoHandler(ICommandBus commandBus)
    : ClientMessageHandler<ChannelInfoRequest>, IProtocolCommandMessageHandler<ChannelInfoCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        ChannelInfoRequest message, CancellationToken cancellationToken) =>
        _ = await commandBus.SendAsync(new ChannelInfoCommand(context, message), cancellationToken).ConfigureAwait(false);
}
