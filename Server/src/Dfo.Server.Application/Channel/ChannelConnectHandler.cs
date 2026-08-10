using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Channel;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Channel;

public sealed class ChannelConnectHandler(
    INetworkMessageSender sender,
    ICommandBus commandBus) : ClientMessageHandler<ChannelConnectRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        ChannelConnectRequest message,
        CancellationToken cancellationToken)
    {
        var result = await commandBus.SendAsync(
            new EstablishChannelConnectionCommand(),
            cancellationToken).ConfigureAwait(false);
        await sender.SendAsync(
            context.Connection.ConnectionId,
            new ChannelConnectResponse(result.EncryptionKey),
            cancellationToken).ConfigureAwait(false);
    }
}
