using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Configuration;
using Dfo.Server.Networking.Framing;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Runtime;

internal sealed class TouchSocketNetworkMessageSender(
    TouchSocketConnectionRegistry connections,
    IChannelEncryptionKeyProvider keyProvider,
    GameLoginOptions gameLoginOptions,
    ISelectCharacterProjectionComposer projectionComposer) : INetworkMessageSender
{
    public async ValueTask SendAsync(
        NetworkConnectionId connectionId,
        IServerNetworkMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message is SelectCharacterProjectionDelivery delivery)
        {
            await DeliverProjectionAsync(
                connectionId,
                delivery,
                projectionComposer,
                SendFrameAsync,
                cancellationToken);
            return;
        }

        var connection = connections.Get(connectionId);
        var frames = message switch
        {
            ChannelConnectResponse response when connection.Context.EndpointKind == NetworkEndpointKind.Channel =>
                One(EncodeChannelConnect(response)),
            ChannelScriptVersionResponse response when connection.Context.EndpointKind == NetworkEndpointKind.Channel =>
                One(EncodeChannelScriptVersion(response, keyProvider.GetCurrentKey())),
            ChannelScriptResponse response when connection.Context.EndpointKind == NetworkEndpointKind.Channel =>
                One(EncodeChannelScript(response, keyProvider.GetCurrentKey())),
            ChannelInfoResponse response when connection.Context.EndpointKind == NetworkEndpointKind.Channel =>
                One(EncodeChannelInfo(response, keyProvider.GetCurrentKey())),
            GameExitResponse response when connection.Context.EndpointKind == NetworkEndpointKind.Game =>
                One(EncodeGameExit(response)),
            GameInitialConnectionNotice response when connection.Context.EndpointKind == NetworkEndpointKind.Game =>
                One(GameInitialConnectionPayloadCodec.Encode(response, gameLoginOptions)),
            GameLoginResponse response when connection.Context.EndpointKind == NetworkEndpointKind.Game =>
                One(EncodeGameLogin(response, gameLoginOptions)),
            AccountCharacterRosterResponse response when connection.Context.EndpointKind == NetworkEndpointKind.Game =>
                One(EncodeAccountCharacterRoster(response)),
            SelectCharacterResponse response when connection.Context.EndpointKind == NetworkEndpointKind.Game =>
                SelectCharacterResponseComposer.Encode(response.Result),
            _ => throw new InvalidOperationException(
                $"Server message '{message.GetType().Name}' is not supported for endpoint " +
                $"'{connection.Context.EndpointKind}'.")
        };

        foreach (var frame in frames)
        {
            await ((ISender)connection.Session).SendAsync(
                new ReadOnlySequence<byte>(frame),
                cancellationToken);
        }
    }

    internal static async ValueTask DeliverProjectionAsync(
        NetworkConnectionId requiredDirectConnectionId,
        SelectCharacterProjectionDelivery delivery,
        ISelectCharacterProjectionComposer composer,
        Func<NetworkConnectionId, ReadOnlyMemory<byte>, CancellationToken, ValueTask> sendFrame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(sendFrame);
        ArgumentNullException.ThrowIfNull(composer);
        ValidateProjectionDelivery(delivery);

        foreach (var frame in composer.Compose(delivery.RequiredDirectProjections))
        {
            await sendFrame(requiredDirectConnectionId, frame, cancellationToken);
        }

        foreach (var target in delivery.BestEffortTargets)
        {
            if (target.ConnectionId == requiredDirectConnectionId)
                continue;

            foreach (var frame in composer.Compose(target.OrderedProjections))
            {
                try
                {
                    await sendFrame(target.ConnectionId, frame, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // A disconnected or failed optional recipient does not prevent
                    // later explicit recipients from receiving their own ordered batch.
                    break;
                }
            }
        }
    }

    private async ValueTask SendFrameAsync(
        NetworkConnectionId connectionId,
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        var connection = connections.Get(connectionId);
        if (connection.Context.EndpointKind != NetworkEndpointKind.Game)
        {
            throw new InvalidOperationException(
                $"Projection delivery requires a game endpoint, not '{connection.Context.EndpointKind}'.");
        }

        await ((ISender)connection.Session).SendAsync(
            new ReadOnlySequence<byte>(frame),
            cancellationToken);
    }

    private static void ValidateProjectionDelivery(SelectCharacterProjectionDelivery delivery)
    {
        if (delivery.RequiredDirectProjections is null)
            throw new InvalidDataException("Required direct projections are unavailable.");
        if (delivery.BestEffortTargets is null)
            throw new InvalidDataException("Best-effort projection targets are unavailable.");
        foreach (var projection in delivery.RequiredDirectProjections)
        {
            if (projection is null)
                throw new InvalidDataException("A required direct projection is unavailable.");
        }
        foreach (var target in delivery.BestEffortTargets)
        {
            if (target is null || target.OrderedProjections is null)
                throw new InvalidDataException("An explicit projection target is unavailable.");
            foreach (var projection in target.OrderedProjections)
            {
                if (projection is null)
                    throw new InvalidDataException("A best-effort projection is unavailable.");
            }
        }
    }

    private static IReadOnlyList<byte[]> One(byte[] frame) => [frame];

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

    private static byte[] EncodeGameExit(GameExitResponse response) =>
        GameServerFrameEncoder.Encode(
            GameProtocol.Command,
            GameProtocol.ExitType,
            [response.Succeeded ? (byte)0x01 : (byte)0x00]);

    private static byte[] EncodeGameLogin(
        GameLoginResponse response,
        GameLoginOptions options) => response.Succeeded
        ? GameLoginPayloadCodec.EncodeSuccess(options)
        : throw new InvalidOperationException("The observed Game LOGIN protocol has no failure body yet.");

    private static byte[] EncodeAccountCharacterRoster(AccountCharacterRosterResponse response) =>
        GameServerFrameEncoder.Encode(
            command: 0x00,
            GameProtocol.AccountCharacterRosterNotificationType,
            AccountCharacterRosterPayloadCodec.Encode(response.Roster));

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
