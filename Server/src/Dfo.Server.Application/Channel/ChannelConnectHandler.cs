using System.Globalization;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Channel;

public sealed class ChannelConnectHandler(
    INetworkMessageSender sender,
    TimeProvider timeProvider) : ClientMessageHandler<ChannelConnectRequest>
{
    protected override ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        ChannelConnectRequest message,
        CancellationToken cancellationToken)
    {
        var encryptionKey = timeProvider.GetLocalNow().ToString("yyyyMMdd'000006'", CultureInfo.InvariantCulture);
        return sender.SendAsync(
            context.Connection.ConnectionId,
            new ChannelConnectResponse(encryptionKey),
            cancellationToken);
    }
}
