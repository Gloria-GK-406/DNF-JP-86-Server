using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.Info;

public sealed class ChannelInfoHandler(
    INetworkMessageSender sender,
    ICommandBus commandBus) : ClientMessageHandler<ChannelInfoRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        ChannelInfoRequest message,
        CancellationToken cancellationToken)
    {
        var result = await commandBus.SendAsync(
            new GetChannelInfoCommand(), cancellationToken).ConfigureAwait(false);
        await sender.SendAsync(
            context.Connection.ConnectionId,
            new ChannelInfoResponse(result.Channels),
            cancellationToken).ConfigureAwait(false);
    }
}
