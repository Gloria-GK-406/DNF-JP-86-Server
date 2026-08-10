using System.Buffers;
using System.Text;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Runtime;

internal sealed class TouchSocketNetworkMessageSender(
    TouchSocketConnectionRegistry connections) : INetworkMessageSender
{
    public async ValueTask SendAsync(
        NetworkConnectionId connectionId,
        IServerNetworkMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var connection = connections.Get(connectionId);
        var frame = message switch
        {
            ChannelConnectResponse response when connection.Context.EndpointKind == NetworkEndpointKind.Channel =>
                EncodeChannelConnect(response),
            _ => throw new InvalidOperationException(
                $"Server message '{message.GetType().Name}' is not supported for endpoint " +
                $"'{connection.Context.EndpointKind}'.")
        };

        await ((ISender)connection.Session).SendAsync(new ReadOnlySequence<byte>(frame), cancellationToken);
    }

    private static byte[] EncodeChannelConnect(ChannelConnectResponse response)
    {
        var keyBytes = Encoding.ASCII.GetBytes(response.EncryptionKey);
        if (keyBytes.Length > 32)
        {
            throw new InvalidOperationException("The channel connect key cannot exceed 32 ASCII bytes.");
        }

        var body = new byte[36];
        keyBytes.CopyTo(body, 4);
        return ChannelFrameEncoder.Encode(
            classification: 0x7C,
            messageNumber: 0x0C,
            checksum: 0,
            acknowledgement: 1,
            body);
    }
}
