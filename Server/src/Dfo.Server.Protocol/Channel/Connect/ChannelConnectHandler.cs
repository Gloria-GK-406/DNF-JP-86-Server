using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.Connect;

public sealed class ChannelConnectHandler(
    INetworkMessageSender sender,
    ChannelProtocolKeyProvider keyProvider) : ClientMessageHandler<ChannelConnectRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        ChannelConnectRequest message,
        CancellationToken cancellationToken)
    {
        await sender.SendAsync(
            context.Connection.ConnectionId,
            new ChannelConnectResponse(keyProvider.GetCurrentKey()),
            cancellationToken).ConfigureAwait(false);
    }
}
