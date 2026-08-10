using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Runtime;

internal sealed class TouchSocketNetworkMessageSender(
    TouchSocketConnectionRegistry connections,
    IChannelEncryptionKeyProvider keyProvider) : INetworkMessageSender
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
            ChannelScriptVersionResponse response when connection.Context.EndpointKind == NetworkEndpointKind.Channel =>
                EncodeChannelScriptVersion(response, keyProvider.GetCurrentKey()),
            ChannelScriptResponse response when connection.Context.EndpointKind == NetworkEndpointKind.Channel =>
                EncodeChannelScript(response, keyProvider.GetCurrentKey()),
            ChannelInfoResponse response when connection.Context.EndpointKind == NetworkEndpointKind.Channel =>
                EncodeChannelInfo(response, keyProvider.GetCurrentKey()),
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

    private static byte[] EncodeChannelScriptVersion(
        ChannelScriptVersionResponse response,
        string key)
    {
        var versionBytes = Encoding.ASCII.GetBytes(response.Version);
        if (versionBytes.Length > 16)
        {
            throw new InvalidOperationException("The channel script version cannot exceed 16 ASCII bytes.");
        }

        var plaintext = new byte[20];
        versionBytes.CopyTo(plaintext, 4);
        return EncodeEncrypted(0x06, plaintext, key, compress: false);
    }

    private static byte[] EncodeChannelScript(ChannelScriptResponse response, string key) =>
        EncodeEncrypted(0x0A, response.Content, key, compress: true);

    private static byte[] EncodeChannelInfo(ChannelInfoResponse response, string key)
    {
        var plaintext = new byte[checked(6 + response.Channels.Count * 48)];
        BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(2, 4), response.Channels.Count);
        var offset = 6;
        foreach (var channel in response.Channels)
        {
            WriteFixedAscii(plaintext.AsSpan(offset, 20), channel.Name);
            BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(offset + 20, 4), channel.MaximumUsers);
            BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(offset + 24, 4), channel.CurrentUsers);
            WriteFixedAscii(plaintext.AsSpan(offset + 28, 16), channel.Host);
            BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(offset + 44, 4), channel.Port);
            offset += 48;
        }

        return EncodeEncrypted(0x12, plaintext, key, compress: true);
    }

    private static byte[] EncodeEncrypted(
        byte messageNumber,
        ReadOnlySpan<byte> plaintext,
        string key,
        bool compress)
    {
        var body = ChannelPayloadCipher.Encrypt(plaintext, key, compress);
        return ChannelFrameEncoder.Encode(0x7C, messageNumber, 0, 1, body);
    }

    private static void WriteFixedAscii(Span<byte> target, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
        bytes.AsSpan(0, Math.Min(bytes.Length, target.Length)).CopyTo(target);
    }
}
